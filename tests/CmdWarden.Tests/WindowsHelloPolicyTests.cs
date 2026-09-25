using CmdWarden.Contracts;

namespace CmdWarden.Tests;

public class WindowsHelloPolicyTests
{
    [Theory]
    [InlineData("off", CommandClass.SecretReveal, false)]
    [InlineData("secret-reveal", CommandClass.SecretReveal, true)]
    [InlineData("secret-reveal", CommandClass.Write, false)]
    [InlineData("write-and-up", CommandClass.Read, false)]
    [InlineData("write-and-up", CommandClass.Write, true)]
    [InlineData("write-and-up", CommandClass.Unknown, true)]
    [InlineData("write-and-up", CommandClass.SecretReveal, true)]
    [InlineData("nonsense", CommandClass.SecretReveal, true)]
    [InlineData(null, CommandClass.Write, false)]
    public void Requires_follows_the_mode(string? mode, CommandClass commandClass, bool expected) =>
        Assert.Equal(expected, WindowsHelloPolicy.Requires(mode, commandClass));

    [Theory]
    [InlineData(0, HelloCheck.Verified)]
    [InlineData(1, HelloCheck.NotAvailable)]
    [InlineData(2, HelloCheck.NotAvailable)]
    [InlineData(3, HelloCheck.NotAvailable)]
    [InlineData(4, HelloCheck.Canceled)]
    [InlineData(5, HelloCheck.Canceled)]
    [InlineData(6, HelloCheck.Canceled)]
    public void Verification_result_maps_to_check(int result, HelloCheck expected) =>
        Assert.Equal(expected, ApprovalAnswer.FromVerificationResult(result));

    [Fact]
    public void Policy_file_defaults_to_secret_reveal_and_saves_the_mode()
    {
        var path = Path.Combine(Path.GetTempPath(), "cw-hello-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new PolicyStore(path);
            store.Load();
            Assert.Equal(WindowsHelloPolicy.SecretReveal, store.HelloMode);

            store.SetHelloMode("Write-And-Up");
            store.Save();
            var again = new PolicyStore(path);
            again.Load();
            Assert.Equal(WindowsHelloPolicy.WriteAndUp, again.HelloMode);
            Assert.Throws<ArgumentException>(() => again.SetHelloMode("always"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Payload_carries_hello_required()
    {
        var request = new ApprovalRequest("gh", "secret-reveal", "Read", "k", "authenticode", null, "GH_TOKEN",
            null, "ai-harness", null, HelloRequired: true);
        var json = ApprovalHelperJson.Serialize(ApprovalPresentation.ToHelperPayload(request));
        Assert.True(ApprovalHelperJson.TryDeserialize(json)!.HelloRequired);
    }
}
