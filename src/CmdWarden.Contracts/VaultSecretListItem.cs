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

    /// <summary>Move the selection by <paramref name="step"/> rows and stop at the ends. No selection starts at an end.</summary>
    public static VaultSecretListItem? Move(IList<VaultSecretListItem> items, int step)
    {
        if (items.Count == 0)
            return null;
        var current = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsSelected)
                current = i;
        }
        var next = current < 0
            ? (step > 0 ? 0 : items.Count - 1)
            : Math.Clamp(current + step, 0, items.Count - 1);
        SelectOnly(items, items[next]);
        return items[next];
    }

    public static void Clear(IEnumerable<VaultSecretListItem> items)
    {
        foreach (var item in items)
            item.IsSelected = false;
    }
}
