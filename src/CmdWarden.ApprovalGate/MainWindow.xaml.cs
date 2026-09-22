using System.Windows;
using CmdWarden.Contracts;
using CmdWarden.Ui;

namespace CmdWarden.ApprovalGate;

public partial class MainWindow : Window
{
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

        Closing += (_, _) =>
        {
            if (!_completed)
            {
                _completed = true;
                Environment.ExitCode = ApprovalHelperExitCodes.Unavailable;
            }
        };
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ApprovalHelperExitCodes.AllowOnce);

    private void DenyButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ApprovalHelperExitCodes.Deny);

    private void SessionButton_Click(object sender, RoutedEventArgs e) =>
        Complete(ApprovalHelperExitCodes.AllowForSession);

    private void Complete(int code)
    {
        if (_completed)
            return;
        _completed = true;
        App.UserChoseOutcome = true;
        Environment.Exit(code);
    }
}
