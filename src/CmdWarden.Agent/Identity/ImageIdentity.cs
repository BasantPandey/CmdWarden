using System.Collections.Concurrent;
using System.Runtime.Versioning;
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
        var id = LauncherImage.Identify(path);
        return new Identity(info.Length, info.LastWriteTimeUtc, id.Kind, id.PolicyKey, id.Publisher, id.Thumbprint, id.Sha256);
    }
}
