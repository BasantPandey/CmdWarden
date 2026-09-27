using System.Runtime.Versioning;
using System.Text;
using CmdWarden.Cli;

using CmdWarden.Cli.Hooks;
using CmdWarden.Cli.Launch;
using CmdWarden.Cli.Mcp;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Ssh;
using Microsoft.Win32;
using Spectre.Console;

/// <summary><c>cw setup</c> and <c>cw try</c> (#65): from install to a first block in one command.</summary>
public static partial class CliApp
{
    public const string TryTool = "try";
    public const string TrySecret = "CW_TRY_TOKEN";
    public const string TryAgentEnvVar = "CW_TRY_AGENT_PATH";

    private static readonly string[] SetupSteps = ["terminal", "tools", "harnesses", "shortcuts", "canary", "protect", "try"];

    private sealed record StepResult(string Step, string State, string Detail);

    private static async Task<int> SetupAsync(string[] args)
    {
        var yes = false;
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--yes" or "-y":
                    yes = true;
                    break;
                case "--skip" when i + 1 < args.Length:
                    foreach (var step in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!SetupSteps.Contains(step, StringComparer.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine($"Unknown setup step: {step}. Steps: {string.Join(", ", SetupSteps)}.");
                            return 1;
                        }
                        skip.Add(step);
                    }
                    break;
                case var a when IsHelp(a):
                    PrintSetupHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown setup option: {args[i]}");
                    PrintSetupHelp();
                    return 1;
            }
        }
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("cw setup is Windows-only.");
            return 1;
        }

        var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
        if (ensure is int code)
            return code;

        Ui.Title($"{ProductInfo.Name} setup");
        Ui.Line(Ui.Dim(yes ? "  --yes: every step runs." : "  Each step asks first. Enter means yes."));
        bool Ask(string question) => yes || Ui.Confirm(question);
        var results = new List<StepResult>();

        if (!skip.Contains("terminal"))
            results.Add(await SetupTerminalAsync(Ask).ConfigureAwait(false));
        if (!skip.Contains("tools"))
            results.AddRange(await SetupToolsAsync(Ask).ConfigureAwait(false));
        if (!skip.Contains("harnesses"))
            results.AddRange(await SetupHarnessesAsync(Ask, yes).ConfigureAwait(false));
        if (!skip.Contains("shortcuts"))
            results.Add(SetupShortcuts(Ask));
        if (!skip.Contains("canary"))
            results.Add(SetupCanary(Ask));
        if (!skip.Contains("protect"))
            results.AddRange(SetupProtections(Ask));

        Console.WriteLine();
        Ui.Title("setup summary");
        var table = Ui.Table("step", "state", "detail");
        foreach (var r in results)
        {
            var state = r.State switch
            {
                "ok" => Ui.Ok("ok"),
                "failed" => Ui.Fail("failed"),
                _ => Ui.Dim(r.State),
            };
            table.AddRow(Ui.E(r.Step), state, Ui.E(r.Detail));
        }
        AnsiConsole.Write(table);
        Ui.Line(Ui.Dim("  Undo one part: cw unharden <tool>, cw hook|leak-guard|mcp|protect uninstall <harness>, cw canary remove."));
        Ui.Line(Ui.Dim("  Undo everything: cw uninstall."));

        var tryExit = 0;
        if (!skip.Contains("try") && Ask("Show a real Approval Gate card now (cw try)?"))
        {
            Console.WriteLine();
            tryExit = await TryAsync([]).ConfigureAwait(false);
        }

        Console.WriteLine();
        Ui.Line($"[bold]Next:[/] {Ui.E("restart your AI harness and open a new terminal, so they read the new PATH and hooks.")}");
        return results.Any(r => r.State == "failed") ? 1 : tryExit is 0 or 3 ? 0 : tryExit;
    }

    private static void PrintSetupHelp()
    {
        Console.WriteLine("Usage: cw setup [--yes] [--skip <step,...>]");
        Console.WriteLine("  Set up CmdWarden in one command. Each step asks first; Enter means yes.");
        Console.WriteLine("  terminal   Enroll this terminal as Trusted (your own work gets no card).");
        Console.WriteLine("  tools      Harden each installed tool: gh, git, az, docker, npm, aws, kubectl, ssh. Fix PATH when needed.");
        Console.WriteLine("  harnesses  Enroll Claude Code, Cursor, and Codex at Read. Add the hook, the leak guard, and the MCP server.");
        Console.WriteLine("  shortcuts  Start Menu entry, tray icon at logon, and the Settings > Apps entry.");
        Console.WriteLine("  canary     Fake tokens that show an attack when used.");
        Console.WriteLine("  protect    Turn on the secret protections of each harness (cw protect).");
        Console.WriteLine("  try        Show a real Approval Gate card (cw try).");
        Console.WriteLine("  --yes      Run every step with no questions.");
        Console.WriteLine("  --skip     Leave out steps, for example --skip canary,try.");
        Console.WriteLine("  Run it again at any time: a step that is done reports so.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task<StepResult> SetupTerminalAsync(Func<string, bool> ask)
    {
        const string step = "terminal";
        try
        {
            var id = await AgentHealthClient.ResolveIdentityAsync().ConfigureAwait(false);
            var harnessImages = HarnessLauncher.Catalog.Select(h => h.Image).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (id.Chain.FirstOrDefault(n => harnessImages.Contains(Path.GetFileName(n.Path ?? ""))) is { } harness)
                return new(step, "skipped", $"this terminal runs inside {Path.GetFileName(harness.Path)}; run cw setup in your own terminal");
            var store = LoadPolicyStore();
            if (store.Launchers.TryGetValue(id.SelectedPolicyKey, out var entry))
                return new(step, "done", $"already enrolled as {entry.Kind}");
            if (!ask($"Enroll this terminal ({Path.GetFileName(id.SelectedPath)}) as Trusted?"))
                return new(step, "skipped", "you said no");
            return await PolicyEnrollAsync(["--kind", LauncherEnrollmentKindNames.Terminal]).ConfigureAwait(false) == 0
                ? new(step, "ok", $"enrolled {Path.GetFileName(id.SelectedPath)}")
                : new(step, "failed", "see the lines above");
        }
        catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
        {
            return new(step, "failed", "Session Agent is not reachable");
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<IReadOnlyList<StepResult>> SetupToolsAsync(Func<string, bool> ask)
    {
        var results = new List<StepResult>();
        var hardened = false;
        foreach (var tool in ToolCatalog.All().Select(t => t.Id))
        {
            var step = "tool " + tool;
            // The ssh gate sits in front of the OpenSSH agent, not in front of ssh.exe.
            var isSsh = tool == SshGate.Tool;
            if (isSsh ? SshGate.Load() is not null : HardenedToolStatus.Probe(tool).State == HardenState.Hardened)
            {
                results.Add(new(step, "done", "already hardened"));
                continue;
            }
            var real = isSsh ? SshGate.DefaultUpstream : RealToolOnPath(tool);
            if (isSsh ? !SshGate.PipeExists(SshGate.DefaultUpstream) : real is null)
            {
                results.Add(new(step, "skipped", isSsh ? "the OpenSSH agent does not run" : "not installed"));
                continue;
            }
            if (!ask(isSsh ? "Ask before an agent signs with your ssh key (harden ssh)?" : $"Harden {tool} ({real})?"))
            {
                results.Add(new(step, "skipped", "you said no"));
                continue;
            }
            var exit = await HardenAsync([tool]).ConfigureAwait(false);
            results.Add(exit == 0 ? new(step, "ok", "hardened") : new(step, "failed", "see the lines above"));
            hardened |= exit == 0;
        }

        if (hardened && ToolCatalog.All().Any(t => HardenedToolStatus.Probe(t.Id).Reason?.Contains("(machine)", StringComparison.Ordinal) == true))
        {
            results.Add(!ask("A real tool comes before the shims on the machine PATH. Fix PATH (one admin prompt)?")
                ? new("PATH", "skipped", "run cw doctor --fix-path later")
                : FixPath([]) == 0 ? new("PATH", "ok", "shims first on the machine PATH") : new("PATH", "failed", "see the lines above"));
        }
        return results;
    }

    /// <summary>The real tool on PATH, not a CmdWarden shim.</summary>
    private static string? RealToolOnPath(string tool)
    {
        var shims = ProductPaths.ShimsDir().TrimEnd('\\');
        var path = string.Join(Path.PathSeparator, (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(d => !string.Equals(d.Trim().TrimEnd('\\'), shims, StringComparison.OrdinalIgnoreCase)));
        var found = InjectRunner.ResolveProgram(tool, path);
        return Path.IsPathRooted(found) ? found : null;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<IReadOnlyList<StepResult>> SetupHarnessesAsync(Func<string, bool> ask, bool yes)
    {
        var results = new List<StepResult>();
        foreach (var harness in HarnessLauncher.Catalog)
        {
            var step = "harness " + harness.Id;
            if (HarnessLauncher.Locate(harness) is not { } install)
            {
                results.Add(new(step, "skipped", "not installed"));
                continue;
            }
            if (!ask($"Enroll {harness.DisplayName} at Read and add the hook, the leak guard, and the MCP server?"))
            {
                results.Add(new(step, "skipped", "you said no"));
                continue;
            }
            var (key, _) = HarnessLauncher.EnsureEnrolled(install, LoadPolicyStore());
            var failed = new List<string>();
            if (await PolicyHookCommands.HookAsync(["install", harness.Id]).ConfigureAwait(false) != 0)
                failed.Add("hook");
            if (await LeakGuardCommands.LeakGuardAsync(["install", harness.Id]).ConfigureAwait(false) != 0)
                failed.Add("leak guard");
            if (await McpCommands.McpAsync(["install", harness.Id]).ConfigureAwait(false) != 0)
                failed.Add("MCP server");
            if (harness.Id == "codex")
                await OfferAgentAccountsAsync(ask: !yes).ConfigureAwait(false);
            results.Add(failed.Count > 0
                ? new(step, "failed", $"{string.Join(", ", failed)} not added; see the lines above")
                : new(step, "ok", key is null ? "hooks added; binary not enrolled" : "enrolled at Read; hook, leak guard, MCP server"));
        }
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static StepResult SetupShortcuts(Func<string, bool> ask)
    {
        const string step = "shortcuts";
        if (!ask("Add the Start Menu entry, the tray icon at logon, and the Settings > Apps entry?"))
            return new(step, "skipped", "you said no");
        var shortcuts = ShortcutInstall(desktop: false);
        var apps = RegisterAppsEntry();
        return shortcuts == 0 ? new(step, "ok", $"Start Menu, tray; Apps entry: {apps}") : new(step, "failed", $"see the lines above; Apps entry: {apps}");
    }

    /// <summary>
    /// The Settings > Apps entry, as the install script writes it. A portable or winget install has
    /// none, so cw setup adds it. The uninstall script ships next to cw.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string RegisterAppsEntry()
    {
        using (var existing = Registry.CurrentUser.OpenSubKey(UpdateCommands.UninstallKey))
        {
            if (existing?.GetValue("UninstallString") is string)
                return "already there";
        }
        var source = Path.Combine(AppContext.BaseDirectory, "uninstall", "Uninstall-CmdWarden.ps1");
        if (!File.Exists(source))
            return "skipped: the uninstall script is not next to cw";
        var dir = Path.Combine(ProductPaths.Root(), "uninstall");
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "Uninstall-CmdWarden.ps1");
        File.Copy(source, script, overwrite: true);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var run = $"\"{powershell}\" -NoProfile -ExecutionPolicy Bypass -File \"{script}\"";
        using var key = Registry.CurrentUser.CreateSubKey(UpdateCommands.UninstallKey);
        key.SetValue("DisplayName", ProductInfo.Name);
        key.SetValue("DisplayVersion", ProductInfo.Version);
        key.SetValue("Publisher", ProductInfo.Name);
        key.SetValue("URLInfoAbout", "https://github.com/BasantPandey/CmdWarden");
        key.SetValue("InstallLocation", AppContext.BaseDirectory.TrimEnd('\\'));
        key.SetValue("UninstallString", run);
        key.SetValue("QuietUninstallString", run + " -Quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        if (SecretsManagerLocator.FindExePath() is { } icon)
            key.SetValue("DisplayIcon", icon);
        return "added";
    }

    private static StepResult SetupCanary(Func<string, bool> ask)
    {
        const string step = "canary";
        if (new CanaryStore().Load().Count > 0)
            return new(step, "done", "canary tokens are in place");
        if (!ask("Plant canary tokens (fake tokens that show an attack when used)?"))
            return new(step, "skipped", "you said no");
        return LeakGuardCommands.Canary(["install"]) == 0 ? new(step, "ok", "planted") : new(step, "failed", "see the lines above");
    }

    private static IReadOnlyList<StepResult> SetupProtections(Func<string, bool> ask)
    {
        var results = new List<StepResult>();
        if (HarnessLauncher.Locate(HarnessLauncher.Find("claude")!) is not null || Directory.Exists(Path.GetDirectoryName(HookInstaller.ClaudeSettingsPath())))
        {
            if (ask("Turn on the Claude Code protections (Read deny rules for secret files, env scrub)?"))
            {
                var added = HarnessProtections.InstallClaude();
                results.Add(new("protect claude", added.Count == 0 ? "done" : "ok", added.Count == 0 ? "already on" : $"{added.Count} settings added"));
            }
            else
            {
                results.Add(new("protect claude", "skipped", "you said no"));
            }
        }
        if (HarnessLauncher.Locate(HarnessLauncher.Find("codex")!) is not null || Directory.Exists(Path.GetDirectoryName(CodexConfig.Path())))
        {
            if (ask("Turn on the Codex protection (drop secret variables from its commands)?"))
            {
                var change = HarnessProtections.InstallCodex(HarnessProtections.SafeVaultNames());
                results.Add(change == CodexConfig.Change.UserOwned
                    ? new("protect codex", "skipped", "your own [shell_environment_policy] table is there")
                    : new("protect codex", change == CodexConfig.Change.Unchanged ? "done" : "ok", change == CodexConfig.Change.Unchanged ? "already on" : "shell_environment_policy set"));
            }
            else
            {
                results.Add(new("protect codex", "skipped", "you said no"));
            }
        }
        return results;
    }

    /// <summary>
    /// cw try: a stand-in agent that nobody enrolled asks for a fake secret. The real Approval Gate
    /// shows. After the answer, cw try prints the audit row. The output shows the placeholder, never
    /// the value, because cw inject masks it.
    /// </summary>
    private static async Task<int> TryAsync(string[] args)
    {
        if (args.Any(IsHelp))
        {
            Console.WriteLine("Usage: cw try");
            Console.WriteLine("  A stand-in AI agent asks for a fake secret. The real Approval Gate card shows.");
            Console.WriteLine("  Press Deny or Approve Once. Then cw try prints the audit row. No real token is used.");
            return 0;
        }
        var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
        if (ensure is int code)
            return code;
        var agent = FindTryAgent();
        if (agent is null)
        {
            Console.Error.WriteLine($"cw-try-agent.exe is not next to cw (try-agent\\). Rebuild or reinstall, or set {TryAgentEnvVar}.");
            return 2;
        }

        var fake = "cw-try-" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        await AgentVaultClient.SaveAsync(TrySecret, Encoding.UTF8.GetBytes(fake)).ConfigureAwait(false);

        Ui.Title($"{ProductInfo.Name} try");
        Ui.Line(Ui.E("  A stand-in AI agent that nobody enrolled asks for the fake secret " + TrySecret + "."));
        Ui.Line(Ui.E("  CmdWarden shows the Approval Gate card now. Press Deny, or Approve Once."));
        Console.WriteLine();

        var psi = new System.Diagnostics.ProcessStartInfo(agent) { UseShellExecute = false };
        foreach (var part in HookInstaller.SelfCommandParts().Concat([
                     "inject", "+" + TrySecret, "--tool", TryTool, "--class", CommandClassNames.SecretReveal, "--",
                     "cmd.exe", "/c", $"echo The agent read the token: %{TrySecret}%"]))
            psi.ArgumentList.Add(part);
        int exit;
        using (var process = System.Diagnostics.Process.Start(psi)!)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            exit = process.ExitCode;
        }

        Console.WriteLine();
        Ui.Line(exit == 0
            ? $"  {Ui.Ok("Approved.")} {Ui.E("The agent got the fake value, and the output shows only [" + ProductInfo.Name + ": " + TrySecret + "].")}"
            : $"  {Ui.Fail("Blocked.")} {Ui.E("The agent got nothing. A real agent reads the same answer.")}");
        Console.WriteLine();
        AuditAsync(["-n", "1"]);
        Console.WriteLine();
        Ui.Line(Ui.Dim("  An app that you did not enroll always gets this card. Your enrolled terminal does not."));
        Ui.Line(Ui.Dim("  Undo everything: cw uninstall."));
        return exit;
    }

    private static string? FindTryAgent()
    {
        if (Environment.GetEnvironmentVariable(TryAgentEnvVar) is { Length: > 0 } overridePath)
            return File.Exists(overridePath) ? overridePath : null;
        var bundled = Path.Combine(AppContext.BaseDirectory, "try-agent", "cw-try-agent.exe");
        return File.Exists(bundled) ? bundled : null;
    }
}
