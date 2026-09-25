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
    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable(VaultNames.RootEnvVar, "CmdWardenTest/");
}
