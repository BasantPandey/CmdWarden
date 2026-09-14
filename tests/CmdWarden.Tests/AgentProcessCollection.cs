namespace CmdWarden.Tests;

/// <summary>
/// Serialize tests that spawn Session Agent processes (named-pipe races under load).
/// </summary>
[CollectionDefinition("AgentProcess", DisableParallelization = true)]
public sealed class AgentProcessCollection;
