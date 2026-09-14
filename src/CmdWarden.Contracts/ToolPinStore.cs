using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace CmdWarden.Contracts;

/// <summary>
/// Absolute path + SHA-256 pin for a hardened tool under product root.
/// </summary>
public sealed class ToolPinStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _pinsDir;
    private readonly ConcurrentDictionary<string, string> _lastSeenSha256 = new(StringComparer.OrdinalIgnoreCase);

    public ToolPinStore(string? productRoot = null)
    {
        var root = productRoot ?? ProductPaths.Root();
        _pinsDir = Path.Combine(root, "pins");
    }

    public string PinsDirectory => _pinsDir;

    public void Save(string toolId, string absolutePath, string? sha256Hex = null, string? signerThumbprint = null)
    {
        if (string.IsNullOrWhiteSpace(toolId))
            throw new ArgumentException("Tool id is required.", nameof(toolId));
        if (string.IsNullOrWhiteSpace(absolutePath))
            throw new ArgumentException("Path is required.", nameof(absolutePath));

        var full = Path.GetFullPath(absolutePath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Pinned tool binary not found.", full);

        var hash = sha256Hex ?? ComputeSha256Hex(full);
        Directory.CreateDirectory(_pinsDir);
        // A re-pin keeps the harden mode; only cw unharden clears it (#204).
        var previous = TryGet(toolId);
        var doc = new ToolPinDocument
        {
            Tool = toolId.Trim().ToLowerInvariant(),
            Path = full,
            Sha256 = hash.ToLowerInvariant(),
            SignerThumbprint = string.IsNullOrWhiteSpace(signerThumbprint)
                ? TryReadSignerThumbprint(full)
                : signerThumbprint.Trim(),
            Mode = previous?.Mode,
            PreviousCredsStore = previous?.PreviousCredsStore,
            Strong = previous?.Strong,
        };
        Write(toolId, doc);
    }

    /// <summary>Record strong mode and what it replaced; null <paramref name="mode"/> returns to compat.</summary>
    public void SetMode(string toolId, string? mode, string? previousCredsStore = null, StrongState? strong = null)
    {
        var pin = TryGet(toolId) ?? throw new InvalidOperationException($"No pin for tool '{toolId}'.");
        Write(toolId, new ToolPinDocument
        {
            Tool = pin.Tool,
            Path = pin.Path,
            Sha256 = pin.Sha256,
            SignerThumbprint = pin.SignerThumbprint,
            Mode = mode,
            PreviousCredsStore = mode is null ? null : previousCredsStore,
            Strong = mode is null ? null : strong,
        });
    }

    public bool Delete(string toolId)
    {
        var file = PinFile(toolId);
        if (!File.Exists(file))
            return false;
        File.Delete(file);
        return true;
    }

    private void Write(string toolId, ToolPinDocument doc) =>
        File.WriteAllText(PinFile(toolId), JsonSerializer.Serialize(doc, JsonOptions));

    public ToolPin? TryGet(string toolId)
    {
        var file = PinFile(toolId);
        if (!File.Exists(file))
            return null;

        var json = File.ReadAllText(file);
        var doc = JsonSerializer.Deserialize<ToolPinDocument>(json, JsonOptions);
        if (doc is null || string.IsNullOrWhiteSpace(doc.Path) || string.IsNullOrWhiteSpace(doc.Sha256))
            return null;

        return new ToolPin(doc.Tool ?? toolId, doc.Path, doc.Sha256, doc.SignerThumbprint, doc.Mode, doc.PreviousCredsStore, doc.Strong);
    }

    /// <summary>Every stored pin. Unreadable files are skipped.</summary>
    public IReadOnlyList<ToolPin> All()
    {
        if (!Directory.Exists(_pinsDir))
            return Array.Empty<ToolPin>();
        var pins = new List<ToolPin>();
        foreach (var file in Directory.GetFiles(_pinsDir, "*.json"))
        {
            try
            {
                if (TryGet(Path.GetFileNameWithoutExtension(file)) is { } pin)
                    pins.Add(pin);
            }
            catch (Exception)
            {
                // A half-written pin file must not break launcher resolution.
            }
        }
        return pins;
    }

    public PinCheckResult Check(string toolId)
    {
        var pin = TryGet(toolId);
        if (pin is null)
            return PinCheckResult.Missing();

        if (!File.Exists(pin.Path))
            return PinCheckResult.Mismatch("pinned path does not exist: " + pin.Path);

        var actual = ComputeSha256Hex(pin.Path);
        if (!string.Equals(actual, pin.Sha256, StringComparison.OrdinalIgnoreCase))
            return PinCheckResult.Mismatch("content hash does not match pin");

        return PinCheckResult.Ok(pin);
    }

    /// <summary>
    /// True when this tool's stored pin hash differs from the last time this instance looked,
    /// including the first time a pin is seen at all - a grant taken out while the tool had no pin
    /// yet must still drop once the harden that pins it lands (#133).
    /// </summary>
    public bool DetectRepin(string toolId)
    {
        var pin = TryGet(toolId);
        if (pin is null)
            return false;

        var key = toolId.Trim().ToLowerInvariant();
        var changed = !_lastSeenSha256.TryGetValue(key, out var previous)
            || !string.Equals(previous, pin.Sha256, StringComparison.OrdinalIgnoreCase);
        _lastSeenSha256[key] = pin.Sha256;
        return changed;
    }

    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string PinFile(string toolId) =>
        Path.Combine(_pinsDir, toolId.Trim().ToLowerInvariant() + ".json");

    /// <summary>Authenticode thumbprint of the pinned binary, or null when unsigned.</summary>
    public static string? TryReadSignerThumbprint(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile still the practical PE Authenticode loader
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return string.IsNullOrEmpty(cert.Thumbprint) ? null : cert.Thumbprint;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class ToolPinDocument
    {
        public string? Tool { get; set; }
        public string? Path { get; set; }
        public string? Sha256 { get; set; }
        public string? SignerThumbprint { get; set; }
        public string? Mode { get; set; }
        public string? PreviousCredsStore { get; set; }
        public StrongState? Strong { get; set; }
    }
}

/// <summary>What a strong harden replaced, so unharden can put it back (#207, #209).</summary>
public sealed class StrongState
{
    /// <summary>git: global <c>credential.helper</c> values before the reset.</summary>
    public List<string>? PreviousHelpers { get; set; }
    /// <summary>git: removed <c>credential.&lt;url&gt;.helper</c> lines written by gh auth setup-git, in file order.</summary>
    public List<ConfigLine>? GhHelperBlocks { get; set; }
    /// <summary>gh: migrated hosts with their users and active user.</summary>
    public List<GhHostState>? Hosts { get; set; }
}

public sealed class ConfigLine
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class GhHostState
{
    public string Host { get; set; } = "";
    public List<string> Users { get; set; } = new();
    public string? ActiveUser { get; set; }
}

/// <param name="Mode">Null is compat; <see cref="ToolPin.StrongMode"/> after a strong harden.</param>
/// <param name="PreviousCredsStore">docker only: the credsStore value before strong harden, for unharden.</param>
public sealed record ToolPin(
    string Tool,
    string Path,
    string Sha256,
    string? SignerThumbprint = null,
    string? Mode = null,
    string? PreviousCredsStore = null,
    StrongState? Strong = null)
{
    public const string StrongMode = "strong";

    public bool IsStrong => string.Equals(Mode, StrongMode, StringComparison.OrdinalIgnoreCase);
}

public sealed class PinCheckResult
{
    public bool IsOk { get; private init; }
    public bool IsMissing { get; private init; }
    public string? Error { get; private init; }
    public ToolPin? Pin { get; private init; }

    public static PinCheckResult Ok(ToolPin pin) => new() { IsOk = true, Pin = pin };
    public static PinCheckResult Missing() => new() { IsMissing = true, Error = "pin not found" };
    public static PinCheckResult Mismatch(string error) => new() { Error = error };
}
