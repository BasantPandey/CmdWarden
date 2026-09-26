using System.Windows;
using System.Windows.Controls;
using CmdWarden.Contracts;

namespace CmdWarden.SecretsManager;

/// <summary>#44: pick a level for one tool on one launcher. Set level is the confirmation.</summary>
public partial class SetLevelWindow : Window
{
    private readonly PolicyLevel _current;

    public SetLevelWindow(string policyKey, string toolName, PolicyLevel current)
    {
        InitializeComponent();
        _current = current;
        TitleText.Text = $"Set the {toolName} level";
        LauncherText.Text = policyKey;
        Button(current).IsChecked = true;
    }

    public PolicyLevel Chosen { get; private set; }

    private RadioButton Button(PolicyLevel level) => level switch
    {
        PolicyLevel.Read => LevelRead,
        PolicyLevel.Trusted => LevelTrusted,
        PolicyLevel.Full => LevelFull,
        _ => LevelDeny,
    };

    private void Level_Checked(object sender, RoutedEventArgs e)
    {
        Chosen = sender == LevelRead ? PolicyLevel.Read
            : sender == LevelTrusted ? PolicyLevel.Trusted
            : sender == LevelFull ? PolicyLevel.Full
            : PolicyLevel.Deny;
        MatrixText.Text = PolicyLevelText.Matrix(Chosen);
        FullWarning.Visibility = Chosen == PolicyLevel.Full ? Visibility.Visible : Visibility.Collapsed;
        SetButton.IsEnabled = Chosen != _current;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Set_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

internal static class PolicyLevelText
{
    /// <summary>Class matrix for one level, derived from the evaluator so the text cannot drift from enforcement.</summary>
    public static string Matrix(PolicyLevel level)
    {
        var all = new[] { CommandClass.Read, CommandClass.Write, CommandClass.SecretReveal, CommandClass.Unknown };
        var allowed = all.Where(c => PolicyEvaluator.IsAutoAllowed(level, c)).Select(CommandClassNames.Format).ToList();
        var gated = all.Where(c => !PolicyEvaluator.IsAutoAllowed(level, c)).Select(CommandClassNames.Format).ToList();
        var auto = allowed.Count == 0 ? "auto-allow: none" : "auto-allow: " + string.Join(", ", allowed);
        return gated.Count == 0 ? auto : auto + " / Approval Gate: " + string.Join(", ", gated);
    }
}
