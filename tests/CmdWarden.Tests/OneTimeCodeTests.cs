using CmdWarden.Contracts.Proxy;

namespace CmdWarden.Tests;

/// <summary>#93: TOTP codes match the test values of RFC 6238, appendix B.</summary>
public class OneTimeCodeTests
{
    // The RFC keys "1234567890..." as base32: 20 bytes for SHA1, 32 for SHA256, 64 for SHA512.
    private const string Sha1Key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private const string Sha256Key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZA";
    private const string Sha512Key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQGEZDGNA";

    [Theory]
    [InlineData(Sha1Key, "SHA1", 59, "94287082")]
    [InlineData(Sha1Key, "SHA1", 1111111109, "07081804")]
    [InlineData(Sha1Key, "SHA1", 1234567890, "89005924")]
    [InlineData(Sha1Key, "SHA1", 20000000000, "65353130")]
    [InlineData(Sha256Key, "SHA256", 59, "46119246")]
    [InlineData(Sha256Key, "SHA256", 1111111109, "68084774")]
    [InlineData(Sha512Key, "SHA512", 59, "90693936")]
    [InlineData(Sha512Key, "SHA512", 1111111109, "25091201")]
    public void Codes_match_the_rfc_test_values(string key, string algorithm, long time, string expected) =>
        Assert.Equal(expected, OneTimeCode.From(
            $"otpauth://totp/Test:me?secret={key}&issuer=Test&algorithm={algorithm}&digits=8&period=30",
            DateTimeOffset.FromUnixTimeSeconds(time)));

    [Fact]
    public void A_plain_base32_key_gives_six_digits_every_thirty_seconds()
    {
        var spaced = "gezd gnbv gy3t qojq gezd gnbv gy3t qojq";
        Assert.Equal("287082", OneTimeCode.From(spaced, DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal("287082", OneTimeCode.From(Sha1Key + "====", DateTimeOffset.FromUnixTimeSeconds(30)));
        Assert.NotEqual("287082", OneTimeCode.From(Sha1Key, DateTimeOffset.FromUnixTimeSeconds(60)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base32!")]
    [InlineData("otpauth://totp/x?issuer=NoSecret")]
    [InlineData("otpauth://totp/x?secret=GEZDGNBV&digits=12")]
    [InlineData("otpauth://totp/x?secret=GEZDGNBV&period=0")]
    [InlineData("otpauth://totp/x?secret=GEZDGNBV&algorithm=MD5")]
    [InlineData("otpauth://hotp/x?secret=GEZDGNBV&counter=1")]
    public void A_bad_secret_is_a_format_error(string secret) =>
        Assert.Throws<FormatException>(() => OneTimeCode.From(secret, DateTimeOffset.UnixEpoch));
}
