using System.Diagnostics;

// cw try (#65): a stand-in for an AI agent. It starts the command in its arguments, so the Session
// Agent sees this binary as the launcher: an app that nobody enrolled.
if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: cw-try-agent <program> [args...]");
    return 2;
}

var psi = new ProcessStartInfo(args[0]) { UseShellExecute = false };
foreach (var arg in args.Skip(1))
    psi.ArgumentList.Add(arg);
using var child = Process.Start(psi)!;
child.WaitForExit();
return child.ExitCode;
