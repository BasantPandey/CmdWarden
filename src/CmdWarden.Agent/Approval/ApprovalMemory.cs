using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CmdWarden.Contracts;

namespace CmdWarden.Agent.Approval;

/// <summary>
/// In-memory memory of human Approval Gate decisions (#131, #132). Two entry kinds:
/// a transient entry reuses an exact request's outcome while the launcher process lives; a session grant
/// lets a launcher process pass a command class (and lower) for one tool + secret until the
/// launcher exits or goes idle. Never persisted: agent restart forgets everything.
/// </summary>
public sealed class ApprovalMemory
{
    public const string TransientWindowEnvVar = "CW_TRANSIENT_REUSE_SECONDS";
    public const string SessionIdleEnvVar = "CW_SESSION_IDLE_SECONDS";
    /// <summary>No time cap: a transient entry lives as long as its launcher process (one session).</summary>
    public static readonly TimeSpan DefaultTransientWindow = TimeSpan.MaxValue;
    public static readonly TimeSpan DefaultSessionIdle = TimeSpan.FromMinutes(60);

    private readonly ConcurrentDictionary<string, TransientEntry> _transient = new();
    private readonly ConcurrentDictionary<string, SessionGrant> _sessions = new();
    private readonly ConcurrentDictionary<int, RunRecord> _runs = new();
    private readonly TimeSpan _transientWindow;
    private readonly TimeSpan _sessionIdle;

    public ApprovalMemory(TimeSpan? transientWindow = null, TimeSpan? sessionIdle = null)
    {
        _transientWindow = transientWindow ?? WindowFromEnvironment(TransientWindowEnvVar, DefaultTransientWindow);
        _sessionIdle = sessionIdle ?? WindowFromEnvironment(SessionIdleEnvVar, DefaultSessionIdle);
    }

    public static TimeSpan WindowFromEnvironment(string envVar, TimeSpan fallback) =>
        double.TryParse(
            Environment.GetEnvironmentVariable(envVar),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;

    /// <summary>UTC start time of a live process, or null when it is gone or inaccessible.</summary>
    public static DateTime? ProcessStartUtc(int pid)
    {
        if (pid <= 0)
            return null;
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception)
        {
            // ArgumentException / InvalidOperationException: dead. Win32Exception: no access. All miss.
            return null;
        }
    }

    /// <summary>True when <paramref name="pid"/> is alive and started at <paramref name="startUtc"/>.</summary>
    private static bool IsLive(int pid, DateTime startUtc) =>
        ProcessStartUtc(pid) is { } live && Math.Abs((live - startUtc).TotalSeconds) <= 1;

    // ---- transient (#131) ----

    /// <summary>
    /// Key for one exact request from one launcher. The launcher (harness or terminal) is the
    /// stable process; a shim client pid is fresh per call and would never match. Null when the
    /// launcher pid is unknown or its process is gone, so the lookup counts as a miss. Start
    /// time in the key defeats pid reuse.
    /// </summary>
    public static string? TransientKey(ApprovalRequest request)
    {
        if (request.LauncherPid is not { } launcherPid || ProcessStartUtc(launcherPid) is not { } start)
            return null;
        // ponytail: newline-joined string; pid + start time bound any field-boundary collision to one process.
        return string.Join('\n', launcherPid, start.Ticks, request.LauncherPolicyKey, request.Tool,
            request.CommandClass, request.SecretName, request.CommandLine ?? "");
    }

    public ApprovalOutcome? TryGetTransient(string? key)
    {
        if (key is null || !_transient.TryGetValue(key, out var entry))
            return null;
        if (entry.ExpiresUtc > DateTime.UtcNow)
            return entry.Outcome;
        _transient.TryRemove(key, out _);
        return null;
    }

