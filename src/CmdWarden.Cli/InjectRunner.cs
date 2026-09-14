using System.Diagnostics;
using System.Text;

namespace CmdWarden.Cli;

/// <summary>
/// Spawns a child process with secrets only in the child environment.
/// </summary>
public static class InjectRunner
{
    public static int Run(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            // Do not redirect — user sees child I/O; secret stays out of our streams.
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in env)
            psi.Environment[key] = value;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Parse: inject +NAME [+NAME2 ...] -- command [args...]
    /// </summary>
    public static (List<string> SecretNames, string FileName, List<string> Arguments) ParseInjectArgs(
        string[] argsAfterInject)
    {
        var names = new List<string>();
        var i = 0;
        for (; i < argsAfterInject.Length; i++)
        {
            var a = argsAfterInject[i];
            if (a == "--")
            {
                i++;
                break;
            }

            if (a.StartsWith('+') && a.Length > 1)
            {
                names.Add(a[1..]);
                continue;
            }

            throw new ArgumentException(
                "Expected +SECRET_NAME arguments before --. Example: cw inject +TOKEN -- cmd /c echo %TOKEN%");
        }

        if (names.Count == 0)
            throw new ArgumentException("At least one +SECRET_NAME is required.");

        if (i >= argsAfterInject.Length)
            throw new ArgumentException("Missing command after --.");

        var fileName = argsAfterInject[i++];
        var arguments = argsAfterInject.Skip(i).ToList();
        return (names, fileName, arguments);
    }

    public static string DecodeUtf8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}
