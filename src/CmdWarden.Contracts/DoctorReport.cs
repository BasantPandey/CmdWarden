using CmdWarden.Contracts.Grpc;

namespace CmdWarden.Contracts;

/// <summary>
/// Read-only snapshot of the facts <c>cw doctor</c> prints, for the management shell Doctor tab.
/// Never starts the agent (no lazy start); DOWN is a normal outcome.
/// </summary>
public sealed record DoctorReport(
    string ProductName,
    string ProductVersion,
    string ProductRoot,
    string ExpectedPipe,
    string? AgentBinaryPath,
    string? VaultUiPath,
    string ShortcutPath,
    bool ShortcutPresent,
    bool AgentUp,
    string? AgentDetail,
    HealthResponse? Health)
{
    /// <summary>True when the agent is up and reports a different version than this app's Contracts.</summary>
    public bool VersionMismatch =>
        Health is not null && !string.Equals(Health.Version, ProductVersion, StringComparison.Ordinal);

    public static async Task<DoctorReport> GatherAsync(
        string? pipeName = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var pipe = pipeName ?? AgentEndpoints.PipeName;
        var status = await AgentLifecycle.StatusAsync(pipe, timeout, cancellationToken).ConfigureAwait(false);

        HealthResponse? health = null;
        var up = status.Up;
        var detail = status.Detail;
        if (up)
        {
            try
            {
                health = await AgentHealthClient.GetHealthAsync(pipe, timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (AgentHealthClient.IsAgentUnreachable(ex))
            {
                // Rare race: status said up, health call lost the pipe.
                up = false;
                detail = "Session Agent is not reachable.";
            }
        }

        return new DoctorReport(
            ProductName: ProductInfo.Name,
            ProductVersion: ProductInfo.Version,
            ProductRoot: ProductPaths.Root(),
            ExpectedPipe: pipe,
            AgentBinaryPath: AgentLocator.FindAgentBinary(),
            VaultUiPath: SecretsManagerLocator.FindExePath(),
            ShortcutPath: SecretsManagerStartMenu.ShortcutPath,
            ShortcutPresent: OperatingSystem.IsWindows() && SecretsManagerStartMenu.ShortcutExists(),
            AgentUp: up,
            AgentDetail: detail,
            Health: health);
    }
}
