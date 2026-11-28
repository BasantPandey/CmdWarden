using System.Net;
using System.Net.Http;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class AwsSessionTests
{
    [Theory]
    [InlineData("sts get-session-token", true)]
    [InlineData("sts get-federation-token", true)]
    [InlineData("--profile prod sts get-session-token", true)]
    [InlineData("sts get-caller-identity", false)]
    [InlineData("get-session-token", false)]
    [InlineData("s3 ls", false)]
    public void Long_term_key_stays_for_two_sts_commands(string argv, bool expected) =>
        Assert.Equal(expected, AwsSession.KeepsLongTermKey(argv.Split(' ')));

    [Fact]
    public void A_session_replaces_the_long_term_key()
    {
        var env = Env();
        string? header = null;
        string? body = null;
        AwsSession.Apply(env, ["s3", "ls"], request =>
        {
            header = request.Headers.GetValues("Authorization").Single();
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <GetSessionTokenResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
                      <GetSessionTokenResult>
                        <Credentials>
                          <AccessKeyId>ASIATEMP</AccessKeyId>
                          <SecretAccessKey>temp-secret</SecretAccessKey>
                          <SessionToken>session-token</SessionToken>
                        </Credentials>
                      </GetSessionTokenResult>
                    </GetSessionTokenResponse>
                    """),
            });
        });

        Assert.StartsWith("AWS4-HMAC-SHA256", header, StringComparison.Ordinal);
        Assert.Contains("AKIAEXAMPLE", header, StringComparison.Ordinal);
        Assert.Contains("DurationSeconds=900", body, StringComparison.Ordinal);
        Assert.Equal("ASIATEMP", env["AWS_ACCESS_KEY_ID"]);
        Assert.Equal("temp-secret", env["AWS_SECRET_ACCESS_KEY"]);
        Assert.Equal("session-token", env["AWS_SESSION_TOKEN"]);
    }

    [Fact]
    public void A_refusal_leaves_the_env_unchanged()
    {
        var env = Env();
        Assert.Throws<AwsSessionException>(() => AwsSession.Apply(env, ["s3", "ls"], _ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<ErrorResponse><Error><Code>AccessDenied</Code></Error></ErrorResponse>"),
            })));
        Assert.Equal("AKIAEXAMPLE", env["AWS_ACCESS_KEY_ID"]);
        Assert.Equal("long-term-secret", env["AWS_SECRET_ACCESS_KEY"]);
        Assert.False(env.ContainsKey("AWS_SESSION_TOKEN"));
    }

    [Fact]
    public void A_missing_key_does_not_call_sts()
    {
        var called = false;
        var env = new Dictionary<string, string> { ["AWS_ACCESS_KEY_ID"] = "AKIAEXAMPLE" };
        AwsSession.Apply(env, ["s3", "ls"], _ =>
        {
            called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        Assert.False(called);
        Assert.False(env.ContainsKey("AWS_SESSION_TOKEN"));
    }

    [Fact]
    public void Sts_token_commands_keep_the_long_term_key()
    {
        var called = false;
        var env = Env();
        AwsSession.Apply(env, ["sts", "get-session-token"], _ =>
        {
            called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        Assert.False(called);
        Assert.Equal("long-term-secret", env["AWS_SECRET_ACCESS_KEY"]);
    }

    [Fact]
    public void The_same_inputs_sign_the_same_header()
    {
        const string body = "Action=GetSessionToken&DurationSeconds=900&Version=2011-06-15";
        var first = AwsSession.Sign("AKIAEXAMPLE", "secret", body, "20261128T120000Z", "20261128");
        var second = AwsSession.Sign("AKIAEXAMPLE", "secret", body, "20261128T120000Z", "20261128");
        Assert.Equal(first, second);
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAEXAMPLE/20261128/us-east-1/sts/aws4_request, SignedHeaders=content-type;host;x-amz-date, Signature=3622b3a8785550d98c7c599c3596d6526b31f12e483e1d8922e2295cbf9590aa",
            first);
    }

    private static Dictionary<string, string> Env() => new()
    {
        ["AWS_ACCESS_KEY_ID"] = "AKIAEXAMPLE",
        ["AWS_SECRET_ACCESS_KEY"] = "long-term-secret",
    };
}
