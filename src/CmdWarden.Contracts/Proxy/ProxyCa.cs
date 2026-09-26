using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CmdWarden.Contracts.Proxy;

/// <summary>
/// The per-user CA of the placeholder proxy (#41). Its key stays in the CurrentUser\My store and
/// cannot be exported. No admin right is needed. The CA signs a short-lived certificate for each
/// host the proxy opens TLS for.
/// </summary>
public sealed class ProxyCa : IDisposable
{
    private readonly X509Certificate2 _ca;
    private readonly ECDsa _caKey;
    private readonly ConcurrentDictionary<string, X509Certificate2> _leaves = new(StringComparer.OrdinalIgnoreCase);

    private ProxyCa(X509Certificate2 ca)
    {
        _ca = ca;
        _caKey = ca.GetECDsaPrivateKey() ?? throw new InvalidOperationException("The proxy CA has no private key.");
    }

    public X509Certificate2 Certificate => _ca;

    /// <summary>Make a new CA in the CurrentUser\My store.</summary>
    public static X509Certificate2 Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"{KeyProxy.CaSubject} ({Environment.UserName})", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        // A PFX round trip puts the key in the user key store, not exportable.
        var ca = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(ca);
        return ca;
    }

    /// <summary>The CA with its key from the CurrentUser\My store, or null.</summary>
    public static ProxyCa? Open(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            return null;
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return found.Count == 0 || !found[0].HasPrivateKey ? null : new ProxyCa(found[0]);
    }

    /// <summary>Remove the CA from the My store and from the Root store of the user.</summary>
    public static void Remove(string thumbprint)
    {
        foreach (var name in new[] { StoreName.My, StoreName.Root })
        {
            using var store = new X509Store(name, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var cert in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false))
                store.Remove(cert);
        }
    }

    /// <summary>Trust the CA for this user. Windows asks the person to confirm.</summary>
    public static void Trust(X509Certificate2 ca)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(X509CertificateLoader.LoadCertificate(ca.RawData));
    }

    public static bool IsTrusted(string thumbprint)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).Count > 0;
    }

    /// <summary>A server certificate for the host, signed by the CA. One per host, for 30 days.</summary>
    public X509Certificate2 LeafFor(string host) => _leaves.AddOrUpdate(host,
        Issue,
        (h, old) => old.NotAfter > DateTime.Now.AddDays(1) ? old : Issue(h));

    private X509Certificate2 Issue(string host)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(host, out var ip))
            names.AddIpAddress(ip);
        else
            names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_ca, true, false));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        using var signed = request.Create(_ca.SubjectName, X509SignatureGenerator.CreateForECDsa(_caKey),
            DateTimeOffset.UtcNow.AddDays(-1), notAfter < _ca.NotAfter ? notAfter : _ca.NotAfter, serial);
        using var withKey = signed.CopyWithPrivateKey(key);
        // SChannel needs a key it can find again: an ephemeral key fails the TLS handshake.
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    public void Dispose()
    {
        _caKey.Dispose();
        _ca.Dispose();
        foreach (var leaf in _leaves.Values)
            leaf.Dispose();
    }
}
