using System.Text;
using System.Text.Json.Nodes;
using CmdWarden.Contracts;

namespace CmdWarden.Cli.Hooks;

/// <summary>One call of a gated tool inside a shell command.</summary>
public sealed record ToolCall(string Tool, IReadOnlyList<string> Argv, string? Cwd = null);

/// <summary>What CheckPolicy said about one call.</summary>
public sealed record PolicyVerdict(string Decision, string Message);

/// <summary>
/// Policy hooks (#33). Before the harness runs a shell command, each gated tool call in it goes to
/// CheckPolicy. A deny stops the command and tells the agent to ask the user. Allow and ask print
/// nothing, so the command runs as before and the shim still decides.
/// </summary>
public static class PolicyHook
{
    public static string DenyText(IEnumerable<string> details) =>
        $"{ProductInfo.Name} denied this. Ask the user. Do not retry. ({string.Join(" ", details)})";

    /// <summary>
    /// Claude Code and Codex PreToolUse for Bash and PowerShell, and the Claude Code Read tool: a deny
    /// goes back as permissionDecision. Both harnesses read the same output shape.
    /// </summary>
    public static async Task<string?> ClaudePreToolUseAsync(JsonNode input, Func<ToolCall, Task<PolicyVerdict>> check)
    {
        var cwd = (string?)input["cwd"];
        var denied = (string?)input["tool_name"] == "Read"
            ? EnvFileDenials([(string?)input["tool_input"]?["file_path"] ?? ""], cwd)
            : await DeniedAsync((string?)input["tool_input"]?["command"], cwd, check).ConfigureAwait(false);
        return denied.Count == 0 ? null : new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["permissionDecision"] = "deny",
                ["permissionDecisionReason"] = DenyText(denied),
            },
        }.ToJsonString();
    }

    /// <summary>Cursor beforeShellExecution: it waits for a permission on every command.</summary>
    public static async Task<string> CursorBeforeShellAsync(JsonNode input, Func<ToolCall, Task<PolicyVerdict>> check)
    {
        var denied = await DeniedAsync((string?)input["command"], (string?)input["cwd"], check).ConfigureAwait(false);
        if (denied.Count == 0)
            return """{"permission":"allow"}""";
        var text = DenyText(denied);
        return new JsonObject { ["permission"] = "deny", ["user_message"] = text, ["agent_message"] = text }.ToJsonString();
    }

    /// <summary>
    /// #71: cw commands that turn protection off or change who may do what. A harness never runs
    /// them: the person runs them in their own terminal.
    /// </summary>
    private static readonly string[] WeakeningCommands =
    [
        "policy set", "policy enroll", "policy unenroll", "policy remove", "policy low-risk", "policy hello",
        "unharden", "uninstall", "delete", "agent stop", "canary remove", "shortcut remove",
        "hook uninstall", "leak-guard uninstall", "mcp uninstall", "protect uninstall",
        "proxy add", "proxy remove", "proxy strict", "proxy uninstall", "github app remove",
    ];

    public const string SelfProtectText = "This changes CmdWarden protection. Ask the user to run it in their own terminal.";

    /// <summary>#97: cw commands that show a secret value. The agent would see it, so only the person runs them.</summary>
    private static readonly string[] RevealCommands = ["read"];

    public const string RevealText = "This shows a secret value to the agent. Use cw inject or cw://NAME, or ask the user to run it in their own terminal.";

    /// <summary>The cw commands in a command line that <see cref="WeakeningCommands"/> names.</summary>
    public static IReadOnlyList<string> WeakeningCwCalls(string command) => CwCalls(command, WeakeningCommands);

    /// <summary>The cw commands in a command line that <see cref="RevealCommands"/> names.</summary>
    public static IReadOnlyList<string> RevealCwCalls(string command) => CwCalls(command, RevealCommands);

    private static List<string> CwCalls(string command, string[] commands)
    {
        var found = new List<string>();
        foreach (var words in Commands(command))
        {
            var i = ProgramIndex(words);
            if (i >= words.Count || ProgramName(words[i]) is not ("cw" or "cmdwarden"))
                continue;
            var args = string.Join(' ', words.Skip(i + 1).Select(w => w.ToLowerInvariant()));
            if (commands.FirstOrDefault(c => args == c || args.StartsWith(c + " ", StringComparison.Ordinal)) is { } hit)
                found.Add("cw " + hit);
        }
        return found;
    }

    private static async Task<List<string>> DeniedAsync(string? command, string? cwd, Func<ToolCall, Task<PolicyVerdict>> check)
    {
        var denied = EnvFileDenials(EnvFileReads(command ?? ""), cwd);
        denied.AddRange(WeakeningCwCalls(command ?? "").Select(c => $"{c}: {SelfProtectText}"));
        denied.AddRange(RevealCwCalls(command ?? "").Select(c => $"{c}: {RevealText}"));
        foreach (var call in FindToolCalls(command ?? ""))
        {
            var verdict = await check(call with { Cwd = cwd }).ConfigureAwait(false);
            if (verdict.Decision == PolicyCheckDecisions.Deny)
                denied.Add(verdict.Message);
        }
        return denied;
    }

    /// <summary>
    /// The gated tool calls in a bash or PowerShell command line. It splits on unquoted
    /// <c>; &amp; | ( )</c> and new lines, skips <c>NAME=value</c> and the PowerShell call operator,
    /// and reads the program name without its folder and extension.
    /// ponytail: a guess at the words, not a shell. It misses calls inside $(...), eval, or a
    /// script file. That is safe: the shim still gates every run; this only warns the agent early.
    /// </summary>
    public static IReadOnlyList<ToolCall> FindToolCalls(string command)
    {
        var calls = new List<ToolCall>();
        var known = ToolCatalog.All();
        foreach (var words in Commands(command))
        {
            var i = ProgramIndex(words);
            if (i >= words.Count)
                continue;
            var name = ProgramName(words[i]);
            if (known.Any(t => t.Id == name))
                calls.Add(new ToolCall(name, words.Skip(i + 1).ToList()));
        }
        return calls;
    }

    /// <summary>The index of the program word: after NAME=value words and the call operator.</summary>
    private static int ProgramIndex(List<string> words)
    {
        var i = 0;
        while (i < words.Count && (words[i] is "&" or "command" or "exec" || IsAssignment(words[i])))
            i++;
        return i;
    }

    /// <summary>The program name without its folder and extension, in lower case.</summary>
    private static string ProgramName(string word) =>
        word == "." ? "." : Path.GetFileNameWithoutExtension(word.Replace('\\', '/').Split('/')[^1]).ToLowerInvariant();

    private static readonly HashSet<string> FileReaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "cat", "type", "get-content", "gc", "more", "less", "head", "tail", "bat", "nl", "sed", "awk", "grep", "rg",
        "findstr", "select-string", "sls", "strings", "xxd", "od", "base64", "cp", "copy", "source", ".",
    };

    /// <summary>
    /// #67: the dotenv files a command reads: a file reader, or source, with a .env or .env.* file.
    /// ponytail: the same word guess as <see cref="FindToolCalls"/>; a script that reads the file is not seen.
    /// </summary>
    public static IReadOnlyList<string> EnvFileReads(string command)
    {
        var files = new List<string>();
        foreach (var words in Commands(command))
        {
            var i = ProgramIndex(words);
            if (i < words.Count && FileReaders.Contains(ProgramName(words[i])))
                files.AddRange(words.Skip(i + 1).Where(IsEnvFile));
        }
        return files;
    }

    private static bool IsEnvFile(string path)
    {
        var name = Path.GetFileName(path.Replace('\\', '/'));
        return name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".example", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".sample", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".template", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A deny message for each dotenv file that still holds a plain secret value. A file of
    /// <c>KEY=cw://NAME</c> lines holds no value, so a read of it is fine.
    /// </summary>
    public static List<string> EnvFileDenials(IEnumerable<string> paths, string? cwd)
    {
        var denied = new List<string>();
        foreach (var path in paths.Where(p => p.Length > 0 && IsEnvFile(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.IsPathRooted(path) || cwd is null ? path : Path.Combine(cwd, path);
            if (HoldsPlainSecret(full))
                denied.Add($"{path} holds plain secret values. Run the program with: cw inject --env-file {path} -- <command>. The user can move the values with: cw env import {path}.");
        }
        return denied;
    }

    private static bool HoldsPlainSecret(string path)
    {
        try
        {
            return File.Exists(path) && DotEnvFile.Parse(File.ReadAllText(path))
                .Any(l => l.Key is not null && l.VaultRef is null && !string.IsNullOrEmpty(l.Value) && DotEnvFile.LooksSecret(l.Key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsAssignment(string word)
    {
        var eq = word.IndexOf('=');
        return eq > 0 && word[..eq].All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !char.IsAsciiDigit(word[0]);
    }

    /// <summary>
    /// #97: the words of each command, and of each command that a wrapper runs: <c>cmd /c</c>,
    /// <c>powershell -Command</c> or <c>-EncodedCommand</c>, <c>bash -c</c>, <c>Start-Process</c>, and
    /// <c>dotnet cw.dll</c>, which becomes <c>cw</c>.
    /// Each level is shorter text, so the depth limit only stops a pathological input.
    /// ponytail: a renamed copy of cw, a script file, or eval stays unseen.
    /// </summary>
    public const int MaxWrapperDepth = 16;

    private static List<List<string>> Commands(string command, int depth = 0)
    {
        var commands = new List<List<string>>();
        foreach (var words in Segments(command))
        {
            commands.Add(words);
            var i = ProgramIndex(words);
            if (depth >= MaxWrapperDepth || i >= words.Count)
                continue;
            if (Unwrap(ProgramName(words[i]), words.Skip(i + 1).ToList()) is { } inner)
                commands.AddRange(Commands(inner, depth + 1));
        }
        return commands;
    }

    /// <summary>The command line that the wrapper runs, or null when the program is no wrapper.</summary>
    private static string? Unwrap(string program, List<string> args)
    {
        // One word is the command line itself, as in cmd /c "...". In more words, a word with a
        // space had quotes; put them back, so the next split gives the same words.
        static string Join(IEnumerable<string> words)
        {
            var list = words.ToList();
            return list.Count == 1 ? list[0]
                : string.Join(' ', list.Select(w => w.Any(char.IsWhiteSpace) ? "\"" + w.Replace("\"", "\\\"") + "\"" : w));
        }
        int Flag(Func<string, bool> match) => args.FindIndex(a => match(a.ToLowerInvariant()));
        switch (program)
        {
            case "cmd":
                var c = Flag(a => a is "/c" or "/k");
                return c >= 0 ? Join(args.Skip(c + 1)) : null;
            case "powershell" or "pwsh":
                for (var k = 0; k < args.Count; k++)
                {
                    var a = args[k].ToLowerInvariant();
                    if (a.Length > 1 && a[0] == '-' && (a == "-ec" || "-encodedcommand".StartsWith(a, StringComparison.Ordinal)))
                    {
                        try
                        {
                            return k + 1 < args.Count ? Encoding.Unicode.GetString(Convert.FromBase64String(args[k + 1])) : null;
                        }
                        catch (FormatException)
                        {
                            return null;
                        }
                    }
                    if (a.Length > 1 && a[0] == '-' && "-command".StartsWith(a, StringComparison.Ordinal))
                        return Join(args.Skip(k + 1));
                    if (a is "-f" or "-file")
                        return null;
                    if (a is "-executionpolicy" or "-ep" or "-ex" or "-windowstyle" or "-w" or "-workingdirectory" or "-wd"
                        or "-configurationname" or "-outputformat" or "-of" or "-inputformat" or "-if" or "-settingsfile")
                        k++;
                    else if (!a.StartsWith('-'))
                        // Windows PowerShell runs its first plain argument as a command; pwsh runs it as a file.
                        return program == "powershell" ? Join(args.Skip(k)) : null;
                }
                return null;
            case "bash" or "sh" or "zsh" or "dash" or "ksh":
                var dashC = Flag(a => a.Length > 1 && a[0] == '-' && a[1..].All(char.IsAsciiLetter) && a.Contains('c'));
                return dashC >= 0 && dashC + 1 < args.Count ? args[dashC + 1] : null;
            case "start-process" or "saps" or "start":
                var file = Flag(a => a is "-filepath" or "-file" or "-path");
                var argList = Flag(a => a is "-argumentlist" or "-args" or "-arguments");
                var target = file >= 0 && file + 1 < args.Count ? args[file + 1]
                    : args.FirstOrDefault(a => a.Length > 0 && a[0] is not '-' and not '/');
                var rest = argList >= 0 && argList + 1 < args.Count ? args[argList + 1]
                    : target is null ? "" : Join(args.SkipWhile(a => a != target).Skip(1).Where(a => !a.StartsWith('-')));
                return target is null ? null : $"\"{target}\" {rest}";
            case "dotnet":
                var dll = args.FindIndex(a => !a.StartsWith('-') && a != "exec");
                if (dll < 0 || Path.GetFileName(args[dll].Replace('\\', '/')).ToLowerInvariant() is not ("cw.dll" or "cmdwarden.dll"))
                    return null;
                return "cw " + Join(args.Skip(dll + 1));
            default:
                return null;
        }
    }

    /// <summary>
    /// Words per command. A backslash is a plain character, as in PowerShell and in Windows paths,
    /// except <c>\"</c> inside double quotes and a bash line end <c>\</c>.
    /// </summary>
    private static List<List<string>> Segments(string command)
    {
        var segments = new List<List<string>> { new() };
        var word = new StringBuilder();
        var inWord = false;
        var quote = '\0';
        void EndWord()
        {
            if (inWord)
                segments[^1].Add(word.ToString());
            word.Clear();
            inWord = false;
        }
        void EndSegment()
        {
            EndWord();
            segments.Add([]);
        }

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            var next = i + 1 < command.Length ? command[i + 1] : '\0';
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                else if (c == '\\' && quote == '"' && next == '"')
                    word.Append(command[++i]);
                else
                    word.Append(c);
            }
            else if (c is '\'' or '"')
            {
                quote = c;
                inWord = true;
            }
            else if (c == '\\' && next is '\n' or '\r')
            {
                EndWord();
                i++;
            }
            else if (c is ';' or '|' or '(' or ')' or '\n' or '\r')
            {
                EndSegment();
            }
            else if (c == '&')
            {
                // A lone & before the program is the PowerShell call operator; && and a background & end the command.
                if (next == '&')
                {
                    i++;
                    EndSegment();
                }
                else if (!inWord && segments[^1].Count == 0)
                {
                    segments[^1].Add("&");
                }
                else
                {
                    EndSegment();
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                EndWord();
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }
        EndWord();
        return segments;
    }
}
