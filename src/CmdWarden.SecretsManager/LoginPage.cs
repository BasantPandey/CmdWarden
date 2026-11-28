using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;

namespace CmdWarden.SecretsManager;

/// <summary>
/// Binds the Logins tab to <see cref="LoginListView"/>. The tab has no reveal control.
/// </summary>
public sealed class LoginPage
{
    private TextBlock? _empty;
    private UIElement? _emptyPanel;
    private UIElement? _rowPanel;
    private TextBlock? _count;

    public ObservableCollection<LoginListItem> Rows { get; } = [];

    public readonly record struct Controls(
        ItemsControl List,
        TextBlock Empty,
        UIElement EmptyPanel,
        UIElement RowPanel,
        TextBlock Count);

    public void Attach(Controls controls)
    {
        controls.List.ItemsSource = Rows;
        _empty = controls.Empty;
        _emptyPanel = controls.EmptyPanel;
        _rowPanel = controls.RowPanel;
        _count = controls.Count;
    }

    public void Show(LoginListView view)
    {
        Rows.Clear();
        foreach (var row in view.Rows)
            Rows.Add(row);
        if (_empty is not null)
            _empty.Text = view.Message;
        if (_emptyPanel is not null)
            _emptyPanel.Visibility = view.ShowEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (_rowPanel is not null)
            _rowPanel.Visibility = view.ShowRows ? Visibility.Visible : Visibility.Collapsed;
        if (_count is not null)
            _count.Text = view.CountText;
    }

    public Task RefreshAsync() =>
        RefreshAsync(AgentUp, LoadList);

    public async Task RefreshAsync(Func<Task<bool>> isUp, Func<Task<IReadOnlyList<LoginInfo>>> list)
    {
        try
        {
            var up = await isUp().ConfigureAwait(true);
            Show(up ? LoginListView.From(await list().ConfigureAwait(true)) : LoginListView.Offline);
        }
        catch (Exception)
        {
            Show(LoginListView.Offline);
        }
    }

    private static async Task<bool> AgentUp() =>
        (await AgentLifecycle.StatusAsync().ConfigureAwait(true)).Up;

    private static async Task<IReadOnlyList<LoginInfo>> LoadList() =>
        await AgentVaultClient.ListLoginsAsync().ConfigureAwait(true);
}
