using System.Reflection;

namespace CmdWarden.Contracts;

/// <summary>
/// Stable product identity for CLI banners and doctor output.
/// Version tracks MSBuild InformationalVersion when built with -p:Version= (CI tags).
/// </summary>
public static class ProductInfo
{
    public const string Name = "CmdWarden";
    public const string CliPrimary = "cw";
    public const string CliAlias = "cmdwarden";

    /// <summary>Fallback when assembly metadata is missing (matches default project Version).</summary>
    public const string DefaultVersion = "0.1.0";

    public static string Version
    {
        get
        {
            var info = typeof(ProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
                return DefaultVersion;

            // SDK may append "+commit" - strip for user-facing version.
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }
}
