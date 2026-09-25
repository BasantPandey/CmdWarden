using System.Diagnostics;
using Grpc.Core;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

return await GhShimApp.RunAsync(args);

/// <summary>
/// PATH shim for gh: Authorize with Session Agent, then spawn pinned real binary (issue #32).
/// </summary>
public static class GhShimApp
{
    public const int ExitAgentDown = 2;
    public const int ExitDenied = 3;
    public const int ExitSpawnFailed = 4;

    public static async Task<int> RunAsync(string[] args, string? pipeName = null, TimeSpan? timeout = null)
    {
        try
        {
            // Lazy-start Session Agent when possible (#36); still fail closed if start fails.
            await TryEnsureAgentAsync(pipeName).ConfigureAwait(false);

            using var channel = AgentChannelFactory.Create(pipeName, timeout ?? TimeSpan.FromSeconds(3));
            var client = new SessionAgent.SessionAgentClient(channel);
            var request = new AuthorizeRequest { Tool = "gh" };
            foreach (var a in args)
                request.Argv.Add(a);
            // #29: hashes only, so the Agent can spot a canary token in the env. Never values.
            request.EnvValueHashes.AddRange(ValueHash.OfEnvironment());
            request.AgentReason = AgentReason.FromEnvironment();
            // Strong gh routes GH_ENTERPRISE_TOKEN by GH_HOST (#208). Never a token.
            if (Environment.GetEnvironmentVariable("GH_HOST") is { Length: > 0 } ghHost)
                request.CallerEnv["GH_HOST"] = ghHost;

            using var cts = new CancellationTokenSource(timeout ?? ApprovalGateTimeouts.Client);
            var grant = await client.AuthorizeAsync(request, cancellationToken: cts.Token).ConfigureAwait(false);

            if (!grant.Allowed)
            {
                WriteStop(string.IsNullOrWhiteSpace(grant.Message) ? grant.ReasonCode : grant.Message,
                    string.IsNullOrWhiteSpace(grant.Message)
                        ? $"{ProductInfo.Name} gh shim: denied ({grant.ReasonCode})"
                        : grant.Message);
                return ExitDenied;
            }

            if (string.IsNullOrWhiteSpace(grant.RealPath))
            {
                Console.Error.WriteLine($"{ProductInfo.Name} gh shim: Grant missing real path.");
                return ExitDenied;
            }

            // #30: the binary the Agent approved is the binary that starts.
            using var locks = BoundFiles.LockVerified(grant.RealPath, grant.RealSha256);
            if (locks is null)
            {
                Console.Error.WriteLine($"{ProductInfo.Name}: {BoundFiles.ChangedMessage}: {grant.RealPath}. The gh command did not run.");
                return ExitDenied;
            }
            var exit = SpawnReal(grant.RealPath, args, grant.Env);
            if (exit == 0 && grant.MigrateAfterRun)
                await MigrateStoreAsync(args, pipeName, timeout).ConfigureAwait(false);
            return exit;
        }
        catch (RpcException ex)
        {
            WriteStop(ex.Status.Detail, $"{ProductInfo.Name} gh shim: {ex.Status.Detail}");
            return ex.StatusCode is StatusCode.PermissionDenied or StatusCode.FailedPrecondition
                ? ExitDenied
                : ExitAgentDown;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            Console.Error.WriteLine(
                $"{ProductInfo.Name} gh shim: Session Agent not reachable. Start it with: cw agent start");
            return ExitAgentDown;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ProductInfo.Name} gh shim failed: {ex.Message}");
            return ExitSpawnFailed;
        }
    }

    /// <summary>Strong gh (#208): the stock token a login just wrote moves into the vault. A failure keeps the child's exit code.</summary>
    private static async Task MigrateStoreAsync(string[] args, string? pipeName, TimeSpan? timeout)
    {
        try
        {
            await AgentMigrateClient.MigrateAsync("gh", args, pipeName, timeout ?? TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var detail = ex is RpcException rpc ? rpc.Status.Detail : ex.Message;
            Console.Error.WriteLine($"{ProductInfo.Name} gh shim: token migration failed: {detail}. Run cw harden gh --strong.");
        }
    }

    private static void WriteStop(string? detail, string fallback) =>
        Console.Error.WriteLine(ShimStopText.TryPlain("gh", detail) ?? fallback);

    private static async Task TryEnsureAgentAsync(string? pipeName)
    {
        try
        {
            // Shared start path with cw agent start / cw doctor (issue #36).
            // Failure is fail-closed on the subsequent Authorize / unreachable catch.
            _ = await AgentLifecycle.EnsureRunningAsync(pipeName).ConfigureAwait(false);
        }
        catch
        {
            // fail closed later on Authorize
        }
    }

    /// <summary>
    /// Spawn absolute path only - never PATH-search the tool name.
    /// Child gets env overlay; parent process environment is not modified.
    /// </summary>
    public static int SpawnReal(
        string absolutePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> childEnv)
    {
        var full = Path.GetFullPath(absolutePath);
        if (!File.Exists(full))
        {
            Console.Error.WriteLine($"{ProductInfo.Name} gh shim: real binary not found: {full}");
            return ExitSpawnFailed;
        }

        var psi = new ProcessStartInfo
        {
            FileName = full,
            UseShellExecute = false,
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in childEnv)
            psi.Environment[key] = value;

        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine($"{ProductInfo.Name} gh shim: failed to start {full}");
            return ExitSpawnFailed;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool IsUnreachable(Exception ex) =>
        ex is OperationCanceledException
            or TimeoutException
            or IOException
            or HttpRequestException
        || ex.InnerException is IOException or TimeoutException;
}
