using System.Management.Automation.Language;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CmdWarden.Agent.Identity;

/// <summary>
/// Looks inside a PowerShell wrapper before a gated tool runs (#31). An agent can hide gh behind
/// <c>-EncodedCommand</c>, <c>Invoke-Expression</c>, or a script block built from a string. The
/// PowerShell parser reads the code; a part it cannot read is opaque, and an opaque part forces the
/// Approval Gate.
/// </summary>
public static class PowerShellInspector
{
    public static bool IsPowerShell(string? fileName) =>
        fileName is not null && Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant() is "pwsh" or "powershell";

    /// <summary>
    /// Why this PowerShell command line holds code CmdWarden cannot read, or null when every part is
    /// readable. <paramref name="readFile"/> reads a <c>-File</c> script; null content counts as opaque.
    /// </summary>
    public static string? FindOpaquePart(string exeName, IReadOnlyList<string> args, Func<string, string?>? readFile = null)
    {
        readFile ??= TryRead;
        var windowsPowerShell = Path.GetFileNameWithoutExtension(exeName).Equals("powershell", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-') && !arg.StartsWith('/'))
            {
                // A bare first argument: pwsh runs it as a file, Windows PowerShell as a command.
                return windowsPowerShell ? FindOpaqueInScript(string.Join(' ', args.Skip(i))) : FindOpaqueInFile(arg, readFile);
            }

            var name = arg.TrimStart('-', '/').ToLowerInvariant();
            var colon = name.IndexOf(':');
            if (colon >= 0)
                name = name[..colon];
            if (name.Length == 0)
                continue;
            if (Matches(name, "encodedcommand", "e", "ec", "en", "enc") || Matches(name, "encodedarguments", "ea", "encodeda"))
                return "the command is encoded (-EncodedCommand)";
            if (Matches(name, "command", "c"))
            {
                var script = string.Join(' ', args.Skip(i + 1));
                return script.Trim() == "-" ? "the command comes from standard input" : FindOpaqueInScript(script);
            }
            if (Matches(name, "file", "f"))
                return i + 1 < args.Count ? FindOpaqueInFile(args[i + 1], readFile) : null;
            // Parameters with a value: skip the value.
            if (Matches(name, "executionpolicy", "ex", "ep") || Matches(name, "workingdirectory", "wd", "wo")
                || Matches(name, "configurationname", "config") || Matches(name, "custompipename")
                || Matches(name, "windowstyle", "w", "win") || Matches(name, "outputformat", "o", "of")
                || Matches(name, "inputformat", "if", "in") || Matches(name, "settingsfile", "settings")
                || Matches(name, "psconsolefile") || Matches(name, "version", "v"))
            {
                i++;
            }
        }
        return null;
    }

    /// <summary>Why the script holds a part CmdWarden cannot read, or null.</summary>
    public static string? FindOpaqueInScript(string script)
    {
        var ast = Parser.ParseInput(script, out _, out var errors);
        if (errors.Length > 0)
            return "the command does not parse";

        foreach (var node in ast.FindAll(_ => true, searchNestedScriptBlocks: true))
        {
            switch (node)
            {
                case CommandAst command when command.GetCommandName() is { } name
                    && name.ToLowerInvariant() is "invoke-expression" or "iex":
                    return "it runs Invoke-Expression";
                case CommandAst command when command.GetCommandName() is { } name
                    && IsPowerShell(name) && FindOpaquePart(name, command.CommandElements.Skip(1).Select(e => e.Extent.Text.Trim('\'', '"')).ToList()) is { } nested:
                    return "a nested PowerShell: " + nested;
                case CommandAst command when command.InvocationOperator is TokenKind.Ampersand or TokenKind.Dot
                    && command.CommandElements[0] is not (StringConstantExpressionAst or ScriptBlockExpressionAst):
                    return "it calls a command whose name is built at run time";
                case InvokeMemberExpressionAst member when member.Member is StringConstantExpressionAst m
                    && m.Value.ToLowerInvariant() is "create" && member.Expression is TypeExpressionAst type
                    && type.TypeName.FullName.ToLowerInvariant() is "scriptblock" or "system.management.automation.scriptblock":
                    return "it builds a script block from a string";
                case InvokeMemberExpressionAst member when member.Member is StringConstantExpressionAst m
                    && m.Value.ToLowerInvariant() is "invokescript" or "newscriptblock" or "expandstring":
                    return "it runs code from a string";
                case InvokeMemberExpressionAst member when member.Member is not StringConstantExpressionAst:
                    return "it calls a method whose name is built at run time";
            }
        }
        return null;
    }

    private static string? FindOpaqueInFile(string path, Func<string, string?> readFile) =>
        readFile(path) is { } content
            ? FindOpaqueInScript(content) is { } why ? $"the script {Path.GetFileName(path)}: {why}" : null
            : $"the script {Path.GetFileName(path)} cannot be read";

    private static string? TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>PowerShell takes any unique prefix of a parameter name, plus a few fixed aliases.</summary>
    private static bool Matches(string given, string full, params string[] aliases) =>
        aliases.Contains(given) || (given.Length >= 3 && full.StartsWith(given, StringComparison.Ordinal));

    /// <summary>Split a Windows command line the way the C runtime does.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero)
            return [];
        try
        {
            var args = new string[count];
            for (var i = 0; i < count; i++)
                args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            return args;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    /// <summary>The command line of a live process, or null when it is gone or not readable.</summary>
    [SupportedOSPlatform("windows")]
    public static string? ReadCommandLine(int pid)
    {
        const int ProcessCommandLineInformation = 60;
        const uint QueryLimitedInformation = 0x1000;
        var process = OpenProcess(QueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var size = 0;
            _ = NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, ref size);
            if (size <= 0)
                return null;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, size, ref size) != 0)
                    return null;
                // UNICODE_STRING: Length (bytes), MaximumLength, then the buffer pointer.
                var length = (ushort)Marshal.ReadInt16(buffer);
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                return Marshal.PtrToStringUni(text, length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, ref int returnLength);
}
