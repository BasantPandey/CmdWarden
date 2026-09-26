using System.Runtime.InteropServices;
using System.Windows;
using CmdWarden.Contracts;

namespace CmdWarden.SecretsManager;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private TrayIcon? _tray;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // No failure in this shell may crash the process (spec's Fail modes table) -- RefreshAsync
        // already guards the known agent-connectivity risk; this is the last-resort backstop for
        // anything unforeseen on the UI thread.
        DispatcherUnhandledException += (_, ex) => ex.Handled = true;

        if (e.Args.Contains(TrayIcon.Argument, StringComparer.OrdinalIgnoreCase))
        {
            StartTray();
            return;
        }

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

    /// <summary>#43: one tray icon per user, with no window. It runs until Exit tray icon.</summary>
    private void StartTray()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true,
            name: $"CmdWardenTray-{AgentEndpoints.Sanitize(Environment.UserName)}", createdNew: out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _tray = new TrayIcon();
        Exit += (_, _) => _tray.Dispose();
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
