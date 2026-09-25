using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CmdWarden.Contracts;
using CmdWarden.Ui;

namespace CmdWarden.ApprovalGate;

public partial class MainWindow : Window
{
    // Keys typed in the terminal just before the popup opens must not approve it.
    private static readonly TimeSpan KeyArmDelay = TimeSpan.FromMilliseconds(600);
    private readonly Stopwatch _shownFor = new();
    private readonly ApprovalInputGuard _inputGuard = new();
    private RealInputHooks? _hooks;
    private bool _completed;

    public MainWindow(ApprovalHelperPayload payload)
    {
        InitializeComponent();
        Title = string.IsNullOrWhiteSpace(payload.WindowTitle) ? ProductInfo.Name : payload.WindowTitle;
        BrandTitle.Text = Title;

        LauncherName.Text = payload.LauncherDisplayName;
        Subtitle.Text = string.IsNullOrWhiteSpace(payload.Subtitle)
            ? ApprovalPresentation.SubtitleWantsToRun.ToUpperInvariant()
            : payload.Subtitle.ToUpperInvariant();

        IconGlyph.Text = string.IsNullOrWhiteSpace(payload.LauncherDisplayName)
            || payload.LauncherDisplayName == ApprovalPresentation.UnknownAppDisplayName
            ? "?"
            : char.ToUpperInvariant(payload.LauncherDisplayName.Trim()[0]).ToString();
        if (BrandImages.ForLauncher(payload.LauncherDisplayName, payload.LauncherPath) is { } launcherIcon)
        {
            LauncherIcon.Source = launcherIcon;
            LauncherIcon.Visibility = Visibility.Visible;
            IconGlyph.Visibility = Visibility.Collapsed;
        }

        if (BrandImages.ForTool(payload.Tool) is { } toolIcon)
        {
            ToolIcon.Source = toolIcon;
            ToolBadge.Visibility = Visibility.Visible;
            ToolBadge.ToolTip = payload.Tool;
        }

        CommandLine.Text = string.IsNullOrWhiteSpace(payload.CommandLine) ? payload.Tool : payload.CommandLine;
        ToolPath.Text = string.IsNullOrWhiteSpace(payload.ToolPath) ? "" : "\u2192 " + payload.ToolPath;
        ToolPath.Visibility = string.IsNullOrWhiteSpace(payload.ToolPath) ? Visibility.Collapsed : Visibility.Visible;
        WorkingDirectory.Text = string.IsNullOrWhiteSpace(payload.WorkingDirectory)
            ? "(unknown)"
            : payload.WorkingDirectory;
        SecretKeys.Text = payload.SecretNames is { Count: > 0 }
            ? string.Join(", ", payload.SecretNames)
            : "(none)";

        ReasonHeading.Text = payload.ReasonHeading;
        ReasonLine.Text = payload.ReasonLine;

        DetailEnrollment.Text = "Enrollment: " + payload.EnrollmentKind;
        DetailIdentity.Text = "Identity: " + payload.IdentityKind;
        DetailPublisher.Text = "Publisher: " + (payload.Publisher ?? "-");
        DetailLauncherPath.Text = "Launcher path: " + (payload.LauncherPath ?? "(unknown)");
        DetailPolicyLevel.Text = "Policy level: " + payload.PolicyLevel;
        DetailCommandClass.Text = "Command class: " + payload.CommandClass;
        DetailPolicyKey.Text = "Policy key: " + payload.PolicyKey;
        DetailTimestamp.Text = "Timestamp: " + payload.RequestedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        // #132/#205: a session state needs an enrolled launcher; Approve Once stays the default (Enter) button.
        if (payload.SessionAllowOffered)
        {
            SessionScope.Text = payload.SessionScopeLine ?? "";
        }
        else
        {
            SessionButton.Visibility = Visibility.Collapsed;
            SessionScope.Visibility = Visibility.Collapsed;
            SessionColumn.Width = new GridLength(0);
            SessionGap.Width = new GridLength(0);
        }

        IgnoredInput.Text = ApprovalInputGuard.IgnoredInputLine;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            try
            {
                _hooks = new RealInputHooks(_inputGuard, () => hwnd);
            }
            catch
            {
                // No hooks means no real input can be proven: Approve stays closed, Deny still works.
            }
        };
        Closed += (_, _) => _hooks?.Dispose();
        ContentRendered += (_, _) => _shownFor.Start();

        Closing += (_, _) =>
        {
            if (!_completed)
            {
                _completed = true;
                Environment.ExitCode = ApprovalHelperExitCodes.Unavailable;
            }
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            return;
        if (!_shownFor.IsRunning || _shownFor.Elapsed < KeyArmDelay)
        {
            _inputGuard.TryConsume(DateTime.UtcNow);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.None && SessionButton.IsVisible)
        {
            e.Handled = true;
            CompleteIfReal(ApprovalHelperExitCodes.AllowForSession);
        }
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e) =>
        CompleteIfReal(ApprovalHelperExitCodes.AllowOnce);

    private void DenyButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ApprovalHelperExitCodes.Deny);

    private void SessionButton_Click(object sender, RoutedEventArgs e) =>
        CompleteIfReal(ApprovalHelperExitCodes.AllowForSession);

    /// <summary>An approval needs real keyboard or mouse input (#23). Other input only shows a hint.</summary>
    private void CompleteIfReal(int code)
    {
        if (_inputGuard.TryConsume(DateTime.UtcNow))
        {
            Complete(code);
            return;
        }
        IgnoredInput.Visibility = Visibility.Visible;
    }

    private void Complete(int code)
    {
        if (_completed)
            return;
        _completed = true;
        App.UserChoseOutcome = true;
        Environment.Exit(code);
    }
}
