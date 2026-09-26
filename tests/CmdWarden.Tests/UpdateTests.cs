using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CmdWarden.Cli;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>cw update and cw uninstall (#45).</summary>
public class UpdateTests
{
    [Theory]
    [InlineData("0.7.0", "0.6.0", true)]
    [InlineData("0.10.0", "0.9.1", true)]
    [InlineData("0.6.0", "0.6.0", false)]
    [InlineData("0.5.0", "0.6.0", false)]
    [InlineData("0.6.0", "0.6.0-beta", true)]
    [InlineData("0.6.0-beta", "0.6.0", false)]
    [InlineData("junk", "0.1.0", false)]
    public void IsNewer_orders_versions(string latest, string current, bool expected) =>
        Assert.Equal(expected, ReleaseUpdate.IsNewer(latest, current));

    [Fact]
    public void Parse_reads_the_tag_and_the_setup_zip_digest()
    {
        var release = ReleaseUpdate.Parse("""
            {"tag_name":"v0.7.0","assets":[
              {"name":"CmdWarden.0.7.0-setup.zip","browser_download_url":"https://x/s.zip","digest":"sha256:ABCD"},
              {"name":"CmdWarden.0.7.0.nupkg","browser_download_url":"https://x/n"}]}
            """);

        Assert.Equal("0.7.0", release.Version);
        Assert.Equal("https://x/s.zip", release.SetupZip!.Url);
        Assert.Equal("abcd", release.SetupZip.Sha256);
        Assert.Null(release.Assets[1].Sha256);
    }

    [Theory]
    [InlineData("\"C:\\Win dows\\powershell.exe\" -File \"C:\\a b\\u.ps1\"", "C:\\Win dows\\powershell.exe", "-File \"C:\\a b\\u.ps1\"")]
    [InlineData("C:\\x.exe /S", "C:\\x.exe", "/S")]
    [InlineData("C:\\x.exe", "C:\\x.exe", "")]
    public void SplitCommand_splits_the_program_from_its_arguments(string line, string exe, string arguments) =>
        Assert.Equal((exe, arguments), UpdateCommands.SplitCommand(line));

    [Theory]
    [InlineData("")]
    [InlineData("\"C:\\no close.exe -x")]
    public void SplitCommand_refuses_a_bad_line(string line) =>
        Assert.Null(UpdateCommands.SplitCommand(line));

    [Fact]
    [Trait("Category", "Process")]
    public async Task Update_runs_the_installer_of_a_verified_setup_zip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var dir = new TempDir();
        var marker = Path.Combine(dir.Path, "installer-ran.txt");
        await using var github = new FakeReleaseServer(SetupZip(marker), goodHash: true);

        var (code, output) = RunCw(github.Url, "update");

        Assert.True(code == 0, output);
        Assert.Contains("(ok)", output, StringComparison.Ordinal);
        Assert.True(await WaitForFileAsync(marker), "the installer did not run");
        Assert.Equal("-Force -Yes", (await File.ReadAllTextAsync(marker)).Trim());
    }

    [Fact]
    [Trait("Category", "Process")]
    public async Task Update_stops_on_a_wrong_hash_and_runs_nothing()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var dir = new TempDir();
        var marker = Path.Combine(dir.Path, "installer-ran.txt");
        await using var github = new FakeReleaseServer(SetupZip(marker), goodHash: false);

        var (code, output) = RunCw(github.Url, "update");

        Assert.Equal(1, code);
        Assert.Contains("The update stops", output, StringComparison.Ordinal);
        Assert.False(await WaitForFileAsync(marker, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    [Trait("Category", "Process")]
    public async Task Update_check_changes_nothing()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var dir = new TempDir();
        var marker = Path.Combine(dir.Path, "installer-ran.txt");
        await using var github = new FakeReleaseServer(SetupZip(marker), goodHash: true);

        var (code, output) = RunCw(github.Url, "update", "--check");

        Assert.Equal(0, code);
        Assert.Contains("99.0.0 is available", output, StringComparison.Ordinal);
        Assert.Equal(0, github.Downloads);
    }

    private static byte[] SetupZip(string marker)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("Install-CmdWarden.ps1").Open());
            w.Write($"Set-Content -LiteralPath '{marker}' -Value ($args -join ' ')");
        }
        return ms.ToArray();
    }

    private static (int Code, string Output) RunCw(string apiUrl, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(TestPaths.FindCliDll());
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment[ReleaseUpdate.ApiUrlEnvVar] = apiUrl;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(60_000), "cw did not exit");
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(path))
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("cw-update-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The latest-release API and one setup zip on 127.0.0.1.</summary>
    private sealed class FakeReleaseServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly byte[] _zip;
        private readonly string _digest;
        private readonly Task _loop;
        private int _downloads;

        public string Url { get; }
        public int Downloads => Volatile.Read(ref _downloads);

        public FakeReleaseServer(byte[] zip, bool goodHash)
        {
            _zip = zip;
            _digest = goodHash ? Convert.ToHexStringLower(SHA256.HashData(zip)) : new string('0', 64);
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _loop = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var path = (await reader.ReadLineAsync())?.Split(' ')[1] ?? "";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                }
                byte[] body;
                if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
                {
                    body = Encoding.UTF8.GetBytes($$"""
                        {"tag_name":"v99.0.0","assets":[{"name":"CmdWarden.99.0.0-setup.zip",
                          "browser_download_url":"{{Url}}/setup.zip","digest":"sha256:{{_digest}}"}]}
                        """);
                }
                else
                {
                    Interlocked.Increment(ref _downloads);
                    body = _zip;
                }
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _loop;
        }
    }
}
