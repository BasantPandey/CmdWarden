using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CmdWarden.Contracts;

namespace CmdWarden.Agent.Identity;

/// <summary>
/// Authenticode when present; otherwise path+SHA-256 of the image file.
/// Results are cached per image (path + length + last write) because every gRPC call
/// walks the caller's whole process chain, and hashing a few hundred MB of terminal
/// and editor executables on each health probe took seconds.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ImageIdentity
{
    private sealed record Identity(long Length, DateTime LastWriteUtc, string Kind, string PolicyKey, string? Publisher, string? Thumbprint, string? Sha256);

    // ponytail: unbounded, keyed by path; the set of distinct launcher images on one machine is small.
    private static readonly ConcurrentDictionary<string, Identity> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Populate(ProcessNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Path) || !File.Exists(node.Path))
        {
            node.Kind = LauncherKinds.Unknown;
            node.PolicyKey = LauncherKinds.PolicyKeyUnknown;
            return;
        }

        var info = new FileInfo(node.Path);
        if (!Cache.TryGetValue(node.Path, out var id) || id.Length != info.Length || id.LastWriteUtc != info.LastWriteTimeUtc)
        {
            id = Compute(node.Path, info);
            Cache[node.Path] = id;
        }

        node.Kind = id.Kind;
        node.PolicyKey = id.PolicyKey;
        node.Publisher = id.Publisher;
        node.Thumbprint = id.Thumbprint;
        node.Sha256 = id.Sha256;
    }

    private static Identity Compute(string path, FileInfo info)
    {
        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile still the practical PE Authenticode loader
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            if (!string.IsNullOrEmpty(cert.Thumbprint))
            {
                return new Identity(
                    info.Length,
                    info.LastWriteTimeUtc,
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
            return new Identity(info.Length, info.LastWriteTimeUtc, LauncherKinds.PathHash, LauncherKinds.PolicyKeyPathHash(hash), null, null, hash);
        }
        catch
        {
            return new Identity(info.Length, info.LastWriteTimeUtc, LauncherKinds.Unknown, LauncherKinds.PolicyKeyUnknown, null, null, null);
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
