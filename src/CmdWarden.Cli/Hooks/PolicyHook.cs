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
        "proxy remove", "proxy strict", "proxy uninstall", "github app remove",
    ];

    public const string SelfProtectText = "This changes CmdWarden protection. Ask the user to run it in their own terminal.";

    /// <summary>The cw commands in a command line that <see cref="WeakeningCommands"/> names.</summary>
    public static IReadOnlyList<string> WeakeningCwCalls(string command)
    {
        var found = new List<string>();
        foreach (var words in Segments(command))
        {
            var i = ProgramIndex(words);
            if (i >= words.Count || ProgramName(words[i]) is not ("cw" or "cmdwarden"))
                continue;
            var args = string.Join(' ', words.Skip(i + 1).Select(w => w.ToLowerInvariant()));
            if (WeakeningCommands.FirstOrDefault(c => args == c || args.StartsWith(c + " ", StringComparison.Ordinal)) is { } hit)
                found.Add("cw " + hit);
        }
        return found;
    }

    private static async Task<List<string>> DeniedAsync(string? command, string? cwd, Func<ToolCall, Task<PolicyVerdict>> check)
    {
        var denied = EnvFileDenials(EnvFileReads(command ?? ""), cwd);
        denied.AddRange(WeakeningCwCalls(command ?? "").Select(c => $"{c}: {SelfProtectText}"));
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
        foreach (var words in Segments(command))
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
        foreach (var words in Segments(command))
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
