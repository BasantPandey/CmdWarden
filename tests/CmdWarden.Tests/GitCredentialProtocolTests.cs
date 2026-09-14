using System.Text;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>Git credential helper line protocol (#206).</summary>
public class GitCredentialProtocolTests
{
    [Fact]
    public void Parse_reads_key_value_lines_until_a_blank_line()
    {
        var attrs = GitCredentialProtocol.Parse(new StringReader(
            "capability[]=authtype\n" +
            "capability[]=state\n" +
            "protocol=https\n" +
            "host=github.com\n" +
            "path=o/r.git\n" +
            "username=alice\n" +
            "\n" +
            "ignored=after-blank\n"));

        Assert.Equal("https", attrs.Protocol);
        Assert.Equal("github.com", attrs.Host);
        Assert.Equal("o/r.git", attrs.Path);
        Assert.Equal("alice", attrs.Username);
        Assert.Equal("https://github.com/o/r.git", attrs.ServerUrl);
        Assert.Equal(new[] { "authtype", "state" }, attrs.Capabilities);
    }

    [Fact]
    public void Parse_reads_store_secret_fields_and_ephemeral()
    {
        var attrs = GitCredentialProtocol.Parse(new StringReader(
            "protocol=https\n" +
            "host=github.com:8443\n" +
            "username=alice\n" +
            "password=s3cret\n" +
            "password_expiry_utc=1700000000\n" +
            "oauth_refresh_token=rt-1\n" +
            "ephemeral=1\n"));

        Assert.Equal("s3cret", attrs.Password);
        Assert.Equal("1700000000", attrs.PasswordExpiryUtc);
        Assert.Equal("rt-1", attrs.OauthRefreshToken);
        Assert.True(attrs.Ephemeral);
        Assert.Equal("https://github.com:8443", attrs.ServerUrl);
    }

    [Fact]
    public void Parse_stops_at_EOF_without_a_blank_line()
    {
        var attrs = GitCredentialProtocol.Parse(new StringReader("protocol=https\nhost=example.test"));
        Assert.Equal("https://example.test", attrs.ServerUrl);
    }

    [Fact]
    public void Parse_ignores_a_line_over_the_byte_cap()
    {
        var huge = new string('x', GitCredentialProtocol.MaxLineBytes) + "=nope";
        var attrs = GitCredentialProtocol.Parse(new StringReader(
            "protocol=https\n" + huge + "\nhost=example.test\n"));
        Assert.Equal("https://example.test", attrs.ServerUrl);
    }

    [Fact]
    public void WriteGet_prints_username_then_password_and_echoes_capabilities()
    {
        var request = GitCredentialProtocol.Parse(new StringReader(
            "capability[]=authtype\ncapability[]=state\nprotocol=https\nhost=github.com\n"));
        var stdout = new StringWriter();
        GitCredentialProtocol.WriteGet(stdout, request, "x-access-token", "tok-1", "1800000000", "rt-9");

        Assert.Equal(
            "capability[]=authtype\n" +
            "capability[]=state\n" +
            "username=x-access-token\n" +
            "password=tok-1\n" +
            "oauth_refresh_token=rt-9\n" +
            "password_expiry_utc=1800000000\n",
            stdout.ToString());
    }

    [Fact]
    public void WriteDeny_prints_quit_true()
    {
        var stdout = new StringWriter();
        GitCredentialProtocol.WriteDeny(stdout);
        Assert.Equal("quit=true\n", stdout.ToString());
    }

    [Fact]
    public void DenyStderr_names_the_url_and_reason()
    {
        Assert.Equal(
            "CmdWarden: git credential denied for https://github.com (HelperParentMissing)",
            GitCredentialProtocol.DenyStderr("https://github.com", "HelperParentMissing"));
    }

    [Fact]
    public void MaxLineBytes_is_the_git_limit()
    {
        Assert.Equal(65535, GitCredentialProtocol.MaxLineBytes);
        Assert.True(Encoding.UTF8.GetByteCount(new string('a', 65534)) + 1 <= GitCredentialProtocol.MaxLineBytes);
    }
}
