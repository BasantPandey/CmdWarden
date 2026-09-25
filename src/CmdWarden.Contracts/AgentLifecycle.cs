using System.Diagnostics;
using Grpc.Core;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>
/// Start / stop / status for the Session Agent (issue #36).
/// Shared by CLI and shims (no dependency on CmdWarden.Cli).
/// </summary>
public static class AgentLifecycle
{
    public sealed record StatusResult(bool Up, int? ProcessId, string PipeName, string? Detail, bool AccessDenied = false);

    public const string AccessDeniedDetail =
        "Session Agent pipe exists but denied access: the agent runs elevated or as another user. "
        + "Stop it from that shell (cw agent stop), then run cw agent start here.";

    public static async Task<StatusResult> StatusAsync(
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var pipe = pipeName ?? AgentEndpoints.PipeName;
        try
        {
            using var channel = AgentChannelFactory.Create(pipe, timeout ?? TimeSpan.FromSeconds(2));
            var client = new SessionAgent.SessionAgentClient(channel);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
            var health = await client.GetHealthAsync(new HealthRequest(), cancellationToken: cts.Token)
                .ConfigureAwait(false);
            return new StatusResult(health.Alive, health.ProcessId, health.PipeName, null);
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            return IsAccessDenied(ex)
                ? new StatusResult(false, null, pipe, AccessDeniedDetail, AccessDenied: true)
                : new StatusResult(false, null, pipe, "Session Agent is not reachable.");
        }
    }

    /// <summary>
    /// CurrentUserOnly pipes reject a client from another elevation level or user. The pipe
    /// connect then throws UnauthorizedAccessException, buried under the gRPC transport error.
    /// </summary>
    public static bool IsAccessDenied(Exception? ex)
    {
        for (var e = ex; e is not null; e = e is RpcException rpc ? rpc.Status.DebugException : e.InnerException)
            if (e is UnauthorizedAccessException)
                return true;
        return false;
    }

