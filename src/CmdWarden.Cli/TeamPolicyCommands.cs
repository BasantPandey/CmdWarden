using System.ComponentModel;
using System.Diagnostics;
using CmdWarden.Contracts;

namespace CmdWarden.Cli;

/// <summary>
/// <c>cw policy sync</c> (#47): read the team file from a git repo and save it next to policy.json.
/// The team rules only lower levels, so a changed repo cannot open the gate.
/// </summary>
public static class TeamPolicyCommands
{
    public static int Sync(string[] args, string? policyPath = null)
    {
        string? repo = null;
        string? gitRef = null;
        string? file = null;
        var remove = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--file" when i + 1 < args.Length:
                    file = args[++i];
                    break;
                case "--ref" when i + 1 < args.Length:
                    gitRef = args[++i];
                    break;
                case "--remove":
                    remove = true;
                    break;
                case var a when !a.StartsWith('-') && repo is null:
                    repo = a;
                    break;
                default:
                    PrintHelp();
                    return args[i] is "-h" or "--help" or "help" ? 0 : 1;
            }
        }

        var target = TeamPolicy.PathFor(policyPath ?? PolicyStore.DefaultPath());
        if (remove)
        {
            File.Delete(target);
            Console.WriteLine("Team rules removed. Only the local policy applies.");
            return 0;
        }

        // No repo: sync again from the saved source.
        TeamPolicy? saved = null;
        if (repo is null)
        {
            try
            {
                saved = TeamPolicy.Load(target);
            }
            catch (InvalidDataException)
            {
                // A damaged file has no trusted source; the person must name the repo.
            }
            if (saved?.Source is null)
            {
                PrintHelp();
                return 1;
            }
        }
        repo ??= saved!.Source!;
        gitRef ??= saved?.Ref;
        file ??= saved?.File ?? TeamPolicy.DefaultRepoFile;

        var clone = Path.Combine(Path.GetTempPath(), "cw-team-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cloneArgs = new List<string> { "clone", "--quiet", "--depth", "1" };
            if (gitRef is not null)
                cloneArgs.AddRange(["--branch", gitRef]);
            cloneArgs.AddRange(["--", repo, clone]);
            Git(cloneArgs);
            var commit = Git(["-C", clone, "rev-parse", "HEAD"]).Trim();

            var path = Path.GetFullPath(Path.Combine(clone, file));
            if (!path.StartsWith(clone + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException($"The repo has no file {file}.");
            var team = TeamPolicy.Parse(File.ReadAllText(path));
            team.Source = repo;
            team.Ref = gitRef;
            team.File = file;
            team.Commit = commit;
            team.SyncedAtUtc = DateTime.UtcNow.ToString("o");
            team.Save(target);

            Console.WriteLine($"Team rules from {repo} {file} at {commit[..Math.Min(12, commit.Length)]}:");
            foreach (var line in team.Lines())
                Console.WriteLine("  " + line);
            if (team.Lines().Count == 0)
                Console.WriteLine("  (no rules)");
            Console.WriteLine("Team rules only lower a level. See them in: cw policy list");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or Win32Exception)
        {
            Console.Error.WriteLine($"cw policy sync: {ex.Message}");
            Console.Error.WriteLine("The team rules did not change.");
            return 1;
        }
        finally
        {
            DeleteClone(clone);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: cw policy sync <git repo> [--file <path>] [--ref <branch>]");
        Console.WriteLine("       cw policy sync            Read the team file again from the saved repo.");
        Console.WriteLine("       cw policy sync --remove   Remove the team rules.");
        Console.WriteLine($"  Read the team file (default {TeamPolicy.DefaultRepoFile}) and save it next to policy.json.");
        Console.WriteLine("  A team rule sets the maximum level for a launcher kind and tool. It never raises a level.");
        Console.WriteLine("  Example file: { \"maxLevels\": { \"ai-harness\": { \"*\": \"Read\", \"az\": \"Deny\" } } }");
    }

    private static string Git(IEnumerable<string> args)
    {
        var gitPath = InjectRunner.ResolveProgram("git");
        if (!Path.IsPathRooted(gitPath))
            throw new InvalidOperationException("git is not on PATH.");
        var psi = new ProcessStartInfo(gitPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var git = Process.Start(psi) ?? throw new InvalidOperationException("git did not start.");
        var stderr = git.StandardError.ReadToEndAsync();
        var stdout = git.StandardOutput.ReadToEnd();
        if (!git.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            git.Kill(entireProcessTree: true);
            throw new InvalidOperationException("git did not finish in 2 minutes.");
        }
        if (git.ExitCode != 0)
            throw new InvalidOperationException($"git {psi.ArgumentList[0]} failed: {stderr.Result.Trim()}");
        return stdout;
    }

    /// <summary>git marks its object files read-only, so clear that before the delete.</summary>
    private static void DeleteClone(string dir)
    {
        if (!Directory.Exists(dir))
            return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that stays has no secret in it.
        }
    }
}
