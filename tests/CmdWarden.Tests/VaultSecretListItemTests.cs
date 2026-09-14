using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class VaultSecretListItemTests
{
    [Fact]
    public void CanDelete_is_false_until_selected()
    {
        var item = new VaultSecretListItem("GH_TOKEN");
        Assert.False(item.IsSelected);
        Assert.False(item.CanDelete);

        item.IsSelected = true;
        Assert.True(item.CanDelete);

        item.IsSelected = false;
        Assert.False(item.CanDelete);
    }

    [Fact]
    public void SelectOnly_arms_delete_on_one_card()
    {
        var a = new VaultSecretListItem("A");
        var b = new VaultSecretListItem("B");
        var c = new VaultSecretListItem("C");
        var items = new[] { a, b, c };

        VaultSecretListSelection.SelectOnly(items, b);

        Assert.False(a.IsSelected);
        Assert.False(a.CanDelete);
        Assert.True(b.IsSelected);
        Assert.True(b.CanDelete);
        Assert.False(c.IsSelected);
        Assert.False(c.CanDelete);

        VaultSecretListSelection.SelectOnly(items, a);
        Assert.True(a.CanDelete);
        Assert.False(b.CanDelete);
    }

    [Fact]
    public void Selecting_raises_CanDelete_property_change()
    {
        var item = new VaultSecretListItem("X");
        var changed = new List<string>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        item.IsSelected = true;

        Assert.Contains(nameof(VaultSecretListItem.IsSelected), changed);
        Assert.Contains(nameof(VaultSecretListItem.CanDelete), changed);
    }
}
