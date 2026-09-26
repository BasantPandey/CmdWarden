using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CmdWarden.Contracts;
using CmdWarden.Contracts.GitHub;

namespace CmdWarden.Cli;

/// <summary>
/// cw github app setup|status|remove (#40). Setup registers a GitHub App with the manifest flow,
/// or imports an app you made, and keeps its private key in the vault.
/// </summary>
public static class GitHubAppCommands
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<int> RunAsync(string[] args)
    {
        var sub = args is ["app", var s, ..] ? s : null;
        var rest = args.Length > 2 ? args[2..] : [];
        try
        {
            return sub switch
            {
                "setup" => await SetupAsync(rest).ConfigureAwait(false),
                "status" => await StatusAsync().ConfigureAwait(false),
                "remove" => Remove(),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or InvalidDataException
                                   or IOException or CryptographicException or ArgumentException or TaskCanceledException)
        {
            Console.Error.WriteLine($"cw github app {sub}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("Usage: cw github app <setup|status|remove>");
        Console.WriteLine("  setup                         Register a new GitHub App in your browser (manifest flow)");
        Console.WriteLine("  setup --org <org>             Register it for an organization");
        Console.WriteLine("  setup --app-id <id> --key <pem>  Use an app you made; the key goes to the vault");
        Console.WriteLine("  status                        Show the app, and try a token for the repo of this folder");
        Console.WriteLine("  remove                        Delete the app key from the vault and stop using the app");
        Console.WriteLine("On an allowed gh run in a github.com repo, the child gets a token for that repo only. It ends after one hour.");
        return 1;
    }

    private static async Task<int> SetupAsync(string[] args)
    {
        string? org = null, keyFile = null;
        long appId = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--org" && i + 1 < args.Length)
                org = args[++i];
            else if (args[i] is "--app-id" && i + 1 < args.Length && long.TryParse(args[++i], out var id))
                appId = id;
            else if (args[i] is "--key" && i + 1 < args.Length)
                keyFile = args[++i];
            else
                return Usage();
        }
        var apiUrl = GitHubApp.Load()?.ApiUrl ?? GitHubApp.DefaultApiUrl;
        if (appId > 0 && keyFile is not null)
            return await ImportAsync(appId, File.ReadAllText(keyFile), apiUrl).ConfigureAwait(false);
        if (appId > 0 || keyFile is not null)
            return Usage();

        var conversion = await ManifestFlowAsync(org, apiUrl).ConfigureAwait(false);
        Store(new GitHubAppConfig { AppId = conversion.Id, Slug = conversion.Slug, HtmlUrl = conversion.HtmlUrl, ApiUrl = apiUrl }, conversion.Pem);
        Ui.Title($"{ProductInfo.Name} GitHub App");
        Ui.Kv("app", $"{conversion.Slug} (id {conversion.Id})");
        Ui.Kv("private key", "in the vault; GitHub keeps no copy you can download again");
        var install = $"https://github.com/apps/{conversion.Slug}/installations/new";
        Ui.Kv("next", $"install the app on your repos: {install}");
        OpenBrowser(install);
        return 0;
    }

    private static async Task<int> ImportAsync(long appId, string pem, string apiUrl)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var config = new GitHubAppConfig { AppId = appId, ApiUrl = apiUrl };
        var (slug, htmlUrl) = await GitHubApp.GetAppAsync(Http, config, rsa, CancellationToken.None).ConfigureAwait(false);
        config.Slug = slug;
        config.HtmlUrl = htmlUrl;
        Store(config, pem);
        Ui.Title($"{ProductInfo.Name} GitHub App");
        Ui.Kv("app", $"{slug} (id {appId})");
        Ui.Kv("private key", "in the vault; delete the .pem file");
        return 0;
    }

    private static void Store(GitHubAppConfig config, string pem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var der = rsa.ExportPkcs8PrivateKey();
        try
        {
            new CredentialVault().SaveTarget(GitHubApp.KeyTarget, config.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture), der);
        }
        finally
        {
            Array.Clear(der);
        }
        GitHubApp.Save(config);
    }

    private static async Task<int> StatusAsync()
    {
        var config = GitHubApp.Load();
        Ui.Title($"{ProductInfo.Name} GitHub App");
        if (config is null)
        {
            Ui.Kv("app", "none; run cw github app setup");
            return 0;
        }
        Ui.Kv("app", $"{config.Slug} (id {config.AppId})");
        Ui.Kv("page", config.HtmlUrl ?? "");
        var entry = new CredentialVault().ReadTarget(GitHubApp.KeyTarget);
        Ui.Kv("private key", entry is null ? Ui.Warn("missing; run cw github app setup") : "in the vault");
        var repo = GitHubApp.RepoFor(["pr", "list"], Environment.GetEnvironmentVariable("GH_REPO"), Environment.CurrentDirectory);
        if (entry is null || repo is null)
            return 0;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(entry.Blob, out _);
            var token = await GitHubApp.CreateRepoTokenAsync(Http, config, rsa, repo, CancellationToken.None).ConfigureAwait(false);
            Ui.Kv(repo, token is null
                ? Ui.Warn($"the app is not installed here; install it: https://github.com/apps/{config.Slug}/installations/new")
                : $"a token for this repo only works; it ends at {token.ExpiresAt.ToLocalTime():HH:mm}");
        }
        finally
        {
            Array.Clear(entry.Blob);
        }
        return 0;
    }

    private static int Remove()
    {
        var config = GitHubApp.Load();
        new CredentialVault().DeleteTarget(GitHubApp.KeyTarget);
        File.Delete(GitHubApp.ConfigPath());
        Ui.Title($"{ProductInfo.Name} GitHub App");
        Ui.Kv("app", config is null ? "none" : $"{config.Slug} no longer used; gh gets your personal token");
        if (config?.HtmlUrl is { Length: > 0 } page)
            Ui.Kv("next", $"delete the app on GitHub if you do not need it: {page}/advanced");
        return 0;
    }

    /// <summary>
    /// The GitHub App manifest flow: a local page posts the manifest to GitHub, you confirm there,
    /// and GitHub sends the browser back here with a code. The code gives the app and its key once.
    /// </summary>
    private static async Task<GitHubApp.ManifestConversion> ManifestFlowAsync(string? org, string apiUrl)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var baseUrl = $"http://127.0.0.1:{port}";
        var manifest = GitHubApp.Manifest($"CmdWarden {Environment.UserName} {state[..4].ToLowerInvariant()}", $"{baseUrl}/done");
        var target = org is null
            ? $"https://github.com/settings/apps/new?state={state}"
            : $"https://github.com/organizations/{Uri.EscapeDataString(org)}/settings/apps/new?state={state}";

        Ui.Line($"Opening {baseUrl}/start in your browser. Confirm the new app on GitHub.");
        OpenBrowser($"{baseUrl}/start");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var line = await ReadRequestLineAsync(stream, timeout.Token).ConfigureAwait(false);
            var path = line.Split(' ') is [_, var p, ..] ? p : "/";
            if (path == "/start")
            {
                await RespondAsync(stream, $$"""
                    <!doctype html><meta charset="utf-8"><title>CmdWarden GitHub App</title>
                    <body style="font-family:Segoe UI,sans-serif;background:#202020;color:#f3f3f3">
                    <form id="f" method="post" action="{{WebUtility.HtmlEncode(target)}}">
                    <input type="hidden" name="manifest" value="{{WebUtility.HtmlEncode(manifest)}}">
                    <p>Sending the app manifest to GitHub...</p><button type="submit">Continue</button></form>
                    <script>document.getElementById('f').submit()</script>
                    """).ConfigureAwait(false);
                continue;
            }
            if (!path.StartsWith("/done?", StringComparison.Ordinal))
            {
                await RespondAsync(stream, "", 404).ConfigureAwait(false);
                continue;
            }
            var query = System.Web.HttpUtility.ParseQueryString(path[6..]);
            if (query["state"] != state || query["code"] is not { Length: > 0 } code)
            {
                await RespondAsync(stream, "<p>This link is not from this setup. Run cw github app setup again.</p>", 400).ConfigureAwait(false);
                continue;
            }
            await RespondAsync(stream, """
                <!doctype html><meta charset="utf-8"><title>CmdWarden GitHub App</title>
                <body style="font-family:Segoe UI,sans-serif;background:#202020;color:#f3f3f3">
                <p>The app is made. Go back to the terminal.</p>
                """).ConfigureAwait(false);
            return await GitHubApp.ConvertManifestAsync(Http, apiUrl, code, timeout.Token).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadRequestLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
                break;
            read += n;
            if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }
        var text = Encoding.ASCII.GetString(buffer, 0, read);
        return text.Split("\r\n")[0];
    }

    private static async Task RespondAsync(Stream stream, string html, int status = 200)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Ui.Line($"Open this page: {url}");
        }
    }
}
