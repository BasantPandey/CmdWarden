using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace CmdWarden.Contracts;

/// <summary>
/// Authenticode check with WinVerifyTrust (#42). Unlike a plain certificate read, it also checks
/// that the file hash matches the signature, so a signature block copied onto another file fails.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Authenticode
{
    /// <summary>
    /// Thumbprint of the signer when the embedded signature is valid, else null. No revocation
    /// check and no network use, so the check is fast and works offline.
    /// </summary>
    public static string? VerifiedSigner(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || Verify(Path.GetFullPath(path)) != 0)
            return null;
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

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private static int Verify(string path)
    {
        var file = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = filePtr,
                dwStateAction = 1, // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x1000 | 0x80, // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.dwStateAction = 2; // WTD_STATEACTION_CLOSE
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}

/// <summary>
/// #42: the Session Agent trusts a CmdWarden helper only when the same signer signs it. A signed
/// agent next to an unsigned or foreign-signed Approval Gate fails closed. An unsigned build (a
/// developer build) has no pin.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CmdWardenSigner
{
    /// <summary>True when there is no pin, or when <paramref name="path"/> has a valid signature by the same signer.</summary>
    public static bool Allows(string path, string? ownSigner) =>
        ownSigner is null || string.Equals(Authenticode.VerifiedSigner(path), ownSigner, StringComparison.OrdinalIgnoreCase);
}
