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

    /// <summary>Claude Code PreToolUse for Bash and PowerShell: a deny goes back as permissionDecision.</summary>
    public static async Task<string?> ClaudePreToolUseAsync(JsonNode input, Func<ToolCall, Task<PolicyVerdict>> check)
    {
        var denied = await DeniedAsync((string?)input["tool_input"]?["command"], (string?)input["cwd"], check).ConfigureAwait(false);
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

    private static async Task<List<string>> DeniedAsync(string? command, string? cwd, Func<ToolCall, Task<PolicyVerdict>> check)
    {
        var denied = new List<string>();
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
        foreach (var words in Segments(command))
        {
            var i = 0;
            while (i < words.Count && (words[i] is "&" or "command" or "exec" || IsAssignment(words[i])))
                i++;
            if (i >= words.Count)
                continue;
            var name = Path.GetFileNameWithoutExtension(words[i].Replace('\\', '/').Split('/')[^1]).ToLowerInvariant();
            if (ToolCatalog.Tools.Any(t => t.Id == name))
                calls.Add(new ToolCall(name, words.Skip(i + 1).ToList()));
        }
        return calls;
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
