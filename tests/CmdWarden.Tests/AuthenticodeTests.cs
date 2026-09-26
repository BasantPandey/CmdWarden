using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>#42: a real signature check, and the signer pin of the Approval Gate.</summary>
public class AuthenticodeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cw-sig-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>The test host exe: Microsoft signs it with an embedded signature.</summary>
    private static string SignedExe => Environment.ProcessPath!;

    [Fact]
    public void A_signed_exe_has_a_verified_signer()
    {
        Assert.NotNull(Authenticode.VerifiedSigner(SignedExe));
    }

    [Fact]
    public void A_changed_byte_breaks_the_signature_and_the_launcher_falls_back_to_its_hash()
    {
        var copy = Path.Combine(_dir, "tampered.exe");
        var bytes = File.ReadAllBytes(SignedExe);
        bytes[bytes.Length / 3] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);

        Assert.Null(Authenticode.VerifiedSigner(copy));
        Assert.Null(ToolPinStore.TryReadSignerThumbprint(copy));
        Assert.Equal(LauncherKinds.PathHash, LauncherImage.Identify(copy).Kind);
        Assert.Equal(LauncherKinds.Authenticode, LauncherImage.Identify(SignedExe).Kind);
    }

    [Fact]
    public void An_unsigned_file_has_no_signer()
    {
        var file = Path.Combine(_dir, "plain.exe");
        File.WriteAllBytes(file, [0x4D, 0x5A, 0, 0]);
        Assert.Null(Authenticode.VerifiedSigner(file));
        Assert.Null(Authenticode.VerifiedSigner(Path.Combine(_dir, "missing.exe")));
    }

    [Fact]
    public void The_pin_allows_the_same_signer_and_everything_when_unsigned()
    {
        var signer = Authenticode.VerifiedSigner(SignedExe);
        var plain = Path.Combine(_dir, "plain.exe");
        File.WriteAllBytes(plain, [0x4D, 0x5A]);

        Assert.True(CmdWardenSigner.Allows(SignedExe, signer));
        Assert.True(CmdWardenSigner.Allows(plain, ownSigner: null));
        Assert.False(CmdWardenSigner.Allows(plain, signer));
        Assert.False(CmdWardenSigner.Allows(SignedExe, new string('A', 40)));
    }

    [Fact]
    public void A_signed_agent_does_not_start_an_approval_gate_with_another_signer()
    {
        var helper = Path.Combine(_dir, "CmdWarden.ApprovalGate.exe");
        File.WriteAllBytes(helper, [0x4D, 0x5A]);
        var started = false;
        var gate = new ProcessApprovalGate(
            resolveHelperPath: () => helper,
            startProcess: _ => { started = true; return null; },
            ownSigner: () => new string('A', 40));

        var answer = gate.Prompt(new ApprovalRequest("gh", "write", "Read", "key", "authenticode", null, "GH_TOKEN", null, null, null));

        Assert.Equal(ApprovalOutcome.Unavailable, answer.Outcome);
        Assert.False(started);
    }
}
