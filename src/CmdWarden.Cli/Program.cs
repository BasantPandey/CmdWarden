using System.Text;
using CmdWarden.Cli;
using CmdWarden.Cli.Harden;
using CmdWarden.Cli.Scan;
using CmdWarden.Contracts.Scan;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;
using CmdWarden.Contracts.Ssh;
using Spectre.Console;

return await CliApp.RunAsync(args);

/// <summary>
/// CmdWarden CLI: doctor, vault save, inject (spike).
/// </summary>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        try
        {
            return await DispatchAsync(cmd, args).ConfigureAwait(false);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.PermissionDenied)
        {
            // For example #36: an agent account called an owner-only command.
            Console.Error.WriteLine($"{ProductInfo.Name}: {ex.Status.Detail}");
            return 3;
        }
    }

    private static async Task<int> DispatchAsync(string cmd, string[] args)
    {
        return cmd switch
        {
            "version" or "--version" or "-v" => PrintVersion(),
            "doctor" => await DoctorAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "agent" => await AgentAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "whoami" or "identity" => await WhoAmIAsync().ConfigureAwait(false),
            "save" => await SaveAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "inject" => await InjectAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "delete" => await DeleteAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "policy" => await PolicyAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "harden" => await HardenAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "unharden" => Unharden(args.AsSpan(1).ToArray()),
            "audit" => AuditAsync(args.AsSpan(1).ToArray()),
            "scan" => ScanAsync(args.AsSpan(1).ToArray()),
            "shortcut" => ShortcutAsync(args.AsSpan(1).ToArray()),
            "leak-guard" => await CmdWarden.Cli.Hooks.LeakGuardCommands.LeakGuardAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "hook" => await CmdWarden.Cli.Hooks.PolicyHookCommands.HookAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "mcp" => await CmdWarden.Cli.Mcp.McpCommands.McpAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "canary" => CmdWarden.Cli.Hooks.LeakGuardCommands.Canary(args.AsSpan(1).ToArray()),
            "launch" => LaunchHarness(args.AsSpan(1).ToArray()),
            "github" => await GitHubAppCommands.RunAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "update" => await UpdateCommands.UpdateAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "uninstall" => UpdateCommands.Uninstall(args.AsSpan(1).ToArray()),
            _ => Unknown(args[0]),
        };
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "help" or "/?";

    private static int Unharden(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: cw unharden docker|git|gh|az|ssh|<pack tool>");
            Console.WriteLine("  docker  Strong mode: restore credsStore, write registry credentials back, delete CmdWarden/docker/*.");
            Console.WriteLine("  git     Strong mode: restore credential.helper lines and gh blocks, write GCM entries back, delete CmdWarden/git/*.");
            Console.WriteLine("  gh      Strong mode: write gh:<host>:<user> entries back, delete CmdWarden/gh/*; the compat GH_TOKEN stays.");
            Console.WriteLine("  az      Strong mode: write the az login back to the az config dir, delete the CmdWarden az store.");
            Console.WriteLine("  ssh     Put SSH_AUTH_SOCK and git core.sshCommand back, and stop the ssh gate.");
            Console.WriteLine("  <pack tool>  Remove the pin and the shim, for example cw unharden npm.");
            Console.WriteLine("  Then remove the pin, shim, and credential helper.");
            return args.Length > 0 && IsHelp(args[0]) ? 0 : 1;
        }
        var tool = args[0].ToLowerInvariant();
        if (tool == SshGate.Tool)
            return UnhardenSsh();
        if (!ToolCatalog.IsBuiltIn(tool) && ToolPacks.IsValidToolName(tool))
            return UnhardenPack(tool);
        if (!ToolCatalog.IsBuiltIn(tool) || !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine($"Unharden for '{args[0]}' is not implemented yet (docker, git, gh, az, or a pack tool).");
            return 1;
        }
        if (tool == "git")
            return UnhardenGit();
        if (tool == "az")
            return UnhardenAz();
        if (tool == "gh")
            return UnhardenGh();

        try
        {
            var result = DockerStrongHarden.Unharden(new DockerStrongOptions());
            Ui.Title($"{ProductInfo.Name} unharden docker");
            if (result.RestoredCredsStore is not null)
            {
                Ui.Kv("credentials", $"{result.RestoredUrls.Count} registries written back to the Docker store");
                Ui.Kv("credsStore", (result.RestoredCredsStore.Length == 0 ? "(removed)" : result.RestoredCredsStore));
            }
            Ui.Kv("pin", (result.PinRemoved ? "removed" : "none"));
            Ui.Kv("shim", (result.ShimRemoved ? "removed" : "none"));
            Ui.Kv("helper", (result.HelperRemoved ? "removed" : "none"));
            Ui.Line(Ui.Dim("next: run cw doctor"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unharden docker failed: {ex.Message}");
            return 1;
        }
    }

    private static int UnhardenSsh()
    {
        try
        {
            var removed = SshHarden.Unharden();
            Ui.Title($"{ProductInfo.Name} unharden ssh");
            Ui.Kv("ssh gate", removed ? "removed; SSH_AUTH_SOCK and core.sshCommand are back" : "none");
            if (removed)
                RestartAgentAsync("Session Agent", "no ssh gate").GetAwaiter().GetResult();
            Ui.Line(Ui.Dim("next: open a new terminal"));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"unharden ssh failed: {ex.Message}");
            return 1;
        }
    }

    private static int UnhardenPack(string tool)
    {
        try
        {
            var (pinRemoved, shimRemoved) = PackHarden.Unharden(tool);
            Ui.Title($"{ProductInfo.Name} unharden {tool}");
            Ui.Kv("pin", pinRemoved ? "removed" : "none");
            Ui.Kv("shim", shimRemoved ? "removed" : "none");
            Ui.Line(Ui.Dim("next: run cw doctor"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"unharden {tool} failed: {ex.Message}");
            return 1;
        }
    }

    private static int UnhardenGit()
    {
        try
        {
            var result = GitStrongHarden.Unharden(new GitStrongOptions());
            Ui.Title($"{ProductInfo.Name} unharden git");
            if (result.RestoredHelpers is not null)
            {
                Ui.Kv("credentials", $"{result.RestoredKeys.Count} entries written back to the GCM store");
                Ui.Kv("helper", $"credential.helper = {(result.RestoredHelpers.Count == 0 ? "(unset)" : string.Join(", ", result.RestoredHelpers))}");
            }
            Ui.Kv("pin", (result.PinRemoved ? "removed" : "none"));
            Ui.Kv("shim", (result.ShimRemoved ? "removed" : "none"));
            Ui.Kv("helper exe", (result.HelperRemoved ? "removed" : "none"));
            Ui.Line(Ui.Dim("next: run cw doctor"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unharden git failed: {ex.Message}");
            return 1;
        }
    }

    private static int UnhardenAz()
    {
        try
        {
            var result = AzStrongHarden.Unharden();
            Ui.Title($"{ProductInfo.Name} unharden az");
            if (result.WasStrong || result.RestoredLoginFiles.Count > 0)
                Ui.Kv("login", $"{result.RestoredLoginFiles.Count} files written back to {result.StockDir}");
            Ui.Kv("pin", (result.PinRemoved ? "removed" : "none"));
            Ui.Kv("shim", (result.ShimRemoved ? "removed" : "none"));
            Ui.Line(Ui.Dim("next: run cw doctor"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unharden az failed: {ex.Message}");
            return 1;
        }
    }

    private static int UnhardenGh()
    {
        try
        {
            var result = GhStrongHarden.Unharden(new GhStrongOptions());
            Ui.Title($"{ProductInfo.Name} unharden gh");
            if (result.WasStrong)
                Ui.Kv("tokens", $"{result.Restored.Count} entries written back to the gh store (compat GH_TOKEN left in the vault)");
            Ui.Kv("pin", (result.PinRemoved ? "removed" : "none"));
            Ui.Kv("shim", (result.ShimRemoved ? "removed" : "none"));
            Ui.Line(Ui.Dim("next: run cw doctor"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unharden gh failed: {ex.Message}");
            return 1;
        }
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"{ProductInfo.Name} {ProductInfo.Version}");
        Console.WriteLine($"CLI: {ProductInfo.CliPrimary} (alias: {ProductInfo.CliAlias})");
        return 0;
    }

    private static async Task<int> DoctorAsync(string[] args)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            Console.WriteLine("Usage: cw doctor [--fix-path]");
            Console.WriteLine("  --fix-path  One UAC prompt: prepend the shims dir to the machine PATH (REG_EXPAND_SZ).");
            return 0;
        }
        if (args.Length > 0 && args[0] == "--fix-path")
            return FixPath(args.AsSpan(1).ToArray());
        if (args.Length > 0)
            return Unknown("doctor " + args[0]);

        var pipe = AgentEndpoints.PipeName;
        Ui.Title($"{ProductInfo.Name} doctor");
        var checks = Ui.Table("check", "state", "detail");
        checks.AddRow(Ui.E("product"), Ui.Ok(), Ui.E($"{ProductInfo.Name} {ProductInfo.Version}"));
        checks.AddRow(Ui.E("pipe"), Ui.Ok(), Ui.E(pipe));
        checks.AddRow(Ui.E("product root"), Ui.Ok(), Ui.E(ProductPaths.Root()));

        var agentBinary = AgentLocator.FindAgentBinary();
        checks.AddRow(Ui.E("agent binary"), agentBinary is null ? Ui.Fail() : Ui.Ok(), Ui.E(agentBinary ?? "(not found)"));

        var secretsManager = SecretsManagerLocator.FindExePath();
        checks.AddRow(Ui.E("vault UI binary"), secretsManager is null ? Ui.Warn() : Ui.Ok(),
            Ui.E(secretsManager ?? "(not found) rebuild/pack so secrets-manager/ is next to cw, or set CW_SECRETS_MANAGER_PATH"));
        if (OperatingSystem.IsWindows())
        {
            var lnkPresent = SecretsManagerStartMenu.ShortcutExists();
            checks.AddRow(Ui.E("vault Start Menu shortcut"), lnkPresent ? Ui.Ok() : Ui.Warn(),
                Ui.E(lnkPresent ? SecretsManagerStartMenu.ShortcutPath : "missing: cw shortcut install"));
        }

        var status = await Ui.StatusAsync("Checking Session Agent...",
            () => AgentLifecycle.StatusAsync(pipe)).ConfigureAwait(false);
        if (!status.Up)
        {
            status = await Ui.StatusAsync("Session Agent is DOWN, starting...",
                () => AgentLifecycle.EnsureRunningAsync(pipe)).ConfigureAwait(false);
        }

        if (!status.Up)
        {
            checks.AddRow(Ui.E("session agent"), Ui.Fail("DOWN"), Ui.E(status.Detail ?? ""));
            AnsiConsole.Write(checks);
            Console.Error.WriteLine(
                "Session Agent is not reachable. Start it with: cw agent start");
            Console.Error.WriteLine(
                "  (dev fallback: dotnet run --project src/CmdWarden.Agent)");
            return 2;
        }

        try
        {
            var health = await AgentHealthClient.GetHealthAsync(pipe).ConfigureAwait(false);
            checks.AddRow(Ui.E("session agent"), Ui.Ok("UP"),
                Ui.E($"v{health.Version} pid {health.ProcessId} as {health.UserName} on {health.MachineName}"));
            AnsiConsole.Write(checks);

            Ui.Title("caller");
            Ui.Kv("caller pid", health.ClientPid.ToString());
            Ui.Kv("launcher kind", health.LauncherKind);
            Ui.Kv("launcher policy key", health.LauncherPolicyKey);
            Ui.Kv("launcher path", health.LauncherPath);
            Ui.Kv("auto-approve eligible", health.AutoApproveEligible.ToString());

            Ui.Title("hardened tools");
            PrintHardenedTools();
            if (HardenedToolStatus.IsProcessPathStale(
                    HardenedToolStatus.ReadRegistryPath(), Environment.GetEnvironmentVariable("PATH")))
                Ui.Line($"{Ui.Warn("info")} {Ui.E(HardenedToolStatus.StaleProcessPathInfo)}");
            return 0;
        }
        catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
        {
            checks.AddRow(Ui.E("session agent"), Ui.Fail("DOWN"), Ui.E(Describe(ex)));
            AnsiConsole.Write(checks);
            Console.Error.WriteLine("Session Agent is not reachable. Start it with: cw agent start");
            return 2;
        }
    }

    /// <summary>
    /// cw doctor --fix-path (#210): one elevated run of cw writes the machine PATH. The entry is
    /// %LOCALAPPDATA%\CmdWarden\shims; when the logon PATH does not expand it, the literal path goes in.
    /// </summary>
    private static int FixPath(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("--fix-path is Windows-only.");
            return 1;
        }
        if (args.Length == 2 && args[0] == MachinePathEditor.ElevatedFlag)
        {
            try
            {
                MachinePathEditor.PrependMachine(args[1]);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"machine PATH write failed: {ex.Message}");
                return 1;
            }
        }

        var shimsDir = ProductPaths.ShimsDir();
        var entry = MachinePathEditor.PreferredEntry(shimsDir);
        Console.WriteLine($"{ProductInfo.Name} doctor --fix-path");
        Console.WriteLine($"  machine PATH: prepend {entry}");
        var exit = MachinePathEditor.RunElevated(entry);
        if (exit is null)
        {
            Console.Error.WriteLine("elevation refused; the machine PATH is unchanged.");
            return 1;
        }
        if (exit != 0)
        {
            Console.Error.WriteLine($"elevated write failed (exit {exit}).");
            return 1;
        }
        if (!MachinePathEditor.IsFirstAtLogon(shimsDir) && entry != shimsDir)
        {
            // The logon PATH did not expand the user variable; the literal path is the fallback.
            Console.WriteLine($"  machine PATH: {entry} did not expand at logon; prepend {shimsDir}");
            exit = MachinePathEditor.RunElevated(shimsDir);
            if (exit != 0)
            {
                Console.Error.WriteLine(exit is null ? "elevation refused; the literal path was not written." : $"elevated write failed (exit {exit}).");
                return 1;
            }
        }
        if (!MachinePathEditor.IsFirstAtLogon(shimsDir))
        {
            Console.Error.WriteLine("shims dir is still not first on the logon PATH.");
            return 1;
        }
        Console.WriteLine("  shims first on PATH (machine)");
        PrintHardenedTools();
        if (HardenedToolStatus.IsProcessPathStale(HardenedToolStatus.ReadRegistryPath(), Environment.GetEnvironmentVariable("PATH")))
            Console.WriteLine($"  info: {HardenedToolStatus.StaleProcessPathInfo}");
        return 0;
    }

    private static int ShortcutAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Start Menu shortcuts are Windows-only.");
            return 1;
        }

        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: cw shortcut install [--desktop]|remove|status");
            Console.WriteLine("  install  Create/update Start Menu 'CmdWarden Vault' -> secrets-manager exe");
            Console.WriteLine("           and a Startup entry for the tray icon; start the tray icon now");
            Console.WriteLine("           --desktop  Also create/update the Desktop shortcut");
            Console.WriteLine("  remove   Delete the Start Menu, Desktop, and Startup shortcuts");
            Console.WriteLine("  status   Show shortcut paths and whether they exist");
            return args.Length > 0 && IsHelp(args[0]) ? 0 : 1;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args.AsSpan(1).ToArray();
        return sub switch
        {
            "status" when rest.Length == 0 => ShortcutStatus(),
            "install" when rest.Length == 0 => ShortcutInstall(desktop: false),
            "install" when rest is ["--desktop"] => ShortcutInstall(desktop: true),
            "remove" when rest.Length == 0 => ShortcutRemove(),
            _ => Unknown("shortcut " + string.Join(' ', args)),
        };
    }

    // cw launch <claude|codex|cursor> [-- args...]
    private static int LaunchHarness(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("cw launch is Windows-only.");
            return 1;
        }
        var ids = string.Join("|", CmdWarden.Cli.Launch.HarnessLauncher.Catalog.Select(h => h.Id));
        if (args.Length == 0 || IsHelp(args[0]) || CmdWarden.Cli.Launch.HarnessLauncher.Find(args[0]) is not { } harness)
        {
            Console.WriteLine($"Usage: cw launch <{ids}> [-- <harness args>]");
            Console.WriteLine("  Start the AI harness without the token variables CmdWarden knows (GH_TOKEN and others).");
            Console.WriteLine("  Enroll the harness binary as ai-harness when it is not enrolled yet.");
            return args.Length > 0 && IsHelp(args[0]) ? 0 : 1;
        }
        var rest = args.Skip(1).ToList();
        if (rest.Count > 0 && rest[0] == "--")
            rest.RemoveAt(0);

        var install = CmdWarden.Cli.Launch.HarnessLauncher.Locate(harness);
        if (install is null)
        {
            Console.Error.WriteLine($"{harness.DisplayName} not found. Install it, or put '{harness.Command}' on PATH.");
            return 2;
        }

        var (clean, removed) = CmdWarden.Cli.Launch.HarnessLauncher.CleanEnvironment(CmdWarden.Cli.Launch.HarnessLauncher.CurrentEnvironment());
        Console.WriteLine(removed.Count == 0
            ? $"{ProductInfo.Name}: no token variables to remove."
            : $"{ProductInfo.Name}: removed from the environment of {harness.DisplayName}: {string.Join(", ", removed)}");

        var (key, enrolled) = CmdWarden.Cli.Launch.HarnessLauncher.EnsureEnrolled(install, LoadPolicyStore());
        if (key is null)
            Console.WriteLine($"{ProductInfo.Name}: {harness.Image} not found, so it is not enrolled. Enroll it later with cw policy enroll --kind ai-harness --key <key>.");
        else if (enrolled)
            Console.WriteLine($"{ProductInfo.Name}: enrolled {harness.Image} as ai-harness ({key}).");

        if (harness.Gui && CmdWarden.Cli.Launch.HarnessLauncher.IsRunning(harness))
            Console.WriteLine($"{ProductInfo.Name}: {harness.DisplayName} is already running. A new window joins that process and keeps its old environment. Close {harness.DisplayName} first.");

        try
        {
            return CmdWarden.Cli.Launch.HarnessLauncher.Start(install, clean, rest);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"launch {harness.Id} failed: {ex.Message}");
            return 1;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<(CmdWarden.Cli.Launch.HarnessInstall Install, string Lnk)> HarnessShortcuts() =>
        CmdWarden.Cli.Launch.HarnessLauncher.Catalog
            .Select(h => CmdWarden.Cli.Launch.HarnessLauncher.Locate(h))
            .OfType<CmdWarden.Cli.Launch.HarnessInstall>()
            .Select(i => (i, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                $"{i.Harness.DisplayName} (CmdWarden).lnk")));

    private static int ShortcutStatus()
    {
        var exe = SecretsManagerLocator.FindExePath();
        Console.WriteLine($"vault UI binary: {exe ?? "(not found)"}");
        Console.WriteLine($"start menu shortcut: {SecretsManagerStartMenu.ShortcutPath}");
        Console.WriteLine($"  present: {(SecretsManagerStartMenu.ShortcutExists() ? "yes" : "no")}");
        Console.WriteLine($"desktop shortcut: {SecretsManagerStartMenu.DesktopShortcutPath}");
        Console.WriteLine($"  present: {(SecretsManagerStartMenu.DesktopShortcutExists() ? "yes" : "no (cw shortcut install --desktop)")}");
        Console.WriteLine($"tray icon at logon: {SecretsManagerStartMenu.TrayShortcutPath}");
        Console.WriteLine($"  present: {(File.Exists(SecretsManagerStartMenu.TrayShortcutPath) ? "yes" : "no (cw shortcut install)")}");
        foreach (var (install, lnk) in HarnessShortcuts())
            Console.WriteLine($"{install.Harness.Id} launch shortcut: {lnk}\n  present: {(File.Exists(lnk) ? "yes" : "no (cw shortcut install)")}");
        return 0;
    }

    private static int ShortcutInstall(bool desktop)
    {
        var exe = SecretsManagerLocator.FindExePath();
        if (exe is null)
        {
            Console.Error.WriteLine(
                "Vault UI binary not found. Build/pack secrets-manager next to cw, or set CW_SECRETS_MANAGER_PATH.");
            return 2;
        }

        try
        {
            var lnk = SecretsManagerStartMenu.Install(exe);
            Console.WriteLine($"Installed Start Menu shortcut:");
            Console.WriteLine($"  {lnk}");
            Console.WriteLine($"  -> {exe}");
            if (desktop)
            {
                var desktopLnk = SecretsManagerStartMenu.InstallDesktop(exe);
                Console.WriteLine($"Installed Desktop shortcut:");
                Console.WriteLine($"  {desktopLnk}");
            }
            // #43: the tray icon starts at logon, and now. A second start exits: one tray per user.
            var trayLnk = SecretsManagerStartMenu.InstallTray(exe);
            Console.WriteLine("Installed the tray icon at logon:");
            Console.WriteLine($"  {trayLnk}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, SecretsManagerStartMenu.TrayArgument)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            })?.Dispose();
            // #25: one "cw launch" entry per harness on this PC.
            var cw = CmdWarden.Cli.Hooks.HookInstaller.SelfCommand("").Trim();
            var (target, prefix) = SplitCommand(cw);
            foreach (var (install, harnessLnk) in HarnessShortcuts())
            {
                SecretsManagerStartMenu.WriteLink(harnessLnk, target, $"{prefix}launch {install.Harness.Id}".Trim(),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    $"Start {install.Harness.DisplayName} without token variables ({ProductInfo.Name})",
                    (install.ImagePath ?? install.StartPath) + ",0");
                Console.WriteLine($"Installed {install.Harness.DisplayName} launch shortcut:");
                Console.WriteLine($"  {harnessLnk}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"shortcut install failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>The self command as a target and the arguments before the cw ones (the cw.dll for a dotnet host).</summary>
    private static (string Target, string Prefix) SplitCommand(string command)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in command)
        {
            if (c == '"')
                quoted = !quoted;
            else if (c == ' ' && !quoted)
            {
                if (current.Length > 0)
                    parts.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }
        if (current.Length > 0)
            parts.Add(current.ToString());
        var prefix = string.Concat(parts.Skip(1).Select(p => (p.Contains(' ') ? $"\"{p}\"" : p) + " "));
        return (parts[0].Replace('/', '\\'), prefix);
    }

    private static int ShortcutRemove()
    {
        try
        {
            if (SecretsManagerStartMenu.Remove())
                Console.WriteLine($"Removed {SecretsManagerStartMenu.ShortcutPath}");
            else
                Console.WriteLine($"Shortcut not present: {SecretsManagerStartMenu.ShortcutPath}");
            if (SecretsManagerStartMenu.RemoveDesktop())
                Console.WriteLine($"Removed {SecretsManagerStartMenu.DesktopShortcutPath}");
            if (SecretsManagerStartMenu.RemoveTray())
                Console.WriteLine($"Removed {SecretsManagerStartMenu.TrayShortcutPath}");
            foreach (var h in CmdWarden.Cli.Launch.HarnessLauncher.Catalog)
            {
                var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{h.DisplayName} (CmdWarden).lnk");
                if (File.Exists(lnk))
                {
                    File.Delete(lnk);
                    Console.WriteLine($"Removed {lnk}");
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"shortcut remove failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> AgentAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: cw agent start|stop|status");
            Console.WriteLine("  start   Start Session Agent if not running (lazy start path)");
            Console.WriteLine("  stop    Stop the running Session Agent for this pipe");
            Console.WriteLine("  status  Report whether the agent is up on the pipe");
            return args.Length > 0 && IsHelp(args[0]) ? 0 : 1;
        }

        var sub = args[0].ToLowerInvariant();
        var pipe = AgentEndpoints.PipeName;
        return sub switch
        {
            "status" => await AgentStatusAsync(pipe).ConfigureAwait(false),
            "start" => await AgentStartAsync(pipe).ConfigureAwait(false),
            "stop" => await AgentStopAsync(pipe).ConfigureAwait(false),
            _ => Unknown("agent " + args[0]),
        };
    }

    private static async Task<int> AgentStatusAsync(string pipe)
    {
        var st = await AgentLifecycle.StatusAsync(pipe).ConfigureAwait(false);
        Ui.Title($"{ProductInfo.Name} agent status");
        PrintAgentState(st);
        return st.Up ? 0 : 2;
    }

    private static async Task<int> AgentStartAsync(string pipe)
    {
        Ui.Title($"{ProductInfo.Name} agent start");
        var st = await Ui.StatusAsync("Starting Session Agent...", () => AgentLifecycle.StartAsync(pipe)).ConfigureAwait(false);
        PrintAgentState(st);
        if (!st.Up)
        {
            Console.Error.WriteLine(st.Detail ?? "Session Agent failed to start.");
            return 2;
        }

        return 0;
    }

    private static async Task<int> AgentStopAsync(string pipe)
    {
        Ui.Title($"{ProductInfo.Name} agent stop");
        var st = await AgentLifecycle.StopAsync(pipe).ConfigureAwait(false);
        PrintAgentState(st);
        return st.Up ? 1 : 0;
    }

    private static void PrintAgentState(AgentLifecycle.StatusResult st)
    {
        Ui.Kv("pipe", st.PipeName);
        Ui.Line($"  {Ui.Dim("state:")} {(st.Up ? Ui.Ok("UP") : Ui.Fail("DOWN"))}");
        if (st.ProcessId is int pid)
            Ui.Kv("pid", pid.ToString());
        if (!string.IsNullOrWhiteSpace(st.Detail))
            Ui.Kv("detail", st.Detail);
    }

    /// <summary>
    /// Lazy-start Session Agent for client commands (issue #36). Returns exit code on failure.
    /// </summary>
    private static async Task<int?> EnsureAgentClientAsync(string? pipeName = null)
    {
        var st = await AgentLifecycle.EnsureRunningAsync(pipeName).ConfigureAwait(false);
        if (st.Up)
            return null;

        if (!string.IsNullOrWhiteSpace(st.Detail))
            Console.Error.WriteLine(st.Detail);
        Console.Error.WriteLine("Session Agent is not reachable. Start it with: cw agent start");
        return 2;
    }

    /// <summary>One row per catalog tool and tool pack, same probe as the Hardened Tools tab (#204, #37).</summary>
    private static void PrintHardenedTools()
    {
        var table = Ui.Table("tool", "source", "state", "detail");
        var (packs, errors) = ToolPacks.Load();
        foreach (var tool in ToolCatalog.All())
        {
            var pack = packs.FirstOrDefault(p => p.Tool == tool.Id);
            var source = pack is null ? "built-in" : pack.Source == ToolPacks.BuiltInSource ? "pack" : "user pack";
            var status = HardenedToolStatus.Probe(tool.Id);
            var (state, detail) = status.State switch
            {
                HardenState.Hardened => (Ui.Ok("Hardened"), status.Note ?? ""),
                HardenState.Degraded => (Ui.Warn("Degraded"), status.Reason ?? ""),
                _ => (Ui.Dim("not hardened"), ""),
            };
            table.AddRow(Ui.E(tool.Id), Ui.Dim(source), state, Ui.E(detail));
        }
        AnsiConsole.Write(table);
        foreach (var error in errors)
            Ui.Line($"{Ui.Warn("pack not loaded:")} {Ui.E(error.Message)}");
    }

    /// <summary>After harden: same shim-first reason as doctor when the new pin is not first (#201).</summary>
    private static void PrintShimOrderHint(string tool)
    {
        var status = HardenedToolStatus.Probe(tool);
        if (status.Reason?.StartsWith(HardenedToolStatus.ShimNotFirstPrefix, StringComparison.Ordinal) == true)
            Ui.Line($"{Ui.Warn("Hint:")} {Ui.E(status.Reason)}");
    }

    private static async Task<int> WhoAmIAsync()
    {
        var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
        if (ensure is int code)
            return code;

        try
        {
            var id = await AgentHealthClient.ResolveIdentityAsync().ConfigureAwait(false);
            Ui.Title($"{ProductInfo.Name} caller identity");
            Ui.Kv("client pid", $"{id.ClientPid} (from pipe: {id.ClientPidFromPipe})");
            Ui.Kv("selected kind", id.SelectedKind);
            Ui.Kv("selected policy key", id.SelectedPolicyKey);
            Ui.Kv("selected path", id.SelectedPath);
            Ui.Line($"  {Ui.Dim("auto-approve eligible:")} {(id.AutoApproveEligible ? Ui.Ok("True") : Ui.Warn("False"))}");
            if (!string.IsNullOrEmpty(id.Notes))
                Ui.Kv("notes", id.Notes);

            var chain = new Tree(Ui.Dim("process chain (caller first)"));
            foreach (var node in id.Chain)
            {
                var selected = node.PolicyKey == id.SelectedPolicyKey;
                var pid = selected ? $"[bold green]{node.Pid}[/]" : $"[bold]{node.Pid}[/]";
                var reuse = node.PidReuseSuspected ? $" {Ui.Warn("[pid-reuse?]")}" : "";
                var item = chain.AddNode($"{pid} {Ui.E(node.Kind)}{reuse}  {Ui.E(node.Path)}");
                item.AddNode($"{Ui.Dim("key:")} {Ui.E(node.PolicyKey)}");
                if (!string.IsNullOrEmpty(node.Publisher))
                    item.AddNode($"{Ui.Dim("publisher:")} {Ui.E(node.Publisher)}");
            }
            AnsiConsole.Write(chain);

            return 0;
        }
        catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
        {
            Console.Error.WriteLine("Session Agent is not reachable. Start it with: cw agent start");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"identity failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> SaveAsync(string[] args)
    {
        if (args.Length < 1 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: cw save <NAME> [--value <secret>]");
            Console.WriteLine("  Without --value, reads one line from stdin (not echoed to agent logs).");
            return args.Length > 0 && IsHelp(args[0]) ? 0 : 1;
        }

        var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
        if (ensure is int code)
            return code;

        var name = args[0];
        byte[] valueBytes;
        try
        {
            if (args.Length >= 3 && args[1] is "--value" or "-v")
            {
                valueBytes = Encoding.UTF8.GetBytes(args[2]);
            }
            else if (!Console.IsInputRedirected)
            {
                var line = AnsiConsole.Prompt(
                    new TextPrompt<string>($"Enter value for secret '{Ui.E(name)}':").Secret().AllowEmpty());
                if (string.IsNullOrEmpty(line))
                {
                    Console.Error.WriteLine("Empty secret not allowed.");
                    return 1;
                }

                valueBytes = Encoding.UTF8.GetBytes(line);
            }
            else
            {
                var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(line))
                {
                    Console.Error.WriteLine("Empty secret not allowed.");
                    return 1;
                }

                valueBytes = Encoding.UTF8.GetBytes(line);
            }

            var response = await AgentVaultClient.SaveAsync(name, valueBytes).ConfigureAwait(false);
            Array.Clear(valueBytes);
            Ui.Title($"{ProductInfo.Name} save");
            Ui.Line($"  {Ui.Dim("secret:")} [bold]{Ui.E(name)}[/]");
            Ui.Kv("target", response.TargetName);
            Ui.Line($"  {Ui.Ok("Saved.")} {Ui.Dim("Release is gated by policy: cw policy enroll --kind terminal")}");
            return 0;
        }
        catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
        {
            Console.Error.WriteLine("Session Agent is not reachable. Start it with: cw agent start");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"save failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> InjectAsync(string[] args)
    {
        try
        {
            var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
            if (ensure is int code)
                return code;

            var (options, remainder) = ParseInjectOptions(args);
            var (names, fileName, arguments) = InjectRunner.ParseInjectArgs(remainder);
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Same display join as the shim argv card line (#200).
            var commandLine = string.Join(' ', arguments.Prepend(fileName));
            // #30: the approval binds the program and its script. They stay locked until the child exits.
            var program = InjectRunner.ResolveProgram(fileName);
            var bound = BoundFiles.Find(program, arguments, Environment.CurrentDirectory);
            using var locks = BoundFiles.Lock(bound);
            IReadOnlyList<BoundFile> approved = [];

            foreach (var name in names)
            {
                var released = await AgentVaultClient.ReleaseAsync(
                        name,
                        purpose: "inject",
                        tool: options.Tool,
                        commandClass: options.CommandClass,
                        commandLine: commandLine,
                        timeout: ApprovalGateTimeouts.Client,
                        boundPaths: bound)
                    .ConfigureAwait(false);
                approved = released.BoundFiles.Select(f => new BoundFile(f.Path, f.Sha256)).ToList();
                var raw = released.Value.ToByteArray();
                try
                {
                    env[released.Name] = Encoding.UTF8.GetString(raw);
                }
                finally
                {
                    Array.Clear(raw);
                }
            }

            if (BoundFiles.Mismatches(approved) is { Count: > 0 } changed)
            {
                Console.Error.WriteLine($"{BoundFiles.ChangedMessage}: {string.Join(", ", changed)}. The command did not run. Run it again to approve the new content.");
                return 3;
            }

            // Parent process environment is not modified - only the child ProcessStartInfo.Environment.
            var exit = InjectRunner.Run(program, arguments, env);
            // Clear local copies
            foreach (var key in env.Keys.ToList())
                env[key] = string.Empty;
            return exit;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
        {
            Console.Error.WriteLine("Session Agent is not reachable. Start it with: cw agent start");
            return 2;
        }
        catch (Exception ex) when (AgentVaultClient.IsPermissionDenied(ex))
        {
            Console.Error.WriteLine(ex.Message);
            if (ex.Message.Contains(PolicyReasonCodes.UserDenied, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Release denied at Approval Gate.");
            }
            else if (ex.Message.Contains(PolicyReasonCodes.ApprovalUnavailable, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Approval Gate could not be shown; request blocked (fail closed).");
            }
            else
            {
                Console.Error.WriteLine("Hint: cw policy enroll --kind terminal   (or --kind ai-harness)");
                Console.Error.WriteLine("      cw policy set <policyKey> <tool> <Deny|Read|Trusted|Full>");
            }

            return 3;
        }
        catch (Exception ex) when (AgentVaultClient.IsReleaseBlocked(ex))
        {
            Console.Error.WriteLine(
                $"Release blocked: audit log not writable ({ProductPaths.AuditDir()}). " +
                "Fix the audit directory and retry.");
            return 1;
        }
        catch (Exception ex) when (AgentVaultClient.IsNotFound(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"inject failed: {ex.Message}");
            return 1;
        }
    }

    private static (InjectOptions Options, string[] Remainder) ParseInjectOptions(string[] args)
    {
        // Defaults: tool=inject, class=write (Trusted terminals can inject; not secret-reveal).
        var tool = "inject";
        var commandClass = CommandClassNames.Write;
        var remainder = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            // Everything after -- belongs to the child command.
            if (a == "--")
            {
                remainder.AddRange(args[i..]);
                break;
            }

            if (a is "--tool" or "-t")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--tool requires a value.");
                tool = args[++i];
                continue;
            }

            if (a is "--class" or "-c")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--class requires a value (read|write|secret-reveal|unknown).");
                var raw = args[++i];
                if (!CommandClassNames.TryParse(raw, out var parsed))
                    throw new ArgumentException($"Unknown command class '{raw}'.");
                commandClass = CommandClassNames.Format(parsed);
                continue;
            }

            remainder.Add(a);
        }

        return (new InjectOptions(tool, commandClass), remainder.ToArray());
    }

    private sealed record InjectOptions(string Tool, string CommandClass);

    private static async Task<int> PolicyAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintPolicyHelp();
            return args.Length > 0 && IsHelp(args[0]) ? 0 : 1;
        }

        var sub = args[0].ToLowerInvariant();
        return sub switch
        {
            "list" or "show" => PolicyList(),
            "enroll" => await PolicyEnrollAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "set" => PolicySet(args.AsSpan(1).ToArray()),
            "unenroll" or "remove" => await PolicyUnenrollAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "path" => PolicyPath(),
            "sessions" => await PolicySessionsAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false),
            "hello" => PolicyHello(args.AsSpan(1).ToArray()),
            "low-risk" => PolicyLowRisk(args.AsSpan(1).ToArray()),
            _ => UnknownPolicy(sub),
        };
    }

    private static int PolicyPath()
    {
        Console.WriteLine(PolicyStore.DefaultPath());
        return 0;
    }

    private static int PolicyList()
    {
        var store = LoadPolicyStore();
        Ui.Title($"{ProductInfo.Name} policy");
        Ui.Kv("path", store.Path);
        Ui.Kv("defaults", $"AI Harness → {PolicyLevelNames.Format(store.DefaultAiHarnessLevel)}; " +
                          $"Terminal → {PolicyLevelNames.Format(store.DefaultTerminalLevel)}");
        Ui.Kv("hello", $"{store.HelloMode} (Windows Hello after Approve; change: cw policy hello)");
        Ui.Kv("low-risk", $"{(store.LowRiskWritesAllowed ? LowRiskModes.Allow : LowRiskModes.Ask)} (low-risk writes such as a push to a feature branch; change: cw policy low-risk)");
        if (store.Launchers.Count == 0)
        {
            Ui.Kv("launchers", "(none enrolled)");
            Ui.Kv("enroll current", "cw policy enroll --kind terminal");
            return 0;
        }

        var table = Ui.Table("launcher", "kind", "levels");
        foreach (var (key, entry) in store.Launchers.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var levels = entry.Levels is { Count: > 0 }
                ? string.Join("\n", entry.Levels.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}: {kv.Value}"))
                : "(kind default)";
            var name = key.StartsWith(AgentAccounts.PolicyKeyPrefix, StringComparison.OrdinalIgnoreCase) && entry.Path is { } account
                ? $"{key}\n{account} (agent account)"
                : key;
            table.AddRow(Ui.E(name), Ui.E(entry.Kind), Ui.E(levels));
        }
        AnsiConsole.Write(table);
        if (OperatingSystem.IsWindows())
        {
            // #36: name the agent accounts on this PC that no policy covers yet.
            foreach (var (account, _) in AgentAccounts.OnThisPc().Where(a => !store.Launchers.ContainsKey(AgentAccounts.PolicyKey(a.Sid))))
                Ui.Line(Ui.Dim($"  Agent account {account} is not enrolled; its calls cannot reach the Session Agent. Enroll: cw policy enroll --account {account}"));
        }

        return 0;
    }

    private static async Task<int> PolicyEnrollAsync(string[] args)
    {
        string? kindRaw = null;
        string? policyKey = null;
        string? displayPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--kind" or "-k")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--kind requires terminal or ai-harness.");
                    return 1;
                }

                kindRaw = args[++i];
                continue;
            }

            if (a is "--account")
            {
                // #36: an agent account (a Windows user) is the launcher, for example CodexSandboxOffline.
                if (i + 1 >= args.Length || !OperatingSystem.IsWindows())
                {
                    Console.Error.WriteLine("--account requires a Windows account name, for example CodexSandboxOffline.");
                    return 1;
                }
                var name = args[++i];
                if (AgentAccounts.TryFind(name) is not { } sid)
                {
                    Console.Error.WriteLine($"No Windows account named {name} on this PC.");
                    return 1;
                }
                policyKey = AgentAccounts.PolicyKey(sid);
                displayPath = AgentAccounts.NameOf(sid);
                kindRaw ??= LauncherEnrollmentKindNames.AiHarness;
                continue;
            }

            if (a is "--key")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("--key requires a launcher policy key.");
                    return 1;
                }

                policyKey = args[++i];
                continue;
            }

            if (!a.StartsWith('-') && policyKey is null)
            {
                policyKey = a;
                continue;
            }

            Console.Error.WriteLine($"Unknown enroll argument: {a}");
            PrintPolicyHelp();
            return 1;
        }

        if (kindRaw is null || !LauncherEnrollmentKindNames.TryParse(kindRaw, out var kind)
            || kind == LauncherEnrollmentKind.Unknown)
        {
            Console.Error.WriteLine("Usage: cw policy enroll --kind <terminal|ai-harness> [--key <policyKey>]");
            Console.Error.WriteLine("  Without --key, uses selected launcher from Session Agent (cw whoami).");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(policyKey))
        {
            try
            {
                var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
                if (ensure is int code)
                    return code;

                var id = await AgentHealthClient.ResolveIdentityAsync().ConfigureAwait(false);
                policyKey = id.SelectedPolicyKey;
                displayPath = id.SelectedPath;
                if (string.IsNullOrWhiteSpace(policyKey)
                    || policyKey.Equals(LauncherKinds.PolicyKeyUnknown, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        "Current launcher is unknown/unverifiable; pass an explicit --key from a known process.");
                    return 1;
                }

                if (!id.AutoApproveEligible)
                {
                    Console.Error.WriteLine(
                        "Warning: current launcher is not auto-approve eligible; enrollment still saved.");
                }
            }
            catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
            {
                Console.Error.WriteLine(
                    "Session Agent is not reachable. Start it with: cw agent start  (or pass --key explicitly).");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not resolve identity: {ex.Message}");
                return 1;
            }
        }

        var store = LoadPolicyStore();
        store.Enroll(policyKey, kind, displayPath);
        store.Save();
        Ui.Title($"{ProductInfo.Name} policy enroll");
        Ui.Line($"  {Ui.Dim("launcher:")} [bold]{Ui.E(policyKey)}[/]");
        if (!string.IsNullOrEmpty(displayPath))
            Ui.Kv("path", displayPath);
        Ui.Kv("kind", LauncherEnrollmentKindNames.Format(kind));
        Ui.Kv("default level", kind == LauncherEnrollmentKind.AiHarness
            ? PolicyLevelNames.Format(store.DefaultAiHarnessLevel)
            : PolicyLevelNames.Format(store.DefaultTerminalLevel));
        Ui.Kv("policy file", store.Path);
        Ui.Line($"  {Ui.Ok("Enrolled.")} {Ui.Dim("Next: cw policy set <policyKey> <tool> <Deny|Read|Trusted|Full>")}");
        if (policyKey.StartsWith(AgentAccounts.PolicyKeyPrefix, StringComparison.OrdinalIgnoreCase))
            await RestartAgentForPipeAccessAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>#36: the agent reads the enrolled accounts for its pipe access at start, so a running agent restarts.</summary>
    private static Task RestartAgentForPipeAccessAsync() => RestartAgentAsync("pipe access", "the new account access");

    /// <summary>A setting that the Session Agent reads at start applies after a restart of a running agent.</summary>
    private static async Task RestartAgentAsync(string key, string what)
    {
        if (!(await AgentLifecycle.StatusAsync().ConfigureAwait(false)).Up)
        {
            Ui.Kv(key, $"{what} applies when the Session Agent starts");
            return;
        }
        await AgentLifecycle.StopAsync().ConfigureAwait(false);
        var started = await AgentLifecycle.StartAsync().ConfigureAwait(false);
        Ui.Kv(key, started.Up
            ? $"Session Agent restarted with {what}"
            : $"Session Agent did not restart: {started.Detail}. Run: cw agent start");
    }

    private static int PolicySet(string[] args)
    {
        // cw policy set <policyKey> <tool> <level>
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: cw policy set <policyKey> <tool> <Deny|Read|Trusted|Full>");
            return 1;
        }

        var policyKey = args[0];
        var tool = args[1];
        if (!PolicyLevelNames.TryParse(args[2], out var level))
        {
            Console.Error.WriteLine($"Unknown policy level '{args[2]}'. Use Deny, Read, Trusted, or Full.");
            return 1;
        }

        var store = LoadPolicyStore();
        store.SetLevel(policyKey, tool, level);
        store.Save();
        Console.WriteLine($"Set {policyKey} / {tool} → {PolicyLevelNames.Format(level)}");
        Console.WriteLine($"  policy file: {store.Path}");
        return 0;
    }

    // cw policy hello <off|secret-reveal|write-and-up>
    private static int PolicyHello(string[] args)
    {
        var usage = $"Usage: cw policy hello <{string.Join("|", WindowsHelloPolicy.Modes)}>";
        if (args.Length != 1 || !WindowsHelloPolicy.TryParse(args[0], out var mode))
        {
            Console.Error.WriteLine(usage);
            return 1;
        }

        var store = LoadPolicyStore();
        store.SetHelloMode(mode);
        store.Save();
        Console.WriteLine($"Windows Hello after Approve: {mode}");
        Console.WriteLine($"  policy file: {store.Path}");
        return 0;
    }

    private static int PolicyLowRisk(string[] args)
    {
        if (args is not [LowRiskModes.Ask or LowRiskModes.Allow])
        {
            Console.Error.WriteLine($"Usage: cw policy low-risk <{LowRiskModes.Ask}|{LowRiskModes.Allow}>");
            Console.Error.WriteLine("  allow  A write that the risk check marks low risk runs with no popup, for an enrolled launcher.");
            Console.Error.WriteLine("         Example: git push to a branch that is not the default branch.");
            return 1;
        }

        var store = LoadPolicyStore();
        store.SetLowRisk(args[0]);
        store.Save();
        Console.WriteLine($"Low-risk writes: {args[0]}");
        Console.WriteLine($"  policy file: {store.Path}");
        return 0;
    }

    private static async Task<int> PolicyUnenrollAsync(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: cw policy unenroll <policyKey>");
            return 1;
        }

        var store = LoadPolicyStore();
        var removed = store.Unenroll(args[0]);
        store.Save();
        Console.WriteLine(removed ? $"Unenrolled {args[0]}." : $"No enrollment for {args[0]}.");
        if (removed && args[0].StartsWith(AgentAccounts.PolicyKeyPrefix, StringComparison.OrdinalIgnoreCase))
            await RestartAgentForPipeAccessAsync().ConfigureAwait(false);
        return 0;
    }

    // cw policy sessions [--revoke <id> | --revoke-all]
    private static async Task<int> PolicySessionsAsync(string[] args)
    {
        string? revokeId = null;
        var revokeAll = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--revoke-all":
                    revokeAll = true;
                    break;
                case "--revoke":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--revoke requires a session id.");
                        return 1;
                    }
                    revokeId = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    Console.Error.WriteLine("Usage: cw policy sessions [--revoke <id> | --revoke-all]");
                    return 1;
            }
        }

        var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
        if (ensure is int code)
            return code;

        if (revokeAll || revokeId is not null)
        {
            var removed = await AgentSessionsClient.RevokeAsync(revokeId, revokeAll).ConfigureAwait(false);
            if (revokeAll)
                Console.WriteLine($"Revoked {removed} session allow(s).");
            else if (removed > 0)
                Console.WriteLine($"Revoked session allow {revokeId}.");
            else
                Console.WriteLine($"No active session allow with id {revokeId}.");
            return removed > 0 || revokeAll ? 0 : 1;
        }

        var rows = await AgentSessionsClient.ListAsync().ConfigureAwait(false);
        if (rows.Count == 0)
        {
            Console.WriteLine("No active session allows");
            return 0;
        }

        Ui.Title($"{ProductInfo.Name} session allows");
        foreach (var r in rows)
        {
            Ui.Line($"  [bold]{Ui.E(r.Id)}[/]  {Ui.E(r.LauncherPolicyKey)} {Ui.Dim($"({r.LauncherKind})  pid={r.Pid}")}");
            Ui.Line($"      {Ui.Ok($"{r.Tool} / {r.SecretName}")}  {Ui.Dim("class=")}{Ui.E(r.CommandClass)}");
            Ui.Line(Ui.Dim($"      granted {LocalTime(r.GrantedAtUtc)}  last used {LocalTime(r.LastUsedUtc)}  " +
                           $"idle expires {LocalTime(r.IdleExpiresUtc)}  {SessionAllowDisplay.Ends(r.EndsUtc)}"));
        }
        Console.WriteLine();
        Console.WriteLine("Revoke: cw policy sessions --revoke <id>   (or --revoke-all)");
        return 0;
    }

    private static string LocalTime(string isoUtc) => SessionAllowDisplay.LocalTime(isoUtc);

    private static PolicyStore LoadPolicyStore()
    {
        var store = new PolicyStore(PolicyStore.DefaultPath());
        store.Load();
        return store;
    }

    private static void PrintPolicyHelp()
    {
        Console.WriteLine("Usage: cw policy <subcommand>");
        Console.WriteLine("  list                         Show enrolled launchers and defaults");
        Console.WriteLine("  path                         Print policy.json path");
        Console.WriteLine("  enroll --kind <terminal|ai-harness> [--key <policyKey>]");
        Console.WriteLine("  enroll --account <name> [--kind ai-harness]   An agent account, for example CodexSandboxOffline, is the launcher");
        Console.WriteLine("  set <policyKey> <tool> <Deny|Read|Trusted|Full>");
        Console.WriteLine("  unenroll <policyKey>");
        Console.WriteLine("  sessions [--revoke <id> | --revoke-all]   List/withdraw active session allows");
        Console.WriteLine("  hello <off|secret-reveal|write-and-up>    When the Approval Gate asks for Windows Hello (default secret-reveal)");
        Console.WriteLine("  low-risk <ask|allow>                      allow: a low-risk write, like a push to a feature branch, runs with no popup (default ask)");
        Console.WriteLine();
        Console.WriteLine("Defaults: AI Harness → Read; Terminal → Trusted; unknown/unenrolled → Deny.");
        Console.WriteLine("Secret release auto-allows only when level × command class permits (see CONTEXT.md).");
    }

    private static int UnknownPolicy(string sub)
    {
        Console.Error.WriteLine($"Unknown policy subcommand: {sub}");
        PrintPolicyHelp();
        return 1;
    }

    private static async Task<int> DeleteAsync(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: cw delete <NAME>");
            return 1;
        }

        var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
        if (ensure is int code)
            return code;

        try
        {
            var deleted = await AgentVaultClient.DeleteAsync(args[0]).ConfigureAwait(false);
            Ui.Title($"{ProductInfo.Name} delete");
            Ui.Line($"  {Ui.Dim("secret:")} [bold]{Ui.E(args[0])}[/]");
            Ui.Line($"  {(deleted ? Ui.Ok("Deleted.") : Ui.Warn("Not present."))}");
            return 0;
        }
        catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
        {
            Console.Error.WriteLine("Session Agent is not reachable. Start it with: cw agent start");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"delete failed: {ex.Message}");
            return 1;
        }
    }

    private static string Describe(Exception ex) =>
        ex switch
        {
            OperationCanceledException => "connection timed out",
            _ when ex.InnerException is not null => $"{ex.GetType().Name}: {ex.InnerException.Message}",
            _ => $"{ex.GetType().Name}: {ex.Message}",
        };

    private static int AuditAsync(string[] args)
    {
        var max = 50;
        for (var i = 0; i < args.Length; i++)
        {
            if (IsHelp(args[i]))
            {
                Console.WriteLine("Usage: cw audit [-n <count>]");
                Console.WriteLine("  Show recent gate decisions from the local audit trail.");
                Console.WriteLine($"  Default count: 50. Product root: {ProductPaths.Root()}");
                return 0;
            }

            if (args[i] is "-n" or "--count" or "--tail")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out max) || max < 1)
                {
                    Console.Error.WriteLine("Expected positive integer after -n.");
                    return 1;
                }

                i++;
            }
        }

        var log = new AuditLog();
        // Prune on read path so trail stays bounded without requiring agent restart (#38).
        log.PruneOlderThan();
        var lines = log.ReadRecentLines(max);
        if (lines.Count == 0)
        {
            Console.WriteLine($"{ProductInfo.Name} audit: (no gate decisions yet)");
            Console.WriteLine($"  path: {log.DirectoryPath}");
            return 0;
        }

        Ui.Title($"{ProductInfo.Name} audit (last {lines.Count})");
        Ui.Kv("path", log.DirectoryPath);
        foreach (var line in lines)
        {
            var r = AuditFormatter.Parse(line);
            if (r is null)
            {
                Console.WriteLine(AuditFormatter.FormatLine(line));
                continue;
            }
            var decision = r.Decision switch
            {
                GateDecisions.AutoAllow or GateDecisions.AllowOnce or GateDecisions.SessionGrant or GateDecisions.SessionAllow => Ui.Ok(r.Decision.PadRight(13)),
                GateDecisions.Deny => Ui.Fail(r.Decision.PadRight(13)),
                _ => Ui.Warn(r.Decision.PadRight(13)),
            };
            var reason = r.Reason.Length > 0 ? "  " + Ui.Dim(r.Reason) : "";
            Ui.Line($"  {Ui.Dim(LocalTime(r.Ts))}  {decision} {Ui.E(r.Tool),-7} {Ui.E(r.CommandClass),-13} {Ui.E(r.Level),-7} [bold]{Ui.E(r.Secret)}[/]");
            var launcher = r.AgentAccount.Length > 0 ? r.AgentAccount + " (agent account)" : r.LauncherKey;
            Ui.Line($"      {Ui.Dim("launcher " + Ui.E(launcher))}{reason}");
            if (AgentReason.Clean(r.AgentReason) is { } says)
                Ui.Line($"      {Ui.Dim(Ui.E($"{AgentReason.Label} “{says}”"))}");
        }

        return 0;
    }

    private static int ScanAsync(string[] args)
    {
        if (args is ["--move-to-vault"])
            return MoveMcpSecretsToVault();
        for (var i = 0; i < args.Length; i++)
        {
            if (IsHelp(args[i]))
            {
                Console.WriteLine("Usage: cw scan [--move-to-vault]");
                Console.WriteLine("  --move-to-vault  Move each plain MCP server secret into the vault; the server starts through cw inject.");
                Console.WriteLine("  Run first-catalog residual-risk detectors (read-only).");
                Console.WriteLine("  Findings never include secret values. No auto-harden.");
                Console.WriteLine($"  Product root: {ProductPaths.Root()}");
                return 0;
            }
        }

        var engine = new ScanEngine();
        var findings = Ui.Status("Scanning first-catalog tools...", () => engine.Run(new ScanContext()));
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            Console.Write(ScanFormatter.Format(findings));
            return 0;
        }

        Ui.Title($"{ProductInfo.Name} scan");
        Ui.Kv("findings", findings.Count.ToString());
        if (findings.Count == 0)
            Ui.Line($"  {Ui.Ok("(no findings)")}");
        foreach (var f in findings)
        {
            var sev = f.Severity.ToString().ToLowerInvariant() switch
            {
                "high" or "critical" => Ui.Fail(f.Severity.ToString()),
                "medium" => Ui.Warn(f.Severity.ToString()),
                _ => Ui.Dim(f.Severity.ToString()),
            };
            var body = new StringBuilder();
            body.AppendLine($"{Ui.Dim("tool:")}         {Ui.E(f.Tool)}");
            body.AppendLine($"{Ui.Dim("title:")}        {Ui.E(f.Title)}");
            body.AppendLine($"{Ui.Dim("summary:")}      {Ui.E(f.Summary)}");
            body.Append($"{Ui.Dim("evidence:")}     {Ui.E(f.Evidence)}");
            if (!string.IsNullOrWhiteSpace(f.Remediation))
                body.Append($"\n{Ui.Dim("remediation:")}  {Ui.E(f.Remediation)}");
            if (!string.IsNullOrWhiteSpace(f.HardenHint))
                body.Append($"\n{Ui.Dim("harden_hint:")}  {Ui.E(f.HardenHint)}");
            AnsiConsole.Write(new Panel(body.ToString()).Header($"{sev} {Ui.E(f.Id)}").Border(BoxBorder.Rounded).Expand());
        }
        // Exit 0 always for successful scan; findings are informational (not a CI fail gate in v1).
        return 0;
    }

    /// <summary>#28: apply every "Move to vault" fix the scan offers.</summary>
    private static int MoveMcpSecretsToVault()
    {
        if (!OperatingSystem.IsWindows())
            return 1;
        var fixes = new ScanEngine([new McpConfigSecretDetector()]).Run(new ScanContext())
            .Select(f => McpSecretLocation.FromFix(f.Fix))
            .OfType<McpSecretLocation>()
            .ToList();
        if (fixes.Count == 0)
        {
            Console.WriteLine("No plain MCP server secrets to move.");
            return 0;
        }
        if (fixes.Any(f => f.File.EndsWith(".claude.json", StringComparison.OrdinalIgnoreCase))
            && System.Diagnostics.Process.GetProcessesByName("claude").Length > 0)
        {
            Console.Error.WriteLine("Claude Code is running and rewrites ~/.claude.json. Close it, then run this again.");
            return 1;
        }
        var cw = CmdWarden.Cli.Hooks.HookInstaller.SelfCommandParts();
        var vault = new CredentialVault();
        var failed = 0;
        foreach (var fix in fixes)
        {
            try
            {
                var name = McpSecretMover.Move(fix, vault, cw);
                Ui.Line($"  {Ui.Ok("moved")}  {Ui.E(fix.File)}: {Ui.E(fix.Describe())} -> vault {Ui.E(name)}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                failed++;
                Ui.Line($"  {Ui.Fail("failed")} {Ui.E(fix.File)}: {Ui.E(fix.Describe())}: {Ui.E(ex.Message)}");
            }
        }
        Ui.Line(Ui.Dim("  Restart the harness so it starts the servers through cw inject."));
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> HardenAsync(string[] args)
    {
        if (args.Length > 0 && args[0] is "--list" or "-l")
        {
            PrintHardenedTools();
            return 0;
        }

        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.WriteLine("Usage: cw harden <tool>");
            Console.WriteLine("  cw harden --list  One row per catalog tool, same probe as cw doctor.");
            Console.WriteLine("  cw harden gh      Discover real gh, pin, install PATH shim, import token (compat).");
            Console.WriteLine("  cw harden git     Discover real git, pin, install PATH shim (compat; leave GCM).");
            Console.WriteLine("  cw harden az      Discover real az, pin, install PATH shim (compat; leave MSAL).");
            Console.WriteLine("  cw harden docker  Discover real docker, pin, install PATH shim (compat; leave Desktop store).");
            Console.WriteLine("  cw harden ssh     Gate each ssh key sign: SSH_AUTH_SOCK points to the CmdWarden ssh-agent pipe.");
            Console.WriteLine("                    --upstream <pipe> names the real agent (default \\.\\pipe\\openssh-ssh-agent).");
            Console.WriteLine("  cw harden <pack>  Pin a tool from its tool pack and install the pack shim, for example npm, aws, kubectl.");
            Console.WriteLine($"                    Add your own packs as JSON files in {ToolPacks.UserDir()}.");
            Console.WriteLine("Options:");
            Console.WriteLine("  --path <exe>       Absolute path to real tool (skip discovery)");
            Console.WriteLine("  --skip-path        Do not modify user PATH");
            Console.WriteLine("  --strong           Move the tool's credentials into the vault (docker, git, gh, az)");
            Console.WriteLine("Options for gh only:");
            Console.WriteLine("  --token <value>    Import this token instead of calling gh auth token");
            Console.WriteLine("  --hostname <host>  Host for --token with --strong (default github.com)");
            Console.WriteLine("  --skip-token       Skip vault import");
            return args.Length > 0 && IsHelp(args[0]) ? 0 : 1;
        }

        var tool = args[0].ToLowerInvariant();
        if (tool == SshGate.Tool)
            return await HardenSshAsync(args.AsSpan(1).ToArray()).ConfigureAwait(false);
        if (!ToolCatalog.IsBuiltIn(tool))
            return HardenPack(tool, args.AsSpan(1).ToArray());

        string? realPath = null;
        string? tokenOverride = null;
        var skipToken = false;
        var skipPath = false;
        var strong = false;
        var hostname = GhVaultNames.DefaultHost;
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--path" && i + 1 < args.Length)
                realPath = args[++i];
            else if (a is "--token" && i + 1 < args.Length)
                tokenOverride = args[++i];
            else if (a is "--hostname" && i + 1 < args.Length && tool == "gh")
                hostname = args[++i];
            else if (a is "--skip-token")
                skipToken = true;
            else if (a is "--skip-path")
                skipPath = true;
            else if (a is "--strong" && tool is "docker" or "git" or "gh" or "az")
                strong = true;
            else
            {
                Console.Error.WriteLine($"Unknown harden option: {a}");
                return 1;
            }
        }

        if (tool == "git")
            return HardenGit(realPath, skipPath, strong);
        if (tool == "az")
            return HardenAz(realPath, skipPath, strong);
        if (tool == "docker")
            return HardenDocker(realPath, skipPath, strong);

        try
        {
            // A compat re-pin keeps an earlier strong mode. Strong needs no compat token import.
            strong = strong || new ToolPinStore().TryGet(GhHarden.ToolId)?.IsStrong == true;
            if (!skipToken && !strong)
            {
                var ensure = await EnsureAgentClientAsync().ConfigureAwait(false);
                if (ensure is int code)
                    return code;
            }

            Ui.Title($"{ProductInfo.Name} harden gh ({(strong ? "strong" : "compat")} mode)");
            var result = await Ui.StatusAsync("Pinning gh and installing the shim...", () => GhHarden.RunAsync(new GhHardenOptions
            {
                RealGhPath = realPath,
                TokenOverride = tokenOverride,
                SkipTokenImport = skipToken || strong,
                SkipUserPath = skipPath,
            })).ConfigureAwait(false);

            Ui.Kv("real gh", result.RealGhPath);
            Ui.Kv("pin sha256", result.PinSha256);
            Ui.Kv("shim", result.ShimExePath);
            Ui.Kv("shims dir", result.ShimsDir);
            Ui.Kv("user PATH", (result.UserPathUpdated ? "updated (prepended shims dir)" : "unchanged / skipped"));
            if (strong && OperatingSystem.IsWindows())
            {
                var migrated = GhStrongHarden.Migrate(new GhStrongOptions { TokenOverride = tokenOverride, Hostname = hostname });
                Ui.Kv("tokens", $"{migrated.Migrated.Count} entries in the vault; {migrated.Deleted.Count} stock entries erased{(migrated.HostsStripped ? "; oauth_token stripped from hosts.yml" : "")}");
                foreach (var h in migrated.Hosts)
                    Ui.Kv("host", $"{h.Host} - {h.Users.Count} accounts, active {h.ActiveUser ?? "(none)"}");
            }
            else
            {
                Ui.Kv("token", (result.TokenImported ? "imported as GH_TOKEN" : result.TokenImportNote));
            }
            Console.WriteLine();
            Ui.Line(Ui.Dim("Next: cw policy enroll --kind terminal"));
            Ui.Line(Ui.Dim("Then: open a new shell (PATH refresh) and run gh via PATH."));
            if (!strong)
                Ui.Line(Ui.Dim("Note: absolute-path to real gh bypasses the shim (compat residual). Run cw harden gh --strong to move the tokens."));
            PrintShimOrderHint("gh");
            return 0;
        }
        catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
        {
            Console.Error.WriteLine(
                "Session Agent is not reachable (required for token import). Start it with: cw agent start");
            Console.Error.WriteLine("  Or use: cw harden gh --skip-token");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"harden gh failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HardenSshAsync(string[] args)
    {
        string? upstream = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--upstream" && i + 1 < args.Length)
                upstream = args[++i];
            else
            {
                Console.Error.WriteLine($"Unknown harden option for ssh: {args[i]}");
                return 1;
            }
        }
        try
        {
            var result = SshHarden.Run(upstream);
            Ui.Title($"{ProductInfo.Name} harden ssh");
            Ui.Kv("real agent", result.Upstream);
            Ui.Kv("gate pipe", result.PipePath);
            Ui.Kv("SSH_AUTH_SOCK", "set for your user to the gate pipe");
            Ui.Kv("git ssh", result.GitSshCommandSet
                ? $"core.sshCommand = {result.GitSshCommand} (the Windows OpenSSH client reads the pipe)"
                : result.GitSshCommand is null ? "git not found; nothing changed" : $"core.sshCommand stays {result.GitSshCommand}");
            await RestartAgentAsync("ssh gate", "the ssh gate").ConfigureAwait(false);
            Console.WriteLine();
            Ui.Line(Ui.Dim("Next: open a new terminal and restart your AI harness, so they read the new SSH_AUTH_SOCK."));
            Ui.Line(Ui.Dim("Then: cw policy set <launcher> ssh <level> sets who signs with no popup (read = git fetch, write = push or shell)."));
            Ui.Line(Ui.Dim($"Note: a process that talks to {result.Upstream} directly skips the gate (compat residual)."));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"harden ssh failed: {ex.Message}");
            return 1;
        }
    }

    private static int HardenPack(string tool, string[] args)
    {
        var (packs, errors) = ToolPacks.Load();
        var pack = packs.FirstOrDefault(p => p.Tool == tool);
        if (pack is null)
        {
            foreach (var error in errors)
                Console.Error.WriteLine($"pack not loaded: {error.Message}");
            Console.Error.WriteLine($"No tool pack for '{tool}'. Tools: {string.Join(", ", ToolCatalog.All().Select(t => t.Id))}.");
            return 1;
        }
        string? realPath = null;
        var skipPath = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--path" && i + 1 < args.Length)
                realPath = args[++i];
            else if (args[i] is "--skip-path")
                skipPath = true;
            else
            {
                Console.Error.WriteLine($"Unknown harden option for a pack tool: {args[i]}");
                return 1;
            }
        }
        try
        {
            var result = Ui.Status($"Pinning {tool} and installing the shim...", () => PackHarden.Run(pack, realPath, skipPath));
            Ui.Title($"{ProductInfo.Name} harden {tool} (tool pack)");
            Ui.Kv($"real {tool}", result.RealPath);
            Ui.Kv("pin sha256", result.PinSha256);
            Ui.Kv("shim", result.ShimExePath);
            Ui.Kv("pack", pack.Source);
            Ui.Kv("user PATH", result.UserPathUpdated ? "updated (prepended shims dir)" : "unchanged / skipped");
            Ui.Kv("secrets", pack.SecretEnv.Count == 0
                ? "none; the shim gates the run only"
                : $"{string.Join(", ", pack.SecretEnv)} from the vault when present (cw save <NAME>)");
            Console.WriteLine();
            Ui.Line(Ui.Dim("Next: cw policy enroll --kind terminal"));
            Ui.Line(Ui.Dim($"Then: open a new shell (PATH refresh) and run {tool} via PATH."));
            Ui.Line(Ui.Dim($"Note: absolute-path to real {tool} bypasses the shim (compat residual)."));
            PrintShimOrderHint(tool);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"harden {tool} failed: {ex.Message}");
            return 1;
        }
    }

    private static int HardenGit(string? realPath, bool skipPath, bool strong)
    {
        try
        {
            var result = Ui.Status("Pinning git and installing the shim...", () => GitHarden.Run(new GitHardenOptions
            {
                RealGitPath = realPath,
                SkipUserPath = skipPath,
            }));
            // A compat re-pin keeps an earlier strong mode.
            strong = strong || new ToolPinStore().TryGet(GitHarden.ToolId)?.IsStrong == true;
            Ui.Title($"{ProductInfo.Name} harden git ({(strong ? "strong" : "compat")} mode)");

            Ui.Kv("real git", result.RealGitPath);
            Ui.Kv("pin sha256", result.PinSha256);
            Ui.Kv("shim", result.ShimExePath);
            Ui.Kv("shims dir", result.ShimsDir);
            Ui.Kv("user PATH", (result.UserPathUpdated ? "updated (prepended shims dir)" : "unchanged / skipped"));
            if (strong && OperatingSystem.IsWindows())
            {
                var migrated = GitStrongHarden.Migrate(new GitStrongOptions());
                Ui.Kv("credentials", $"{migrated.MigratedKeys.Count} {migrated.Namespace}: entries moved to the vault; legacy entries erased");
                Ui.Kv("helper", $"credential.helper = [\"\", {migrated.HelperValue}] (was: {(migrated.PreviousHelpers.Count == 0 ? "(unset)" : string.Join(", ", migrated.PreviousHelpers))})");
                foreach (var key in migrated.RemovedGhBlocks.Select(b => b.Key).Distinct())
                    Ui.Kv("gh block", $"removed {key} (saved for cw unharden git)");
            }
            else
            {
                Ui.Kv("credentials", result.CredentialNote);
            }
            Console.WriteLine();
            Ui.Line(Ui.Dim("Next: cw policy enroll --kind terminal"));
            Ui.Line(Ui.Dim("Then: open a new shell (PATH refresh) and run git via PATH."));
            if (!strong)
            {
                Ui.Line(Ui.Dim("Note: absolute-path to real git bypasses the shim (compat residual)."));
                Ui.Line(Ui.Dim("Note: allowed git still uses ambient GCM / credential stores (gate only). Run cw harden git --strong to move them."));
            }
            PrintShimOrderHint("git");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"harden git failed: {ex.Message}");
            return 1;
        }
    }

    private static int HardenAz(string? realPath, bool skipPath, bool strong)
    {
        try
        {
            // A compat re-pin keeps an earlier strong mode.
            strong = strong || new ToolPinStore().TryGet(AzHarden.ToolId)?.IsStrong == true;
            Ui.Title($"{ProductInfo.Name} harden az ({(strong ? "strong" : "compat")} mode)");
            var result = Ui.Status("Pinning az and installing the shim...", () => AzHarden.Run(new AzHardenOptions
            {
                RealAzPath = realPath,
                SkipUserPath = skipPath,
            }));

            Ui.Kv("real az", result.RealAzPath);
            Ui.Kv("pin sha256", result.PinSha256);
            Ui.Kv("shim", result.ShimExePath);
            Ui.Kv("shims dir", result.ShimsDir);
            Ui.Kv("user PATH", (result.UserPathUpdated ? "updated (prepended shims dir)" : "unchanged / skipped"));
            if (strong && OperatingSystem.IsWindows())
            {
                var migrated = AzStrongHarden.Migrate();
                Ui.Kv("login", migrated.MovedLoginFiles.Count == 0
                    ? $"no login files in {migrated.StockDir}; az login through the shim saves one"
                    : $"{migrated.MovedLoginFiles.Count} files moved from {migrated.StockDir} to the CmdWarden store");
                Ui.Kv("store", migrated.StorePath);
            }
            else
            {
                Ui.Kv("credentials", result.CredentialNote);
            }
            Console.WriteLine();
            Ui.Line(Ui.Dim("Next: cw policy enroll --kind terminal"));
            Ui.Line(Ui.Dim("Then: open a new shell (PATH refresh) and run az via PATH."));
            if (strong)
            {
                Ui.Line(Ui.Dim("Note: az without the shim now has no login. Undo: cw unharden az."));
            }
            else
            {
                Ui.Line(Ui.Dim("Note: absolute-path to real az bypasses the shim (compat residual)."));
                Ui.Line(Ui.Dim("Note: allowed az still uses ambient MSAL under ~/.azure (gate only). Run cw harden az --strong to move the login."));
            }
            PrintShimOrderHint("az");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"harden az failed: {ex.Message}");
            return 1;
        }
    }

    private static int HardenDocker(string? realPath, bool skipPath, bool strong)
    {
        try
        {
            var result = Ui.Status("Pinning docker and installing the shim...", () => DockerHarden.Run(new DockerHardenOptions
            {
                RealDockerPath = realPath,
                SkipUserPath = skipPath,
            }));
            // A compat re-pin keeps an earlier strong mode.
            strong = strong || new ToolPinStore().TryGet(DockerHarden.ToolId)?.IsStrong == true;
            Ui.Title($"{ProductInfo.Name} harden docker ({(strong ? "strong" : "compat")} mode)");

            Ui.Kv("real docker", result.RealDockerPath);
            Ui.Kv("pin sha256", result.PinSha256);
            Ui.Kv("shim", result.ShimExePath);
            Ui.Kv("shims dir", result.ShimsDir);
            Ui.Kv("user PATH", (result.UserPathUpdated ? "updated (prepended shims dir)" : "unchanged / skipped"));
            if (strong && OperatingSystem.IsWindows())
            {
                var migrated = DockerStrongHarden.Migrate(new DockerStrongOptions());
                Ui.Kv("credentials", $"{migrated.MigratedUrls.Count} registries in vault; legacy entries erased");
                Ui.Kv("config", $"{migrated.ConfigPath} (credsStore: cmdwarden, was: {migrated.PreviousCredsStore ?? "(none)"})");
                foreach (var warning in migrated.Warnings)
                    Ui.Line($"  {Ui.Warn("warning:")} {Ui.E(warning)}");
            }
            else
            {
                Ui.Kv("credentials", result.CredentialNote);
            }
            Console.WriteLine();
            Ui.Line(Ui.Dim("Next: cw policy enroll --kind terminal"));
            Ui.Line(Ui.Dim("Then: open a new shell (PATH refresh) and run docker via PATH."));
            if (!strong)
            {
                Ui.Line(Ui.Dim("Note: absolute-path to real docker bypasses the shim (compat residual)."));
                Ui.Line(Ui.Dim($"Note: {HelperTools.DockerHelperExe} is installed. Run cw harden docker --strong to route registry credentials through it."));
            }
            PrintShimOrderHint("docker");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"harden docker failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Ui.Line($"[bold]{Ui.E(ProductInfo.Name)}[/] - CLI secret gate by tool and launcher identity");
        Console.WriteLine();
        Ui.Line($"Usage: [bold]{Ui.E(ProductInfo.CliPrimary)}[/] <command>");
        Ui.Line($"   or: [bold]{Ui.E(ProductInfo.CliAlias)}[/] <command>");
        Console.WriteLine();
        var table = Ui.Table("command", "what it does").Border(TableBorder.None).HideHeaders();
        void Row(string cmd, string text) => table.AddRow($"[bold]{Ui.E(cmd)}[/]", Ui.E(text));
        Row("help", "Show this help");
        Row("version", "Show version");
        Row("doctor [--fix-path]", "Check Session Agent + vault UI / Start Menu shortcut; --fix-path elevates once");
        Row("agent start|stop|status", "Session Agent lifecycle");
        Row("whoami", "Show hybrid launcher identity for this caller");
        Row("save <NAME>", "Store secret in Credential Manager via agent");
        Row("inject +NAME -- cmd", "Run cmd with secret only in child env [--tool T] [--class read|write|secret-reveal|unknown]");
        Row("delete <NAME>", "Remove secret from vault");
        Row("policy ...", "List/enroll/set tool x launcher policy levels");
        Row("harden gh|git|az|docker", "Pin real tool, install PATH shim (gh also imports token)");
        Row("harden docker|git|gh|az --strong", "Also move the tool's credentials into the vault (vault-only)");
        Row("harden ssh", "Ask before an AI harness signs with an ssh key (git push over ssh)");
        Row("harden <pack tool>", "Gate a tool from its tool pack: npm, aws, kubectl, or your own JSON pack");
        Row("harden --list", "One status row per catalog tool and tool pack");
        Row("unharden docker|git|gh|az|ssh|<pack tool>", "Restore the stock store and config, remove pin and shim");
        Row("github app setup|status|remove", "gh gets a GitHub App token for one repo that ends in one hour");
        Row("audit [-n N]", "Show recent gate decisions (local audit trail)");
        Row("scan", "First-catalog residual risk detectors (read-only)");
        Row("launch claude|codex|cursor [-- args]", "Start an AI harness without token variables; enroll it if needed");
        Row("leak-guard install|uninstall claude|cursor", "Hide vaulted secret values in tool output from the model");
        Row("hook install|uninstall claude|cursor", "Check policy before the harness runs a command; a deny stops it early");
        Row("mcp [serve] | mcp install|uninstall claude|cursor", "MCP server: run_with_secret, list_allowed, why_denied");
        Row("canary install [--env F]|remove|status", "Fake tokens that show an attack when used");
        Row("shortcut install [--desktop]|remove|status", "Start Menu (and Desktop) entry for CmdWarden Vault");
        Row("update [--check]", "Install the newest release (checks the sha256 of the setup zip)");
        Row("uninstall", "Run the uninstaller of Settings > Apps > CmdWarden");
        AnsiConsole.Write(table);
        Console.WriteLine();
        Ui.Line(Ui.Dim("Session Agent: cw agent start  (or CW_AGENT_PATH / bundled agent/ layout)"));
        Ui.Line(Ui.Dim("  dev fallback: dotnet run --project src/CmdWarden.Agent"));
        Ui.Line(Ui.Dim("Vault UI: bundled under secrets-manager/ (CW_SECRETS_MANAGER_PATH override)"));
        Console.WriteLine();
        Ui.Line(Ui.Dim("Release requires enrolled launcher + auto-allowed level x class (cw policy enroll)."));
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintHelp();
        return 1;
    }
}
