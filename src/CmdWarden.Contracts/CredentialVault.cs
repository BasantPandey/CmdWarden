using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
namespace CmdWarden.Contracts;

/// <summary>
/// Windows Credential Manager vault (CRED_TYPE_GENERIC) for CmdWarden secrets.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialVault
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int CredEnumerateAllCredentials = 1;
    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE.</summary>
    public const int MaxBlobBytes = 2560;

    public void Save(string secretName, ReadOnlySpan<byte> value) =>
        SaveTarget(VaultNames.TargetName(secretName), Environment.UserName, value);

    /// <summary>
    /// Write one generic credential. <paramref name="userName"/> and <paramref name="comment"/> are
    /// metadata, never a secret. A <paramref name="label"/> becomes the one <c>label</c> attribute,
    /// the wincred layout (#204).
    /// </summary>
    public void SaveTarget(string target, string? userName, ReadOnlySpan<byte> value, string? comment = null, string? label = null)
    {
        if (value.Length == 0)
            throw new ArgumentException("Secret value must not be empty.", nameof(value));
        if (value.Length > MaxBlobBytes)
            throw new ArgumentException($"Secret exceeds Credential Manager blob limit ({MaxBlobBytes} bytes).", nameof(value));

        var blob = value.ToArray();
        var labelBytes = label is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(label);
        var pinned = GCHandle.Alloc(blob, GCHandleType.Pinned);
        var pinnedLabel = GCHandle.Alloc(labelBytes, GCHandleType.Pinned);
        var attribute = label is null ? IntPtr.Zero : Marshal.AllocHGlobal(Marshal.SizeOf<CREDENTIAL_ATTRIBUTE>());
        try
        {
            if (label is not null)
            {
                Marshal.StructureToPtr(new CREDENTIAL_ATTRIBUTE
                {
                    Keyword = "label",
                    Flags = 0,
                    ValueSize = (uint)labelBytes.Length,
                    Value = pinnedLabel.AddrOfPinnedObject(),
                }, attribute, false);
            }
            var cred = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = comment ?? (label is null ? "CmdWarden vault secret" : null),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = pinned.AddrOfPinnedObject(),
                Persist = CredPersistLocalMachine,
                AttributeCount = label is null ? 0u : 1u,
                Attributes = attribute,
                TargetAlias = null,
                UserName = string.IsNullOrEmpty(userName) ? Environment.UserName : userName,
            };

            if (!CredWrite(ref cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CredWrite failed for '{target}'.");
        }
        finally
        {
            if (attribute != IntPtr.Zero)
            {
                Marshal.DestroyStructure<CREDENTIAL_ATTRIBUTE>(attribute);
                Marshal.FreeHGlobal(attribute);
            }
            pinnedLabel.Free();
            pinned.Free();
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    public byte[] Read(string secretName)
    {
        var target = VaultNames.TargetName(secretName);
        return ReadTarget(target)?.Blob
            ?? throw new KeyNotFoundException($"Secret '{secretName}' not found in vault (target {target}).");
    }

    /// <summary>One generic credential, or null when the target does not exist.</summary>
    public VaultEntry? ReadTarget(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var credPtr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound)
                return null;
            throw new Win32Exception(err, $"CredRead failed for '{target}'.");
        }

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            var bytes = Array.Empty<byte>();
            if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
            {
                bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            }
            return new VaultEntry(cred.UserName ?? "", bytes, cred.Comment ?? "");
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public string[] ListNames() =>
        ListTargets(VaultNames.TargetPrefix).Select(e => VaultNames.SecretName(e.Target)).ToArray();

    /// <summary>Targets under <paramref name="prefix"/> with their user names. No blobs leave CredMan.</summary>
    public IReadOnlyList<VaultTarget> ListTargets(string prefix)
    {
        var filter = prefix + "*";
        if (!CredEnumerate(filter, 0, out var count, out var credentialsPtr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound)
                return Array.Empty<VaultTarget>();
            throw new Win32Exception(err, $"CredEnumerate failed for filter '{filter}'.");
        }

        try
        {
            var targets = new List<VaultTarget>((int)count);
            for (var i = 0; i < count; i++)
            {
                var credPtr = Marshal.ReadIntPtr(credentialsPtr, i * IntPtr.Size);
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                targets.Add(new VaultTarget(cred.TargetName, cred.UserName ?? ""));
            }
            return targets;
        }
        finally
        {
            CredFree(credentialsPtr);
        }
    }

    public bool Delete(string secretName) => DeleteTarget(VaultNames.TargetName(secretName));

    public bool DeleteTarget(string target)
    {
        if (CredDelete(target, CredTypeGeneric, 0))
            return true;

        var err = Marshal.GetLastWin32Error();
        if (err == ErrorNotFound)
            return false;
        throw new Win32Exception(err, $"CredDelete failed for '{target}'.");
    }

    /// <summary>
    /// Every generic credential of this user whose <c>label</c> attribute equals
    /// <paramref name="label"/>, blobs included. Docker Desktop and wincred store one such entry
    /// per registry (#204). Targets have no shared prefix, so this walks the whole store.
    /// </summary>
    public IReadOnlyList<LabeledEntry> ReadAllWithLabel(string label)
    {
        if (!CredEnumerate(null, CredEnumerateAllCredentials, out var count, out var credentialsPtr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound)
                return Array.Empty<LabeledEntry>();
            throw new Win32Exception(err, "CredEnumerate (all) failed.");
        }

        try
        {
            var entries = new List<LabeledEntry>();
            for (var i = 0; i < count; i++)
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(Marshal.ReadIntPtr(credentialsPtr, i * IntPtr.Size));
                if (cred.Type != CredTypeGeneric || !string.Equals(LabelOf(cred), label, StringComparison.Ordinal))
                    continue;
                var bytes = Array.Empty<byte>();
                if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
                {
                    bytes = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                }
                entries.Add(new LabeledEntry(PlainTarget(cred.TargetName), cred.UserName ?? "", bytes));
            }
            return entries;
        }
        finally
        {
            CredFree(credentialsPtr);
        }
    }

    /// <summary>Write one generic credential in wincred layout: target, user name, blob, one <c>label</c> attribute.</summary>
    public void WriteWithLabel(string target, string userName, ReadOnlySpan<byte> value, string label) =>
        SaveTarget(target, userName, value, comment: null, label: label);

    private const string EnumeratedGenericPrefix = "LegacyGeneric:target=";

    /// <summary>Enumerate-all returns <c>LegacyGeneric:target=&lt;name&gt;</c> for generic entries; callers want <c>&lt;name&gt;</c>.</summary>
    private static string PlainTarget(string enumeratedName) =>
        enumeratedName.StartsWith(EnumeratedGenericPrefix, StringComparison.OrdinalIgnoreCase)
            ? enumeratedName[EnumeratedGenericPrefix.Length..]
            : enumeratedName;

    private static string? LabelOf(CREDENTIAL cred)
    {
        var size = Marshal.SizeOf<CREDENTIAL_ATTRIBUTE>();
        for (var i = 0; i < cred.AttributeCount; i++)
        {
            var attr = Marshal.PtrToStructure<CREDENTIAL_ATTRIBUTE>(cred.Attributes + i * size);
            if (!string.Equals(attr.Keyword, "label", StringComparison.OrdinalIgnoreCase) || attr.Value == IntPtr.Zero)
                continue;
            var bytes = new byte[attr.ValueSize];
            Marshal.Copy(attr.Value, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        return null;
    }

    public static string Utf8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    public static byte[] Utf8Bytes(string value) => Encoding.UTF8.GetBytes(value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL_ATTRIBUTE
    {
        public string Keyword;
        public uint Flags;
        public uint ValueSize;
        public IntPtr Value;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string targetName, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string? filter, int flags, out uint count, out IntPtr credentials);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>One stored credential: user name metadata plus the secret bytes.</summary>
public sealed record VaultEntry(string UserName, byte[] Blob, string Comment = "");

/// <summary>One listed credential: target and user name only.</summary>
public sealed record VaultTarget(string Target, string UserName);

/// <summary>One foreign credential read by label, e.g. a Docker Desktop registry entry.</summary>
public sealed record LabeledEntry(string Target, string UserName, byte[] Blob);

/// <summary>Minimal zeroing helper without pulling System.Security.Cryptography for spans on all TFMs.</summary>
internal static class CryptographicOperations
{
    public static void ZeroMemory(byte[] buffer) => Array.Clear(buffer);
}
