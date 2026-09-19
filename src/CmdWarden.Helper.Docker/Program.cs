using System.Text.Json;
using System.Text.Json.Serialization;
using Grpc.Core;
using CmdWarden.Contracts;

return await DockerHelperApp.RunAsync(args, Console.In, Console.Out);

/// <summary>
/// docker-credential-cmdwarden (#203). One argv action, stdin payload, protocol text on stdout.
/// docker reads stdout only and compares the not-found line byte for byte, so every line here
/// is exact. Human text goes to stdout too: docker shows it as the failure reason.
/// </summary>
public static class DockerHelperApp
{
    public const string Name = "docker-credential-cmdwarden";
    public const string NotFoundLine = "credentials not found in native keychain";
    public const string MissingUrlLine = "no credentials server URL";
    public const string MissingUsernameLine = "no credentials username";

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Wire shape of docker's Credentials struct.</summary>
    public sealed record Credentials(string? ServerURL, string? Username, string? Secret);

    public static async Task<int> RunAsync(string[] args, TextReader stdin, TextWriter stdout, string? pipeName = null)
    {
        if (args.Length == 1 && args[0] is "--version" or "-v" or "version")
        {
            stdout.WriteLine($"{Name} ({ProductInfo.Name}) {ProductInfo.Version}");
            return 0;
        }
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            stdout.WriteLine($"Usage: {Name} <store|get|erase|list|version>");
            return 0;
        }
        if (args.Length != 1)
        {
            stdout.WriteLine($"Usage: {Name} <store|get|erase|list|version>");
            return 1;
        }

        var action = args[0];
        if (action is not ("store" or "get" or "erase" or "list"))
        {
            stdout.WriteLine($"{Name}: unknown action: {action}");
            return 1;
        }

        var payload = (await stdin.ReadToEndAsync().ConfigureAwait(false)).Trim();
        string serverUrl = "", username = "", secret = "";
        if (action == "store")
        {
            var parsed = ParseStore(payload, out var error);
            if (parsed is null)
            {
                stdout.WriteLine(error);
                return 1;
            }
            (serverUrl, username, secret) = (parsed.ServerURL!, parsed.Username!, parsed.Secret ?? "");
        }
        else if (action != "list")
        {
            if (payload.Length == 0)
            {
                stdout.WriteLine(MissingUrlLine);
                return 1;
            }
            serverUrl = payload;
        }

        var up = false;
        try { up = (await AgentLifecycle.EnsureRunningAsync(pipeName).ConfigureAwait(false)).Up; } catch { /* down */ }
        if (!up)
        {
            stdout.WriteLine($"{ProductInfo.Name}: Session Agent not reachable. Start it with: cw agent start");
            return 1;
        }

        try
        {
            var response = await AgentHelperClient.CredentialAsync(
                "docker", action, serverUrl, username, secret, pipeName, ApprovalGateTimeouts.Client).ConfigureAwait(false);
            switch (action)
            {
                case "get":
                    stdout.WriteLine(JsonSerializer.Serialize(
                        new Credentials(serverUrl, response.Username, response.Secret), Json));
                    break;
                case "list":
                    stdout.WriteLine(JsonSerializer.Serialize(
                        response.Entries.ToDictionary(e => e.ServerUrl, e => e.Username), Json));
                    break;
            }
            return 0;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            stdout.WriteLine(NotFoundLine);
            return 1;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.PermissionDenied or StatusCode.FailedPrecondition)
        {
            stdout.WriteLine($"{ProductInfo.Name}: docker registry credential denied ({ReasonOf(ex.Status.Detail)})");
            return 1;
        }
        catch (RpcException ex)
        {
            stdout.WriteLine($"{ProductInfo.Name}: {ex.Status.Detail}");
            return 1;
        }
        catch (Exception ex)
        {
            stdout.WriteLine($"{ProductInfo.Name}: {ex.Message.Trim()}");
            return 1;
        }
    }

    /// <summary>docker's isValid: ServerURL and Username are required.</summary>
    public static Credentials? ParseStore(string json, out string error)
    {
        Credentials? creds;
        try
        {
            creds = JsonSerializer.Deserialize<Credentials>(json, Json);
        }
        catch (JsonException ex)
        {
            error = $"{Name}: invalid store payload: {ex.Message}";
            return null;
        }
        if (string.IsNullOrWhiteSpace(creds?.ServerURL))
        {
            error = MissingUrlLine;
            return null;
        }
        if (string.IsNullOrWhiteSpace(creds.Username))
        {
            error = MissingUsernameLine;
            return null;
        }
        error = "";
        return creds;
    }

    /// <summary>"HelperParentMissing: no pinned docker ..." to "HelperParentMissing".</summary>
    private static string ReasonOf(string detail)
    {
        var colon = detail.IndexOf(':');
        return colon > 0 ? detail[..colon] : detail;
    }
}
