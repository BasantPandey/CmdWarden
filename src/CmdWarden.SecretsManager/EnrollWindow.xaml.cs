using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CmdWarden.Contracts;
using CmdWarden.Ui;

namespace CmdWarden.SecretsManager;

/// <summary>#44: enroll a launcher by key, or pick one the audit saw. Enroll is the confirmation.</summary>
public partial class EnrollWindow : Window
{
    private sealed record SeenRow(string PolicyKey, string Name, string Detail, string? Path, ImageSource? Icon);

    private readonly IReadOnlyList<SeenRow> _seen;
    private readonly PolicyLevel _harnessDefault;
    private readonly PolicyLevel _terminalDefault;

    public EnrollWindow(IReadOnlyList<SeenLauncher> seen, PolicyLevel harnessDefault, PolicyLevel terminalDefault)
    {
        InitializeComponent();
        _harnessDefault = harnessDefault;
        _terminalDefault = terminalDefault;
        _seen = seen.Select(s => new SeenRow(
            s.PolicyKey,
            s.Path is { } p ? Path.GetFileName(p) : s.PolicyKey,
            (s.Path ?? s.PolicyKey) + (s.LastSeen is { } t ? "  ·  last seen " + t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : ""),
            s.Path,
            BrandImages.ForLauncher(null, s.Path))).ToList();
        SeenList.ItemsSource = _seen;
        if (_seen.Count == 0)
        {
            SeenHeading.Visibility = Visibility.Collapsed;
            SeenFrame.Visibility = Visibility.Collapsed;
        }
        Kind_Checked(this, new RoutedEventArgs());
        Loaded += (_, _) => KeyBox.Focus();
    }

    public string PolicyKey => KeyBox.Text.Trim();

    public LauncherEnrollmentKind Kind =>
        KindTerminal.IsChecked == true ? LauncherEnrollmentKind.Terminal : LauncherEnrollmentKind.AiHarness;

    /// <summary>The path the audit saw for the chosen key, shown on the launcher card.</summary>
    public string? DisplayPath =>
        _seen.FirstOrDefault(s => string.Equals(s.PolicyKey, PolicyKey, StringComparison.OrdinalIgnoreCase))?.Path;

    private void Seen_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key })
            KeyBox.Text = key;
    }

    private void Key_Changed(object sender, TextChangedEventArgs e) =>
        EnrollButton.IsEnabled = PolicyKey.Length > 0
            && !PolicyKey.Equals(LauncherKinds.PolicyKeyUnknown, StringComparison.OrdinalIgnoreCase);

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (KindText is null)
            return;
        var level = Kind == LauncherEnrollmentKind.Terminal ? _terminalDefault : _harnessDefault;
        KindText.Text = $"Default level {PolicyLevelNames.Format(level)}: {PolicyLevelText.Matrix(level)}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Enroll_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
