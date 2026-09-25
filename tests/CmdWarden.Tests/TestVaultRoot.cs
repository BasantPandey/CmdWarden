using System.Runtime.CompilerServices;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Tests save and delete vault entries such as GH_TOKEN and CmdWarden/gh/*. This moves every
/// entry of the test run, in this process and in each agent and shim it starts, under
/// CmdWardenTest/, so a test run on a real machine never touches the real vault.
/// </summary>
internal static class TestVaultRoot
{
    /// <summary>One root per test run, so a parallel run on the same PC keeps its entries.</summary>
    internal static readonly string Root = "CmdWardenTest/" + Guid.NewGuid().ToString("N")[..8] + "/";

    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable(VaultNames.RootEnvVar, Root);
        // Some tests stop their agent before the cleanup asks it to delete. Nothing of this run may stay.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAll();
    }

    private static void DeleteAll()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var vault = new CredentialVault();
        foreach (var entry in vault.ListTargets(VaultNames.ProductPrefix))
            vault.DeleteTarget(entry.Target);
    }
}
