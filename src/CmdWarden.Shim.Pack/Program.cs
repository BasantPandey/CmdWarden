using System.Diagnostics;
using Grpc.Core;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

return await PackShimApp.RunAsync(Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? ""), args);

/// <summary>
/// PATH shim for every tool pack (#37). cw harden copies this exe as &lt;tool&gt;.exe, so the file
/// name is the tool. Authorize with the Session Agent, then start the pinned real tool.
/// </summary>
public static class PackShimApp
{
    public const string ExeName = "cw-pack-shim";
    public const int ExitAgentDown = 2;
    public const int ExitDenied = 3;
    public const int ExitSpawnFailed = 4;

    public static async Task<int> RunAsync(string tool, string[] args, string? pipeName = null, TimeSpan? timeout = null)
    {
        tool = tool.ToLowerInvariant();
        if (tool == ExeName || !ToolPacks.IsValidToolName(tool))
        {
            Console.Error.WriteLine($"{ProductInfo.Name}: {ExeName} runs as <tool>.exe. Run cw harden <tool> to install it.");
            return ExitSpawnFailed;
        }
        try
        {
            try
            {
                _ = await AgentLifecycle.EnsureRunningAsync(pipeName).ConfigureAwait(false);
            }
            catch
            {
                // Fail closed later on Authorize.
            }

            using var channel = AgentChannelFactory.Create(pipeName, timeout ?? TimeSpan.FromSeconds(3));
            var client = new SessionAgent.SessionAgentClient(channel);
            var request = new AuthorizeRequest { Tool = tool };
            request.Argv.AddRange(args);
            // #29: hashes only, so the Agent can spot a canary token in the env. Never values.
            request.EnvValueHashes.AddRange(ValueHash.OfEnvironment());
            request.AgentReason = AgentReason.FromEnvironment();
            request.WorkingDirectory = Environment.CurrentDirectory;

            using var cts = new CancellationTokenSource(timeout ?? ApprovalGateTimeouts.Client);
            var grant = await client.AuthorizeAsync(request, cancellationToken: cts.Token).ConfigureAwait(false);
            if (!grant.Allowed || string.IsNullOrWhiteSpace(grant.RealPath))
            {
                WriteStop(tool, grant.Message, $"{ProductInfo.Name} {tool} shim: denied ({grant.ReasonCode})");
                return ExitDenied;
            }

            // #30: the binary the Agent approved is the binary that starts.
            using var locks = BoundFiles.LockVerified(grant.RealPath, grant.RealSha256);
            if (locks is null)
            {
                Console.Error.WriteLine($"{ProductInfo.Name}: {BoundFiles.ChangedMessage}: {grant.RealPath}. The {tool} command did not run.");
                return ExitDenied;
            }
            return SpawnReal(tool, grant.RealPath, args, grant.Env, grant.StripEnv);
        }
        catch (RpcException ex)
        {
            // #36: an agent account that the person did not enroll cannot open the agent pipe.
            if (AgentEndpoints.RunsAsOtherAccount && AgentLifecycle.IsAccessDenied(ex))
            {
                Console.Error.WriteLine(ShimStopText.AccountRefused());
                return ExitAgentDown;
            }
            WriteStop(tool, ex.Status.Detail, $"{ProductInfo.Name} {tool} shim: {ex.Status.Detail}");
            return ex.StatusCode is StatusCode.PermissionDenied or StatusCode.FailedPrecondition ? ExitDenied : ExitAgentDown;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or HttpRequestException
                                   || ex.InnerException is IOException or TimeoutException)
        {
            Console.Error.WriteLine($"{ProductInfo.Name} {tool} shim: Session Agent not reachable. Start it with: cw agent start");
            return ExitAgentDown;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ProductInfo.Name} {tool} shim failed: {ex.Message}");
            return ExitSpawnFailed;
        }
    }

    private static void WriteStop(string tool, string? detail, string fallback) =>
        Console.Error.WriteLine(ShimStopText.TryPlain(tool, string.IsNullOrWhiteSpace(detail) ? null : detail) ?? fallback);

    /// <summary>Absolute path only, never a PATH search. The env overlay goes to the child only.</summary>
    public static int SpawnReal(string tool, string absolutePath, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> childEnv, IEnumerable<string> stripEnv)
    {
        var full = Path.GetFullPath(absolutePath);
        if (!File.Exists(full))
        {
            Console.Error.WriteLine($"{ProductInfo.Name} {tool} shim: real binary not found: {full}");
            return ExitSpawnFailed;
        }

        ProcessStartInfo psi;
        try
        {
            psi = ToolProcess.StartInfo(full, arguments);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"{ProductInfo.Name} {tool} shim: {ex.Message}");
            return ExitDenied;
        }
        foreach (var key in stripEnv)
            psi.Environment.Remove(key);
        foreach (var (key, value) in childEnv)
            psi.Environment[key] = value;

        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine($"{ProductInfo.Name} {tool} shim: failed to start {full}");
            return ExitSpawnFailed;
        }
        process.WaitForExit();
        return process.ExitCode;
    }
}
