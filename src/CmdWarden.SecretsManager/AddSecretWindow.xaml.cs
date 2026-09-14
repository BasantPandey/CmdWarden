using System.Security.Cryptography;
using System.Windows;
using CmdWarden.Contracts;

namespace CmdWarden.SecretsManager;

public partial class AddSecretWindow : Window
{
    private readonly HashSet<string> _existingNames;

    public string? SavedName { get; private set; }

    public AddSecretWindow(IEnumerable<string> existingNames)
    {
        InitializeComponent();
        _existingNames = existingNames
            .Select(VaultSecretFormValidation.NormalizeName)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        NameBox.Focus();
    }

    private void Fields_Changed(object sender, RoutedEventArgs e) => UpdateValidation(showErrors: false);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var result = VaultSecretFormValidation.Validate(NameBox.Text, ValueBox.Password);
        if (!result.Ok || result.ValueBytes is null)
        {
            ShowErrors(result.NameError, result.ValueError);
            return;
        }

        var valueBytes = result.ValueBytes;
        var name = result.NormalizedName;

        if (VaultSecretFormValidation.RequiresReplaceConfirm(name, _existingNames))
        {
            var replace = new ConfirmWindow(
                title: "Secret already exists",
                body: $"A secret named {name} already exists. Replace it? Values are never shown.",
                confirmLabel: "Replace",
                isDanger: false)
            {
                Owner = this,
            };
            if (replace.ShowDialog() != true)
            {
                CryptographicOperations.ZeroMemory(valueBytes);
                return;
            }
        }

        SaveButton.IsEnabled = false;
        try
        {
            await AgentVaultClient.SaveAsync(name, valueBytes).ConfigureAwait(true);
            SavedName = name;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ValueError.Text = "Save failed: " + VaultSecretFormValidation.SanitizeError(ex.Message);
            ValueError.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(valueBytes);
        }
    }

    private void UpdateValidation(bool showErrors)
    {
        var result = VaultSecretFormValidation.Validate(NameBox.Text, ValueBox.Password);
        if (result.ValueBytes is not null)
            CryptographicOperations.ZeroMemory(result.ValueBytes);

        if (showErrors)
            ShowErrors(result.NameError, result.ValueError);
        else
        {
            NameError.Visibility = Visibility.Collapsed;
            ValueError.Visibility = Visibility.Collapsed;
        }

        SaveButton.IsEnabled = result.Ok;
    }

    private void ShowErrors(string? nameErr, string? valueErr)
    {
        NameError.Text = nameErr ?? "";
        NameError.Visibility = string.IsNullOrEmpty(nameErr) ? Visibility.Collapsed : Visibility.Visible;
        ValueError.Text = valueErr ?? "";
        ValueError.Visibility = string.IsNullOrEmpty(valueErr) ? Visibility.Collapsed : Visibility.Visible;
    }
}
