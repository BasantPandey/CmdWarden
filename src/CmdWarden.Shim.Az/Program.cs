using System.Diagnostics;
using Grpc.Core;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

return await AzShimApp.RunAsync(args);

/// <summary>
/// PATH shim for az: Authorize with Session Agent, then spawn pinned real entrypoint (issue #45).
/// Gate only - no user-token env inject; ambient MSAL under ~/.azure remains the store.
/// </summary>
public static class AzShimApp
{
    public const int ExitAgentDown = 2;
    public const int ExitDenied = 3;
    public const int ExitSpawnFailed = 4;

    public static async Task<int> RunAsync(string[] args, string? pipeName = null, TimeSpan? timeout = null)
    {
        try
        {
            await TryEnsureAgentAsync(pipeName).ConfigureAwait(false);

            using var channel = AgentChannelFactory.Create(pipeName, timeout ?? TimeSpan.FromSeconds(3));
            var client = new SessionAgent.SessionAgentClient(channel);
            var request = new AuthorizeRequest { Tool = "az" };
            foreach (var a in args)
                request.Argv.Add(a);
            // #29: hashes only, so the Agent can spot a canary token in the env. Never values.
            request.EnvValueHashes.AddRange(ValueHash.OfEnvironment());

            using var cts = new CancellationTokenSource(timeout ?? ApprovalGateTimeouts.Client);
            var grant = await client.AuthorizeAsync(request, cancellationToken: cts.Token).ConfigureAwait(false);

            if (!grant.Allowed)
            {
                WriteStop(string.IsNullOrWhiteSpace(grant.Message) ? grant.ReasonCode : grant.Message,
                    string.IsNullOrWhiteSpace(grant.Message)
                        ? $"{ProductInfo.Name} az shim: denied ({grant.ReasonCode})"
                        : grant.Message);
                return ExitDenied;
            }

            if (string.IsNullOrWhiteSpace(grant.RealPath))
            {
                Console.Error.WriteLine($"{ProductInfo.Name} az shim: Grant missing real path.");
                return ExitDenied;
            }

            // #30: the binary the Agent approved is the binary that starts.
            using var locks = BoundFiles.LockVerified(grant.RealPath, grant.RealSha256);
            if (locks is null)
            {
                Console.Error.WriteLine($"{ProductInfo.Name}: {BoundFiles.ChangedMessage}: {grant.RealPath}. The az command did not run.");
                return ExitDenied;
            }
            return SpawnReal(grant.RealPath, args, grant.Env);
        }
        catch (RpcException ex)
        {
            WriteStop(ex.Status.Detail, $"{ProductInfo.Name} az shim: {ex.Status.Detail}");
            return ex.StatusCode is StatusCode.PermissionDenied or StatusCode.FailedPrecondition
                ? ExitDenied
                : ExitAgentDown;
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            Console.Error.WriteLine(
                $"{ProductInfo.Name} az shim: Session Agent not reachable. Start it with: cw agent start");
            return ExitAgentDown;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ProductInfo.Name} az shim failed: {ex.Message}");
            return ExitSpawnFailed;
        }
    }

    private static void WriteStop(string? detail, string fallback) =>
        Console.Error.WriteLine(ShimStopText.TryPlain("az", detail) ?? fallback);

    private static async Task TryEnsureAgentAsync(string? pipeName)
    {
        try
        {
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
    /// Supports pinned az.cmd / az.exe.
    /// </summary>
    public static int SpawnReal(
        string absolutePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> childEnv)
    {
        var full = Path.GetFullPath(absolutePath);
        if (!File.Exists(full))
        {
            Console.Error.WriteLine($"{ProductInfo.Name} az shim: real binary not found: {full}");
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
            Console.Error.WriteLine($"{ProductInfo.Name} az shim: failed to start {full}");
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
