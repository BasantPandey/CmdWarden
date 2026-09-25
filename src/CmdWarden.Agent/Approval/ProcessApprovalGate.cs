using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using CmdWarden.Contracts;

namespace CmdWarden.Agent.Approval;

/// <summary>
/// Spawns the out-of-process Approval Gate helper and maps exit codes to outcomes.
/// Fail closed on missing helper, timeout, crash, or bad exit code.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessApprovalGate : IApprovalGate
{
    public const string HelperPathEnvVar = "CW_APPROVAL_GATE_PATH";
    public static readonly TimeSpan DefaultTimeout = ApprovalGateTimeouts.Gate;

    private readonly Func<string?> _resolveHelperPath;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly TimeSpan _timeout;

    public ProcessApprovalGate(
        Func<string?>? resolveHelperPath = null,
        Func<ProcessStartInfo, Process?>? startProcess = null,
        TimeSpan? timeout = null)
    {
        _resolveHelperPath = resolveHelperPath ?? (() => ApprovalGateLocator.FindHelperPath());
        _startProcess = startProcess ?? (psi => Process.Start(psi));
        _timeout = timeout ?? DefaultTimeout;
    }

    public ApprovalAnswer Prompt(ApprovalRequest request)
    {
        if (!OperatingSystem.IsWindows())
            return ApprovalOutcome.Unavailable;

        if (!Environment.UserInteractive)
            return ApprovalOutcome.Unavailable;

        var helperPath = _resolveHelperPath();
        if (string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath))
            return ApprovalOutcome.Unavailable;

        var payload = ApprovalPresentation.ToHelperPayload(request);
        var json = ApprovalHelperJson.Serialize(payload);

        string? tempFile = null;
        try
        {
            tempFile = Path.Combine(Path.GetTempPath(), "cw-approval-" + Guid.NewGuid().ToString("n") + ".json");
            File.WriteAllText(tempFile, json);

            var psi = new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = "--payload \"" + tempFile + "\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(helperPath) ?? Environment.CurrentDirectory,
            };

            Process? process;
            try
            {
                process = _startProcess(psi);
            }
            catch
            {
                return ApprovalOutcome.Unavailable;
            }

            if (process is null)
                return ApprovalOutcome.Unavailable;

            using (process)
            {
                if (!process.WaitForExit((int)Math.Min(_timeout.TotalMilliseconds, int.MaxValue)))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // ignore
                    }

                    return ApprovalOutcome.Unavailable;
                }

                return ApprovalHelperExitCodes.ToAnswer(process.ExitCode);
            }
        }
        catch
        {
            return ApprovalOutcome.Unavailable;
        }
        finally
        {
            if (tempFile is not null)
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }
}

/// <summary>
/// Resolves CmdWarden.ApprovalGate.exe for the process gate.
/// Packaged layout: agent/approval-gate/ next to Session Agent (ticket #81).
/// </summary>
public static class ApprovalGateLocator
{
    public const string HelperExeName = "CmdWarden.ApprovalGate.exe";
    public const string BundleFolderName = "approval-gate";

    /// <param name="baseDirectory">
    /// When set, only probe this root (and its agent/ subfolder) - no env fallback to other bases,
    /// no AppContext or repo walk. Used by tests and precise lookups.
    /// </param>
    public static string? FindHelperPath(string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            var fromEnv = Environment.GetEnvironmentVariable(ProcessApprovalGate.HelperPathEnvVar);
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv.Trim()))
                return Path.GetFullPath(fromEnv.Trim());
        }

        IEnumerable<string> bases;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            bases = new[] { baseDirectory };
        }
        else
        {
            bases = new[] { AppContext.BaseDirectory };
        }

        foreach (var b in bases)
        {
            var hit = ProbeBase(b);
            if (hit is not null)
                return hit;
        }

        // Dev tree walk only when no explicit baseDirectory was provided.
        if (!string.IsNullOrWhiteSpace(baseDirectory))
            return null;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "src", "CmdWarden.ApprovalGate", "bin", cfg, "net10.0-windows", HelperExeName);
                if (File.Exists(candidate))
                    return candidate;
            }

            if (File.Exists(Path.Combine(dir.FullName, "CmdWarden.sln")))
                break;
            dir = dir.Parent;
        }

        return null;
    }

    private static string? ProbeBase(string b)
    {
        var candidates = new[]
        {
            // Bundled next to agent: approval-gate/CmdWarden.ApprovalGate.exe
            Path.Combine(b, BundleFolderName, HelperExeName),
            // Same folder as agent binaries
            Path.Combine(b, HelperExeName),
            // CLI tool root: agent/approval-gate/
            Path.Combine(b, "agent", BundleFolderName, HelperExeName),
        };

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // next
            }
        }

        return null;
    }
}
