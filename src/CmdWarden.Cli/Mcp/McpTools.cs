using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Cli.Hooks;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Mcp;

/// <summary>
/// The CmdWarden MCP tools (#34): run_with_secret, list_allowed, and why_denied. They use the Session
/// Agent calls that the shims and cw inject use. No secret value goes back to the model: each output
/// passes the leak guard first, and an output that the leak guard cannot check is held back.
/// </summary>
public static class McpTools
{
    public const string Instructions =
        "CmdWarden gates CLI tools (gh, git, az, docker) and vault secrets by the app that runs them. " +
        "Use list_allowed to see what runs without a prompt. Use run_with_secret to run a command that needs a " +
        "vault secret; give a short reason, because the person sees it in the approval popup. After a deny, call " +
        "why_denied, tell the person, and do not retry with another command.";

    public const int MaxOutputChars = 64 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<McpTool> All(string? pipeName = null, string? productRoot = null, IReadOnlyList<string>? cw = null) =>
    [
        new("run_with_secret",
            "Run one program through the CmdWarden gate and return its exit code and output. Hardened tools " +
            "(gh, git, az, docker) get their credential from the gate. Name vault secrets in 'secrets' to put them " +
            "in the environment of the program only. Secret values in the output come back as [CmdWarden: NAME]. " +
            "The person may see an approval popup; the call waits for the answer.",
            Schema(new JsonObject
            {
                ["program"] = Prop("string", "The program, for example gh. No shell: pipes and && do not work."),
                ["args"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Arguments, one per item." },
                ["secrets"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Vault secret names to put in the environment, for example NPM_TOKEN." },
                ["reason"] = Prop("string", $"Why you run this, in {AgentReason.MaxLength} characters or less. The person sees it."),
                ["cwd"] = Prop("string", "Working folder. The default is the folder of the MCP server."),
                ["timeout_seconds"] = Prop("integer", "Stop the program after this time. Default 120, maximum 600."),
            }, "program"),
            (args, ct) => RunWithSecretAsync(args, pipeName, ct, cw)),
        new("list_allowed",
            "List the tools CmdWarden gates for you, their policy level, which command classes run with no prompt, " +
            "and the names (never values) of the vault secrets.",
            Schema(new JsonObject()),
            (_, ct) => ListAllowedAsync(pipeName, productRoot, ct)),
        new("why_denied",
            "Explain the last CmdWarden deny for you: when, which tool and command class, and why.",
            Schema(new JsonObject()),
            (_, ct) => WhyDeniedAsync(pipeName, productRoot, ct)),
    ];

    private static JsonObject Prop(string type, string description) =>
        new() { ["type"] = type, ["description"] = description };

    private static JsonObject Schema(JsonObject properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0)
            schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        return schema;
    }

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray a ? a.Select(n => (string?)n ?? throw new ArgumentException("Each item must be a string.")).ToList() : [];

    public static async Task<McpToolResult> RunWithSecretAsync(JsonObject args, string? pipeName, CancellationToken ct,
        IReadOnlyList<string>? cw = null)
    {
        var program = ((string?)args["program"])?.Trim();
        if (string.IsNullOrEmpty(program))
            throw new ArgumentException("program is required.");
        var arguments = Strings(args["args"]);
        var secrets = Strings(args["secrets"]);
        var seconds = args["timeout_seconds"] is JsonValue t && t.TryGetValue<int>(out var s) ? Math.Clamp(s, 1, 600) : (int)DefaultTimeout.TotalSeconds;

        var self = cw ?? HookInstaller.SelfCommandParts();
        var psi = new ProcessStartInfo
        {
            FileName = secrets.Count > 0 ? self[0] : program,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if ((string?)args["cwd"] is { Length: > 0 } cwd)
            psi.WorkingDirectory = Directory.Exists(cwd) ? cwd : throw new ArgumentException($"cwd does not exist: {cwd}");
        if (secrets.Count > 0)
        {
            foreach (var part in self.Skip(1))
                psi.ArgumentList.Add(part);
            psi.ArgumentList.Add("inject");
            foreach (var name in secrets)
                psi.ArgumentList.Add("+" + name);
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(program);
        }
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        if (AgentReason.Clean((string?)args["reason"]) is { } reason)
            psi.Environment[AgentReason.EnvVar] = reason;
        if (pipeName is not null)
            psi.Environment["CW_PIPE_NAME"] = pipeName;

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {program}.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new McpToolResult($"Could not start {program}: {ex.Message}", IsError: true);
        }
        using (process)
        {
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var texts = new[] { Cap(await stdout.ConfigureAwait(false)), Cap(await stderr.ConfigureAwait(false)) };
            LeakCheck checkedTexts;
            try
            {
                var r = await AgentLeakClient.CheckAsync(texts, "mcp:run_with_secret", pipeName, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                checkedTexts = new LeakCheck(r.Texts, r.Names);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new McpToolResult(
                    $"{ProductInfo.Name} could not check the output for secret values, so it holds the output back. " +
                    $"Exit code {(timedOut ? "none (timed out)" : process.ExitCode)}. Start the Session Agent with: cw agent start",
                    IsError: true);
            }

            var sb = new StringBuilder();
            sb.AppendLine(timedOut ? $"Stopped after {seconds} s." : $"Exit code {process.ExitCode}.");
            if (checkedTexts.Names.Count > 0)
                sb.AppendLine(LeakGuardHook.Note(checkedTexts.Names));
            if (checkedTexts.Texts[0].Length > 0)
                sb.AppendLine("--- stdout").Append(checkedTexts.Texts[0].TrimEnd()).AppendLine();
            if (checkedTexts.Texts[1].Length > 0)
                sb.AppendLine("--- stderr").Append(checkedTexts.Texts[1].TrimEnd()).AppendLine();
            if (!timedOut && process.ExitCode != 0 && checkedTexts.Texts[1].Contains(ProductInfo.Name, StringComparison.Ordinal))
                sb.AppendLine("If CmdWarden denied this, call why_denied, then ask the person. Do not retry with another command.");
            return new McpToolResult(sb.ToString().TrimEnd(), IsError: timedOut || process.ExitCode != 0);
        }
    }

    private static string Cap(string text) =>
        text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + $"{Environment.NewLine}[{text.Length - MaxOutputChars} more characters cut]";

    public static async Task<McpToolResult> ListAllowedAsync(string? pipeName, string? productRoot, CancellationToken ct)
    {
        var sb = new StringBuilder();
        string? launcher = null;
        foreach (var tool in ToolCatalog.All(productRoot))
        {
            if (HardenedToolStatus.Probe(tool.Id, productRoot).State == HardenState.NotHardened)
            {
                sb.AppendLine($"{tool.Id}: not hardened. CmdWarden does not gate it.");
                continue;
            }
            var r = await AgentPolicyClient.CheckAsync(tool.Id, [], pipeName, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            launcher ??= r.LauncherPolicyKey;
            var level = PolicyLevelNames.ParseOrDeny(r.PolicyLevel);
            var classes = new[] { CommandClass.Read, CommandClass.Write, CommandClass.SecretReveal, CommandClass.Unknown };
            var free = classes.Where(c => PolicyEvaluator.IsAutoAllowed(level, c)).Select(CommandClassNames.Format).ToList();
            var ask = classes.Where(c => !PolicyEvaluator.IsAutoAllowed(level, c)).Select(CommandClassNames.Format).ToList();
            sb.AppendLine($"{tool.Id}: level {r.PolicyLevel}. No prompt: {(free.Count == 0 ? "none" : string.Join(", ", free))}. " +
                          $"Approval popup: {(ask.Count == 0 ? "none" : string.Join(", ", ask))}.");
        }
        var names = await AgentVaultClient.ListSecretNamesAsync(pipeName, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        sb.AppendLine($"Vault secrets (names only): {(names.Count == 0 ? "none" : string.Join(", ", names.Order(StringComparer.OrdinalIgnoreCase)))}.");
        if (launcher is not null)
            sb.Insert(0, $"You run as launcher {launcher}.{Environment.NewLine}");
        return new McpToolResult(sb.ToString().TrimEnd());
    }

    public static async Task<McpToolResult> WhyDeniedAsync(string? pipeName, string? productRoot, CancellationToken ct)
    {
        var launcher = (await AgentPolicyClient.CheckAsync("gh", [], pipeName, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false)).LauncherPolicyKey;
        var last = new AuditLog(productRoot).ReadRecentRecords(1000).Records.FirstOrDefault(r =>
            r.Decision is GateDecisions.Deny or GateDecisions.Unavailable
            && string.Equals(r.LauncherPolicyKey, launcher, StringComparison.OrdinalIgnoreCase));
        if (last is null)
            return new McpToolResult($"No deny for your launcher ({launcher}) in the audit trail of the last 30 days.");
        var what = $"{last.Tool}{(last.CommandClass.Length > 0 ? " " + last.CommandClass : "")}" +
                   (string.IsNullOrEmpty(last.SecretName) ? "" : $" with {last.SecretName}");
        var said = AgentReason.Clean(last.AgentReason) is { } r ? $" You said: \"{r}\"." : "";
        return new McpToolResult(
            $"At {last.Timestamp?.ToLocalTime():yyyy-MM-dd HH:mm:ss}, CmdWarden denied {what} (policy level {last.PolicyLevel}).{said} " +
            Explain(last.ReasonCode, last.Tool) + " Tell the person. Do not retry with another command.");
    }

    public static string Explain(string? reasonCode, string tool) => reasonCode switch
    {
        PolicyReasonCodes.UserDenied => "The person clicked Deny in the approval popup.",
        PolicyReasonCodes.DenyCooldown => $"The person denied {tool} a short time ago, so CmdWarden blocks new tries for a few minutes.",
        PolicyReasonCodes.HelloCanceled => "The person cancelled Windows Hello, so the approval did not count.",
        PolicyReasonCodes.ApprovalUnavailable => "The approval popup could not open, or nobody answered in time. CmdWarden fails closed.",
        PolicyReasonCodes.NotEnrolled or PolicyReasonCodes.UnknownLauncher =>
            "Your launcher is not enrolled, and the approval popup could not open. The person can run: cw policy enroll --kind ai-harness",
        PolicyReasonCodes.PinMissing => $"{tool} is not hardened. The person can run: cw harden {tool}",
        PolicyReasonCodes.PinMismatch => $"The {tool} binary changed since the person hardened it. The person can run: cw harden {tool}",
        PolicyReasonCodes.CanaryHit => "A canary token was used. CmdWarden treats this as an attack and blocks your launcher.",
        PolicyReasonCodes.ScriptChanged => "A script or binary changed after the person approved it.",
        PolicyReasonCodes.HiddenCommand => "A PowerShell command that CmdWarden cannot read started the tool.",
        _ => $"Reason code: {reasonCode ?? "none"}.",
    };
}
