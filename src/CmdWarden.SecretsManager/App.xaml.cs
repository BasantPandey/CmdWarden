using System.Runtime.InteropServices;
using System.Windows;
using CmdWarden.Contracts;

namespace CmdWarden.SecretsManager;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // No failure in this shell may crash the process (spec's Fail modes table) -- RefreshAsync
        // already guards the known agent-connectivity risk; this is the last-resort backstop for
        // anything unforeseen on the UI thread.
        DispatcherUnhandledException += (_, ex) => ex.Handled = true;

        var mutexName = $"CmdWardenVault-{AgentEndpoints.Sanitize(Environment.UserName)}";
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out var createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private static void ActivateExistingInstance()
    {
        var hwnd = FindWindow(null, global::CmdWarden.SecretsManager.MainWindow.WindowTitle);
        if (hwnd == IntPtr.Zero)
            return;

        ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
