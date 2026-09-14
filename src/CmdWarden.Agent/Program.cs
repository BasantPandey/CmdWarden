using System.Runtime.Versioning;
using CmdWarden.Agent;
using CmdWarden.Agent.Approval;
using CmdWarden.Contracts;

[SupportedOSPlatform("windows")]
static async Task MainEntry(string[] args)
{
    var pipeName = Environment.GetEnvironmentVariable("CW_PIPE_NAME");
    if (string.IsNullOrWhiteSpace(pipeName))
        pipeName = AgentEndpoints.PipeName;

    var policyPath = PolicyStore.DefaultPath();
    var approvalMode = Environment.GetEnvironmentVariable(ApprovalGateFactory.EnvVar);
    if (string.IsNullOrWhiteSpace(approvalMode))
        approvalMode = "prompt (default → process helper)";

    Console.WriteLine($"{ProductInfo.Name} Session Agent {ProductInfo.Version}");
    Console.WriteLine($"Listening on named pipe: {pipeName}");
    Console.WriteLine($"Policy store: {policyPath}");
    Console.WriteLine(
        $"Approval Gate mode: {approvalMode} " +
        "(CW_APPROVAL_MODE=prompt|messagebox|allow|deny|session|off)");
    Console.WriteLine("Pipe ACL: CurrentUserOnly (not default Everyone-open). Ctrl+C to stop.");

    var app = AgentHost.Build(args, pipeName, policyPath);
    await app.RunAsync();
}

await MainEntry(args);
