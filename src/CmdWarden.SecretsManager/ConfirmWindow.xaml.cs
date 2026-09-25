using System.Windows;

namespace CmdWarden.SecretsManager;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string body, string confirmLabel, bool isDanger)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        BodyText.Text = body;
        ConfirmLabel.Text = confirmLabel;
        ConfirmButton.ToolTip = confirmLabel + " (Enter)";
        if (isDanger)
            ConfirmButton.Style = (Style)FindResource("DangerPrimaryButtonStyle");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
