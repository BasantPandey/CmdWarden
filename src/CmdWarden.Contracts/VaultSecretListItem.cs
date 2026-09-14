using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CmdWarden.Contracts;

/// <summary>
/// Name-only vault inventory row with selection-gated delete (Variant B / #95).
/// </summary>
public sealed class VaultSecretListItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public VaultSecretListItem(string name) => Name = name;

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDelete));
        }
    }

    /// <summary>Delete is armed only after the card is selected (not hover alone).</summary>
    public bool CanDelete => IsSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Single-selection helper for the vault list.</summary>
public static class VaultSecretListSelection
{
    public static void SelectOnly(IEnumerable<VaultSecretListItem> items, VaultSecretListItem selected)
    {
        foreach (var item in items)
            item.IsSelected = ReferenceEquals(item, selected);
    }
}
