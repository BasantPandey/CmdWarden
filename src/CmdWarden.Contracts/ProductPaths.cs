namespace CmdWarden.Contracts;

/// <summary>
/// Per-user CmdWarden product root under LocalAppData (override CW_PRODUCT_ROOT).
/// </summary>
public static class ProductPaths
{
    public const string EnvVar = "CW_PRODUCT_ROOT";

    public static string Root()
    {
        var overridePath = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath.Trim());

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "CmdWarden");
    }

    public static string ShimsDir() => Path.Combine(Root(), "shims");
    public static string AuditDir() => Path.Combine(Root(), "audit");
    public static string PinsDir() => Path.Combine(Root(), "pins");
    public static string PolicyPath() => Path.Combine(Root(), "policy.json");
}
