using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#97: <c>cw save NAME --generate</c> makes strong passwords that sign-up forms accept.</summary>
public class PasswordGeneratorTests
{
    [Theory]
    [InlineData(PasswordGenerator.MinLength)]
    [InlineData(PasswordGenerator.DefaultLength)]
    [InlineData(PasswordGenerator.MaxLength)]
    public void A_password_has_the_length_and_every_kind_of_character(int length)
    {
        for (var i = 0; i < 200; i++)
        {
            var password = new string(PasswordGenerator.New(length));
            Assert.Equal(length, password.Length);
            Assert.Contains(password, char.IsAsciiLetterUpper);
            Assert.Contains(password, char.IsAsciiLetterLower);
            Assert.Contains(password, char.IsAsciiDigit);
            Assert.Contains(password, c => "!#$%&*+-.:=?@^_~".Contains(c));
            Assert.DoesNotContain(password, c => "\"'`\\/ <>".Contains(c));
        }
    }

    [Fact]
    public void Two_passwords_are_not_the_same() =>
        Assert.NotEqual(new string(PasswordGenerator.New()), new string(PasswordGenerator.New()));

    [Theory]
    [InlineData(PasswordGenerator.MinLength - 1)]
    [InlineData(PasswordGenerator.MaxLength + 1)]
    public void A_length_out_of_range_is_an_error(int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.New(length));
}