    /// <summary>Only human outcomes are remembered; unavailable gates are recomputed every call.</summary>
    public void RememberTransient(string? key, ApprovalOutcome outcome, string launcherPolicyKey, string tool)
    {
        if (key is null || outcome is not (ApprovalOutcome.AllowOnce or ApprovalOutcome.Deny))
            return;
        // The key starts with "<pid>\n<startTicks>" (see TransientKey); the sweep needs both.
        // A key in another shape (unit tests) gets pid 0, which the sweep treats as dead.
        var head = key.Split('\n', 3);
        _ = int.TryParse(head[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid);
        var ticks = head.Length > 1 && long.TryParse(head[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0;
        var start = new DateTime(Math.Clamp(ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc);
        // ponytail: dead-launcher entries linger until this sweep; 256 bounds the map.
        if (_transient.Count >= 256)
            RemoveTransient(e => !IsLive(e.LauncherPid, e.LauncherStartUtc));
        var expires = _transientWindow == TimeSpan.MaxValue ? DateTime.MaxValue : DateTime.UtcNow + _transientWindow;
        _transient[key] = new TransientEntry(pid, start, launcherPolicyKey, tool, outcome, expires);
    }

    private sealed record TransientEntry(
        int LauncherPid,
        DateTime LauncherStartUtc,
        string LauncherPolicyKey,
        string Tool,
        ApprovalOutcome Outcome,
        DateTime ExpiresUtc);

    // ---- session allow (#132) ----

    private static string SessionKey(int launcherPid, long startTicks, string tool, string secretName) =>
        string.Join('\n', launcherPid, startTicks, tool, secretName);

    /// <summary>
    /// Record a human decision that lasts the launcher session. "Allow for session" covers the
    /// granted class and every class below it; "Approve Once" covers that one class (#205).
    /// A later decision adds to the grant it finds, so a narrow answer never takes coverage away.
    /// Null when the launcher process cannot be bound (dead, or the chain reported a different
    /// start time than the live process) or when nothing is left to cover (secret-reveal).
    /// </summary>
    public SessionGrant? Grant(
        int launcherPid,
        DateTime? launcherStartUtc,
        string launcherPolicyKey,
        string launcherKind,
        string tool,
        string secretName,
        CommandClass grantedClass,
        bool exactClass = false)
    {
        if (ProcessStartUtc(launcherPid) is not { } liveStart)
            return null;
        if (launcherStartUtc is { } claimed && !IsLive(launcherPid, claimed))
            return null;
        var mask = exactClass ? ClassBit(grantedClass) : RangeMask(grantedClass);
        if (mask == 0)
            return null;
        var now = DateTime.UtcNow;
        var key = SessionKey(launcherPid, liveStart.Ticks, tool, secretName);
        var grant = _sessions.TryGetValue(key, out var live) && IsLive(live.LauncherPid, live.LauncherStartUtc)
            ? live with { ClassMask = live.ClassMask | mask, LastUsedUtc = now }
            : new SessionGrant(
                Id: Guid.NewGuid().ToString("N")[..8],
                LauncherPid: launcherPid,
                LauncherStartUtc: liveStart,
                LauncherPolicyKey: launcherPolicyKey,
                LauncherKind: launcherKind,
                Tool: tool,
                SecretName: secretName,
                ClassMask: mask,
                GrantedAtUtc: now,
                LastUsedUtc: now);
        _sessions[key] = grant;
        return grant;
    }

    /// <summary>
    /// Live grant covering <paramref name="requested"/> for this launcher process, tool and secret.
    /// Touches last-used on a hit. Dead, restarted, or idle launchers drop the entry and miss.
    /// </summary>
    public SessionGrant? TryUseSession(int launcherPid, string tool, string secretName, CommandClass requested)
    {
        if (ProcessStartUtc(launcherPid) is not { } start)
            return null;
        var key = SessionKey(launcherPid, start.Ticks, tool, secretName);
        if (!_sessions.TryGetValue(key, out var grant))
            return null;
        var now = DateTime.UtcNow;
        if (grant.LastUsedUtc + _sessionIdle <= now)
        {
            _sessions.TryRemove(key, out _);
            return null;
        }
        if ((grant.ClassMask & ClassBit(requested)) == 0)
            return null;
        var touched = grant with { LastUsedUtc = now };
        _sessions[key] = touched;
        return touched;
    }

    // ---- run record (#202) ----

    /// <summary>
    /// A shim grant for <paramref name="tool"/> is live while the shim process lives. A credential
    /// helper whose chain passes that pid is covered at class read.
    /// ponytail: bound to the shim pid, not the child pid the shim spawns next. The helper chain
    /// passes both, and the shim exits with its child.
    /// </summary>
    public void RecordRun(int shimPid, bool pidFromPipe, string launcherPolicyKey, string tool)
    {
        if (!pidFromPipe || ProcessStartUtc(shimPid) is not { } start)
            return;
        // ponytail: dead records linger until this sweep or a lookup hits them; 256 bounds the map.
        if (_runs.Count >= 256)
            RemoveRuns(r => !IsLive(r.Pid, r.StartUtc));
        _runs[shimPid] = new RunRecord(shimPid, start, launcherPolicyKey, tool);
    }

    /// <summary>True when one pid in <paramref name="chainPids"/> holds a live run record for this tool.</summary>
    public bool IsRunCovered(IEnumerable<int> chainPids, string tool)
    {
        var covered = false;
        foreach (var pid in chainPids)
        {
            if (!_runs.TryGetValue(pid, out var run))
                continue;
            if (!IsLive(pid, run.StartUtc))
            {
                _runs.TryRemove(pid, out _);
                continue;
            }
            if (Matches(run.Tool, tool))
                covered = true;
        }
        return covered;
    }

    private sealed record RunRecord(int Pid, DateTime StartUtc, string LauncherPolicyKey, string Tool);

    // ---- listing & revocation (#134) ----

    /// <summary>
    /// Live session grants, pruned of launchers that are dead, restarted, or idle past the window.
    /// Transient entries are never included — command lines stay out of every list.
    /// </summary>
    public IReadOnlyList<SessionGrant> ListSessions()
    {
        var now = DateTime.UtcNow;
        var live = new List<SessionGrant>();
        foreach (var (key, grant) in _sessions)
        {
            if (grant.LastUsedUtc + _sessionIdle <= now || !IsLive(grant.LauncherPid, grant.LauncherStartUtc))
            {
                _sessions.TryRemove(key, out _);
                continue;
            }
            live.Add(grant);
        }
        return live;
    }

    /// <summary>Instant this grant expires if left unused (last use + the idle window).</summary>
    public DateTime IdleExpiresUtc(SessionGrant grant) => grant.LastUsedUtc + _sessionIdle;

    /// <summary>Withdraw one grant by id. Returns the count removed (0 or 1).</summary>
    public int RevokeSession(string id)
    {
        foreach (var (key, grant) in _sessions)
        {
            if (string.Equals(grant.Id, id, StringComparison.OrdinalIgnoreCase))
                return _sessions.TryRemove(key, out _) ? 1 : 0;
        }
        return 0;
    }

    /// <summary>Withdraw every session grant. Returns the count removed.</summary>
    public int RevokeAllSessions()
    {
        var removed = 0;
        foreach (var key in _sessions.Keys.ToList())
        {
            if (_sessions.TryRemove(key, out _))
                removed++;
        }
        return removed;
    }

    /// <summary>Granted class covers itself and every lower class; secret-reveal is never covered.</summary>
    public static bool Covers(CommandClass granted, CommandClass requested) =>
        (RangeMask(granted) & ClassBit(requested)) != 0;

    /// <summary>One bit per coverable class. Secret-reveal has no bit, so no grant ever covers it.</summary>
    internal static int ClassBit(CommandClass c) => c switch
    {
        CommandClass.Read => 1,
        CommandClass.Write => 2,
        CommandClass.Unknown => 4,
        _ => 0,
    };

    /// <summary>This class and every lower one.</summary>
    private static int RangeMask(CommandClass top) => ClassBit(top) switch
    {
        1 => 1,
        2 => 3,
        4 => 7,
        _ => 0,
    };

    public TimeSpan SessionIdle => _sessionIdle;

    // ---- clearing (#133): memory never outlives the conditions it was granted under ----

    /// <summary>Unenroll: every entry bound to this launcher's policy key, any tool.</summary>
    public int ClearForLauncherKey(string launcherPolicyKey) =>
        RemoveTransient(e => Matches(e.LauncherPolicyKey, launcherPolicyKey))
        + RemoveSessions(g => Matches(g.LauncherPolicyKey, launcherPolicyKey))
        + RemoveRuns(r => Matches(r.LauncherPolicyKey, launcherPolicyKey));

    /// <summary>Re-harden: every entry for this tool, any launcher.</summary>
    public int ClearForTool(string tool) =>
        RemoveTransient(e => Matches(e.Tool, tool))
        + RemoveSessions(g => Matches(g.Tool, tool))
        + RemoveRuns(r => Matches(r.Tool, tool));

    /// <summary>Policy set: entries whose tool or launcher key matches the changed pair.</summary>
    public int ClearForPolicyChange(string launcherPolicyKey, string tool) =>
        RemoveTransient(e => Matches(e.LauncherPolicyKey, launcherPolicyKey) || Matches(e.Tool, tool))
        + RemoveSessions(g => Matches(g.LauncherPolicyKey, launcherPolicyKey) || Matches(g.Tool, tool))
        + RemoveRuns(r => Matches(r.LauncherPolicyKey, launcherPolicyKey) || Matches(r.Tool, tool));

    /// <summary>Workstation lock or agent shutdown: everything.</summary>
    public int ClearAll()
    {
        var removed = _transient.Count + _sessions.Count + _runs.Count;
        _transient.Clear();
        _sessions.Clear();
        _runs.Clear();
        return removed;
    }

    private static bool Matches(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private int RemoveTransient(Func<TransientEntry, bool> predicate)
    {
        var keys = _transient.Where(kv => predicate(kv.Value)).Select(kv => kv.Key).ToList();
        foreach (var key in keys)
            _transient.TryRemove(key, out _);
        return keys.Count;
    }

    private int RemoveRuns(Func<RunRecord, bool> predicate)
    {
        var keys = _runs.Where(kv => predicate(kv.Value)).Select(kv => kv.Key).ToList();
        foreach (var key in keys)
            _runs.TryRemove(key, out _);
        return keys.Count;
    }

    private int RemoveSessions(Func<SessionGrant, bool> predicate)
    {
        var keys = _sessions.Where(kv => predicate(kv.Value)).Select(kv => kv.Key).ToList();
        foreach (var key in keys)
            _sessions.TryRemove(key, out _);
        return keys.Count;
    }
}

/// <summary>One session grant. Names only, never secret values.</summary>
public sealed record SessionGrant(
    string Id,
    int LauncherPid,
    DateTime LauncherStartUtc,
    string LauncherPolicyKey,
    string LauncherKind,
    string Tool,
    string SecretName,
    int ClassMask,
    DateTime GrantedAtUtc,
    DateTime LastUsedUtc)
{
    /// <summary>Highest class this grant covers, for display and audit.</summary>
    public CommandClass GrantedClass =>
        (ClassMask & ApprovalMemory.ClassBit(CommandClass.Unknown)) != 0 ? CommandClass.Unknown
        : (ClassMask & ApprovalMemory.ClassBit(CommandClass.Write)) != 0 ? CommandClass.Write
        : CommandClass.Read;
}
