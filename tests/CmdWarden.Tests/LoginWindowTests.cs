using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Grpc;
using CmdWarden.SecretsManager;

namespace CmdWarden.Tests;

public class LoginWindowTests
{
    [Fact]
    public async Task The_list_shows_the_name_the_user_and_the_sites()
    {
        var password = "THE-PASSWORD";
        var view = LoginListView.From(new[]
        {
            new LoginInfo { Name = "GITHUB", Account = "ada", Sites = "github.com" },
            new LoginInfo { Name = "MAIL", Account = "ada", Sites = "mail.example.com" },
        });
        var row = Assert.Single(view.Rows, item => item.Name == "GITHUB");
        Assert.Equal("ada", row.Account);
        Assert.Equal("github.com", row.Sites);
        Assert.Equal(2, view.Rows.Count);
        Assert.Equal("2 logins", view.CountText);
        Assert.False(view.ShowEmpty);
        Assert.True(view.ShowRows);
        Assert.DoesNotContain(password, $"{row.Name} {row.Account} {row.Sites}", StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Password",
            typeof(LoginListItem).GetProperties().Select(property => property.Name));

        await StaAsync(async () =>
        {
            var page = Bind();
            page.Page.Show(view);
            Assert.Equal("ada", page.Page.Rows[0].Account);
            Assert.Equal("github.com", page.Page.Rows[0].Sites);
            Assert.DoesNotContain(password, page.Page.Rows[0].Account, StringComparison.Ordinal);
            Assert.Equal(Visibility.Collapsed, page.EmptyPanel.Visibility);
            Assert.Equal(Visibility.Visible, page.RowsPanel.Visibility);
            Assert.Empty(page.List.Items.OfType<Button>());
            await page.Page.RefreshAsync(
                () => Task.FromResult(true),
                () => Task.FromResult<IReadOnlyList<LoginInfo>>(new[] { new LoginInfo { Name = "GITHUB", Account = "ada", Sites = "github.com" } }));
            Assert.Equal("1 login", page.Count.Text);
            Assert.Equal("ada", page.Page.Rows[0].Account);
        });
    }

    [Fact]
    public async Task An_empty_list_says_that_no_login_is_stored()
    {
        var view = LoginListView.From(Array.Empty<LoginInfo>());
        Assert.Equal(LoginListView.EmptyText, view.Message);
        Assert.Equal("No login is stored.", view.Message);
        Assert.True(view.ShowEmpty);
        Assert.False(view.ShowRows);
        Assert.Empty(view.Rows);

        await StaAsync(() =>
        {
            var page = Bind();
            page.Page.Show(view);
            Assert.Equal("No login is stored.", page.EmptyText.Text);
            Assert.Equal(Visibility.Visible, page.EmptyPanel.Visibility);
            Assert.Empty(page.Page.Rows);
            return Task.CompletedTask;
        });

        var down = await LoginListView.LoadAsync(
            () => Task.FromResult(false),
            () => Task.FromResult<IReadOnlyList<LoginInfo>>(new[] { new LoginInfo { Name = "GITHUB", Account = "ada", Sites = "github.com" } }));
        Assert.Equal(LoginListView.OfflineText, down.Message);
        Assert.Empty(down.Rows);

        var failed = await LoginListView.LoadAsync(
            () => throw new InvalidOperationException("down"),
            () => Task.FromResult<IReadOnlyList<LoginInfo>>(Array.Empty<LoginInfo>()));
        Assert.Empty(failed.Rows);

        var xaml = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "CmdWarden.SecretsManager", "MainWindow.xaml"));
        var start = xaml.IndexOf("x:Name=\"PageLogins\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("Secret Gates", start, StringComparison.Ordinal);
        var tab = xaml[start..end];
        Assert.Contains("No login is stored.", tab, StringComparison.Ordinal);
        Assert.Contains("{Binding Name}", tab, StringComparison.Ordinal);
        Assert.Contains("{Binding Account}", tab, StringComparison.Ordinal);
        Assert.Contains("{Binding Sites}", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("Reveal", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Value}", tab, StringComparison.Ordinal);
    }

    private static Bound Bind()
    {
        var list = new ItemsControl();
        var empty = new TextBlock();
        var emptyPanel = new StackPanel();
        var rows = new StackPanel();
        var count = new TextBlock();
        var page = new LoginPage();
        page.Attach(new LoginPage.Controls(list, empty, emptyPanel, rows, count));
        return new Bound(page, list, empty, emptyPanel, rows, count);
    }

    private static Task StaAsync(Func<Task> action)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private sealed record Bound(
        LoginPage Page,
        ItemsControl List,
        TextBlock EmptyText,
        UIElement EmptyPanel,
        UIElement RowsPanel,
        TextBlock Count);
}
