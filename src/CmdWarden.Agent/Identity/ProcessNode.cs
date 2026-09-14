namespace CmdWarden.Agent.Identity;

public sealed class ProcessNode
{
    public required int Pid { get; init; }
    public required int ParentPid { get; init; }
    public string? Path { get; init; }
    public string? FileName { get; init; }
    public DateTime? CreateTimeUtc { get; init; }
    public bool PidReuseSuspected { get; init; }
    public string Kind { get; set; } = CmdWarden.Contracts.LauncherKinds.Unknown;
    public string PolicyKey { get; set; } = CmdWarden.Contracts.LauncherKinds.PolicyKeyUnknown;
    public string? Publisher { get; set; }
    public string? Thumbprint { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class LauncherResolution
{
    public required int ClientPid { get; init; }
    public required bool ClientPidFromPipe { get; init; }
    public required ProcessNode Selected { get; init; }
    public required IReadOnlyList<ProcessNode> Chain { get; init; }
    public required bool AutoApproveEligible { get; init; }
    public string Notes { get; init; } = "";
}
