using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>
/// One login row. The password is not a field.
/// </summary>
public sealed class LoginListItem
{
    public LoginListItem(string name, string account, string sites)
    {
        Name = name;
        Account = account;
        Sites = sites;
    }

    public string Name { get; }

    public string Account { get; }

    public string Sites { get; }
}

/// <summary>
/// The Logins tab state. It comes from <c>ListLogins</c> and never carries a password.
/// </summary>
public sealed record LoginListView(
    IReadOnlyList<LoginListItem> Rows,
    string Message,
    string CountText,
    bool ShowEmpty,
    bool ShowRows)
{
    public const string EmptyText = "No login is stored.";

    public const string OfflineText = "Session Agent is not running.";

    public static LoginListView Offline { get; } = new([], OfflineText, "0 logins", true, false);

    public static LoginListView From(IEnumerable<LoginInfo> logins)
    {
        var rows = logins.Select(login => new LoginListItem(login.Name, login.Account, login.Sites)).ToArray();
        var count = rows.Length == 1 ? "1 login" : $"{rows.Length} logins";
        var empty = rows.Length == 0;
        return new LoginListView(rows, empty ? EmptyText : "", count, empty, !empty);
    }

    public static async Task<LoginListView> LoadAsync(
        Func<Task<bool>> isUp,
        Func<Task<IReadOnlyList<LoginInfo>>> list)
    {
        try
        {
            if (!await isUp().ConfigureAwait(false))
                return Offline;
            return From(await list().ConfigureAwait(false));
        }
        catch (Exception)
        {
            return Offline;
        }
    }
}
