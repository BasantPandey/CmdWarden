using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private readonly ApprovalHelperPayload _payload;
    private bool _completed;
    private bool _helloPending;

    public MainWindow(ApprovalHelperPayload payload)
    {
        InitializeComponent();
        _payload = payload;
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
        // #35: the card starts with what the command does. High risk is red.
        if (!string.IsNullOrWhiteSpace(payload.Impact))
        {
            ImpactText.Text = payload.Impact;
            ImpactBanner.Visibility = Visibility.Visible;
            if (payload.ImpactHigh)
            {
                ImpactBanner.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x1D, 0x20));
                ImpactBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0x6B, 0x6B));
                ImpactGlyph.Text = "";
                ImpactGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
            }
        }

        // #32: the Agent cleans the reason; the card cleans it again because the payload file is input too.
        if (AgentReason.Clean(payload.AgentReason) is { } reason)
        {
            AgentReasonLabel.Text = AgentReason.Label + " ";
            AgentReasonText.Text = "“" + reason + "”";
            AgentReasonLine.Visibility = Visibility.Visible;
        }
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
        if (_helloPending)
            return;
        if (!_inputGuard.TryConsume(DateTime.UtcNow))
        {
            IgnoredInput.Visibility = Visibility.Visible;
            return;
        }
        if (_payload.HelloRequired)
        {
            _ = ConfirmWithHelloAsync(code);
            return;
        }
        Complete(code);
    }

    /// <summary>#24: after a real Approve, Windows Hello proves the person. Cancel gives Deny.</summary>
    private async Task ConfirmWithHelloAsync(int code)
    {
        _helloPending = true;
        ApproveButton.IsEnabled = false;
        SessionButton.IsEnabled = false;
        IgnoredInput.Visibility = Visibility.Collapsed;
        HelloStatus.Visibility = Visibility.Visible;
        var what = string.IsNullOrWhiteSpace(_payload.CommandLine) ? _payload.Tool : _payload.CommandLine;
        var check = await WindowsHello.VerifyAsync(new WindowInteropHelper(this).Handle, $"{ProductInfo.Name}: approve {what}");
        Complete(check == HelloCheck.Canceled
            ? ApprovalHelperExitCodes.FromAnswer(ApprovalHelperExitCodes.Deny, check)
            : ApprovalHelperExitCodes.FromAnswer(code, check));
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
