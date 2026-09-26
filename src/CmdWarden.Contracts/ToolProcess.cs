using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CmdWarden.Contracts;

/// <summary>
/// Start the real tool of a shim. A .cmd or .bat tool runs through cmd.exe, and cmd.exe reads
/// &amp; | &lt; &gt; and %VAR% in the arguments. Without care, "npm view x&amp;echo %NPM_TOKEN%" runs a
/// second command that sees the secret the shim put in the env. Each argument goes in quotes, and
/// an argument that cmd.exe cannot hold in quotes stops the run.
/// </summary>
public static partial class ToolProcess
{
    public static bool IsBatch(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".cmd" or ".bat";

    /// <summary>Throws <see cref="ArgumentException"/> for an argument that a batch file cannot get safely.</summary>
    public static ProcessStartInfo StartInfo(string absolutePath, IReadOnlyList<string> arguments)
    {
        var psi = new ProcessStartInfo { UseShellExecute = false };
        if (!IsBatch(absolutePath))
        {
            psi.FileName = absolutePath;
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);
            return psi;
        }

        foreach (var arg in arguments)
        {
            if (arg.IndexOfAny(['"', '\r', '\n', '\0']) >= 0 || EnvReference().IsMatch(arg))
                throw new ArgumentException(
                    $"The argument {arg} has a quote, a line break or a %NAME% pair. {Path.GetFileName(absolutePath)} is a batch file, so cmd.exe would read it as code.");
        }
        // /s: cmd.exe removes only the outer quotes. /d: no AutoRun. /v:off: ! is plain text.
        psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        psi.Arguments = "/d /v:off /s /c \"" + string.Join(' ', new[] { absolutePath }.Concat(arguments).Select(a => "\"" + a + "\"")) + "\"";
        return psi;
    }

    [GeneratedRegex("%[^%]+%")]
    private static partial Regex EnvReference();
}