    /// <summary>
    /// Start agent if not already up. Returns status after attempt.
    /// </summary>
    public static async Task<StatusResult> EnsureRunningAsync(
        string? pipeName = null,
        string? agentBinary = null,
        string? productRoot = null,
        TimeSpan? readyTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var current = await StatusAsync(pipeName, TimeSpan.FromMilliseconds(800), cancellationToken)
            .ConfigureAwait(false);
        if (current.Up)
            return current;

        return await StartAsync(pipeName, agentBinary, productRoot, readyTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<StatusResult> StartAsync(
        string? pipeName = null,
        string? agentBinary = null,
        string? productRoot = null,
        TimeSpan? readyTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var pipe = pipeName ?? AgentEndpoints.PipeName;
        var existing = await StatusAsync(pipe, TimeSpan.FromMilliseconds(800), cancellationToken)
            .ConfigureAwait(false);
        if (existing.Up)
            return existing with { Detail = "already running" };

        var binary = agentBinary ?? AgentLocator.FindAgentBinary();
        if (binary is null || !File.Exists(binary))
        {
            return new StatusResult(
                false,
                null,
                pipe,
                "Session Agent binary not found. Set CW_AGENT_PATH or install the CmdWarden dotnet tool / build the agent project.");
        }

        // Framework-dependent apphost requires sibling managed dll; prefer launching via dotnet.
        var binaryDir = Path.GetDirectoryName(binary) ?? ".";
        var siblingDll = Path.Combine(binaryDir, AgentLocator.AgentDllName);
        if (binary.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !File.Exists(siblingDll)
            && !LooksSelfContained(binaryDir))
        {
            return new StatusResult(
                false,
                null,
                pipe,
                "Session Agent layout is incomplete (apphost without CmdWarden.Agent.dll). Rebuild or set CW_AGENT_PATH to a full agent output folder.");
        }

        var root = productRoot ?? ProductPaths.Root();
        Directory.CreateDirectory(root);

        // Shell execute gives the agent no inherited handles. With CreateProcess, the long-lived agent
        // keeps the caller's stdout pipe open, and "cw doctor | Out-String" never ends.
        // Shell execute has no per-child environment, so the agent reads it from this process.
        var psi = new ProcessStartInfo
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = binaryDir,
        };

        if (binary.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || (binary.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(siblingDll)))
        {
            // Always host via dotnet when the managed assembly is present (reliable deps resolution).
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(File.Exists(siblingDll) && binary.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? siblingDll
                : binary);
        }
        else
        {
            psi.FileName = binary;
        }

        // CW_POLICY_PATH and CW_APPROVAL_MODE pass through as they are.
        Environment.SetEnvironmentVariable("CW_PIPE_NAME", pipe);
        Environment.SetEnvironmentVariable(ProductPaths.EnvVar, root);

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start Session Agent process.");
        }
        catch (Exception ex)
        {
            return new StatusResult(false, null, pipe, "Failed to start Session Agent: " + ex.Message);
        }

        try
        {
            await File.WriteAllTextAsync(AgentLocator.PidFilePath(root), process.Id.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // non-fatal
        }

        var deadline = DateTime.UtcNow + (readyTimeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return new StatusResult(
                    false,
                    null,
                    pipe,
                    $"Session Agent exited immediately (code {process.ExitCode}). Check CW_PIPE_NAME / policy path.");
            }

            var st = await StatusAsync(pipe, TimeSpan.FromMilliseconds(500), cancellationToken)
                .ConfigureAwait(false);
            if (st.Up)
                return st with { Detail = "started" };

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        return new StatusResult(
            false,
            process.HasExited ? null : process.Id,
            pipe,
            "Session Agent started but did not become healthy in time.");
    }

    public static async Task<StatusResult> StopAsync(
        string? pipeName = null,
        string? productRoot = null,
        CancellationToken cancellationToken = default)
    {
        var pipe = pipeName ?? AgentEndpoints.PipeName;
        var root = productRoot ?? ProductPaths.Root();
        int? pid = null;

        try
        {
            using var channel = AgentChannelFactory.Create(pipe, TimeSpan.FromSeconds(2));
            var client = new SessionAgent.SessionAgentClient(channel);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var health = await client.GetHealthAsync(new HealthRequest(), cancellationToken: cts.Token)
                .ConfigureAwait(false);
            if (health.ProcessId > 0)
                pid = health.ProcessId;
        }
        catch
        {
            // fall through to pid file
        }

        if (pid is null)
        {
            var pidFile = AgentLocator.PidFilePath(root);
            if (File.Exists(pidFile)
                && int.TryParse((await File.ReadAllTextAsync(pidFile, cancellationToken).ConfigureAwait(false)).Trim(), out var filePid))
            {
                pid = filePid;
            }
        }

        if (pid is null)
            return new StatusResult(false, null, pipe, "Session Agent is not running (no process to stop).");

        try
        {
            using var proc = Process.GetProcessById(pid.Value);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // already gone
        }
        catch (Exception ex)
        {
            return new StatusResult(false, pid, pipe, "Failed to stop Session Agent: " + ex.Message);
        }

        try
        {
            var pidFile = AgentLocator.PidFilePath(root);
            if (File.Exists(pidFile))
                File.Delete(pidFile);
        }
        catch
        {
            // ignore
        }

        var after = await StatusAsync(pipe, TimeSpan.FromMilliseconds(800), cancellationToken)
            .ConfigureAwait(false);
        if (after.Up)
            return after with { Detail = "stop requested but agent still reports healthy" };

        return new StatusResult(false, null, pipe, "stopped");
    }

    private static bool IsUnreachable(Exception ex) =>
        ex is RpcException
        {
            StatusCode: StatusCode.Unavailable
                or StatusCode.DeadlineExceeded
                or StatusCode.Internal
                or StatusCode.Cancelled
                or StatusCode.Unknown
        }
        or OperationCanceledException
        or TimeoutException
        or IOException
        or HttpRequestException;

    private static bool LooksSelfContained(string binaryDir)
    {
        // Heuristic: self-contained publishes ship hostfxr / coreclr next to the apphost.
        return File.Exists(Path.Combine(binaryDir, "hostfxr.dll"))
            || File.Exists(Path.Combine(binaryDir, "coreclr.dll"));
    }
}
