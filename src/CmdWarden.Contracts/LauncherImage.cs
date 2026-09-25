using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CmdWarden.Contracts;

/// <summary>Launcher identity of one image file: Authenticode signer, else path + SHA-256.</summary>
public sealed record LauncherImageIdentity(string Kind, string PolicyKey, string? Publisher, string? Thumbprint, string? Sha256);

/// <summary>
/// Policy key of an executable (spec section 5). The Session Agent uses it for each process in a
/// caller chain; <c>cw launch</c> uses it to enroll a harness before it starts.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LauncherImage
{
    public static LauncherImageIdentity Identify(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile still the practical PE Authenticode loader
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            if (!string.IsNullOrEmpty(cert.Thumbprint))
            {
                return new LauncherImageIdentity(
                    LauncherKinds.Authenticode,
                    LauncherKinds.PolicyKeyAuthenticode(cert.Thumbprint),
                    cert.GetNameInfo(X509NameType.SimpleName, false) ?? cert.Subject,
                    cert.Thumbprint,
                    HashFile(path));
            }
        }
        catch (Exception)
        {
            // unsigned or unreadable signature: hash fallback
        }

        try
        {
            var hash = HashFile(path);
            return new LauncherImageIdentity(LauncherKinds.PathHash, LauncherKinds.PolicyKeyPathHash(hash), null, null, hash);
        }
        catch
        {
            return new LauncherImageIdentity(LauncherKinds.Unknown, LauncherKinds.PolicyKeyUnknown, null, null, null);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
