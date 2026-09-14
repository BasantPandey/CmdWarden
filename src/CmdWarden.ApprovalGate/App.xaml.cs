using System.IO;
using System.Windows;
using CmdWarden.Contracts;

namespace CmdWarden.ApprovalGate;

public partial class App : Application
{
    internal static bool UserChoseOutcome { get; set; }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var payload = LoadPayload(e.Args);
        if (payload is null)
        {
            // No window - exit immediately with Unavailable for the agent adapter.
            Environment.Exit(ApprovalHelperExitCodes.Unavailable);
            return;
        }

        var window = new MainWindow(payload);
        window.Show();
    }

    private static ApprovalHelperPayload? LoadPayload(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] is "--payload" or "-p") && i + 1 < args.Length)
                return ReadPayloadFile(args[i + 1].Trim('"'));

            if (args[i].StartsWith("--payload=", StringComparison.OrdinalIgnoreCase))
                return ReadPayloadFile(args[i]["--payload=".Length..].Trim('"'));
        }

        return null;
    }

    private static ApprovalHelperPayload? ReadPayloadFile(string path)
    {
        if (!File.Exists(path))
            return null;
        return ApprovalHelperJson.TryDeserialize(File.ReadAllText(path));
    }
}
