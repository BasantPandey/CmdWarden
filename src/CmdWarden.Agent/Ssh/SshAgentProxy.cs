using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using CmdWarden.Agent.Identity;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Ssh;

namespace CmdWarden.Agent.Ssh;

/// <summary>
/// The ssh gate (#39): an ssh-agent pipe for SSH_AUTH_SOCK. Each sign request goes through the
/// Approval Gate first; everything else goes to the real agent as it is. A denied sign gets
/// SSH_AGENT_FAILURE, so ssh tries the next key or stops.
/// ponytail: one request and one reply at a time per client, as ssh and ssh-add use the pipe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SshAgentProxy(SessionAgentService gate, SshGateState state, ILogger<SshAgentProxy> log) : BackgroundService
{
    /// <summary>Key comments from the identity lists the real agent sent, by fingerprint.</summary>
    private readonly ConcurrentDictionary<string, string> _comments = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                // FirstPipeInstance: fail when another process already owns the name.
                server = new NamedPipeServerStream(SshGate.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));
            }
            catch (IOException ex) when (first)
            {
                log.LogError("ssh gate: cannot own the pipe {Pipe}: {Message}", SshGate.PipePath, ex.Message);
                return;
            }
            first = false;
            try
            {
                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }
            _ = Task.Run(() => ServeAsync(server, stoppingToken), stoppingToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream client, CancellationToken ct)
    {
        await using var _ = client;
        NamedPipeClientStream? upstream = null;
        try
        {
            if (!NativeMethods.GetNamedPipeClientProcessId(client.SafePipeHandle, out var pid) || pid == 0)
                return;
            while (await SshAgentProtocol.ReadMessageAsync(client, ct).ConfigureAwait(false) is { } request)
            {
                if (request[0] == SshAgentProtocol.SignRequest && !await AllowSignAsync((int)pid, request, ct).ConfigureAwait(false))
                {
                    await SshAgentProtocol.WriteMessageAsync(client, SshAgentProtocol.FailureMessage, ct).ConfigureAwait(false);
                    continue;
                }
                upstream ??= await ConnectUpstreamAsync(ct).ConfigureAwait(false);
                var reply = await RoundTripAsync(upstream, request, ct).ConfigureAwait(false);
                if (request[0] == SshAgentProtocol.RequestIdentities && reply[0] == SshAgentProtocol.IdentitiesAnswer)
                    Remember(reply);
                await SshAgentProtocol.WriteMessageAsync(client, reply, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            // The client left, or the real agent is not there. ssh then reports that the agent failed.
            log.LogDebug("ssh gate: connection ends: {Message}", ex.Message);
        }
        finally
        {
            if (upstream is not null)
                await upstream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> AllowSignAsync(int pid, byte[] request, CancellationToken ct)
    {
        var sign = SshAgentProtocol.ParseSign(request);
        var fingerprint = SshAgentProtocol.Fingerprint(sign.KeyBlob);
        if (!_comments.ContainsKey(fingerprint))
        {
            // A client can sign without a list first. Ask the real agent for the names once.
            await using var lookup = await ConnectUpstreamAsync(ct).ConfigureAwait(false);
            var reply = await RoundTripAsync(lookup, [SshAgentProtocol.RequestIdentities], ct).ConfigureAwait(false);
            if (reply[0] == SshAgentProtocol.IdentitiesAnswer)
                Remember(reply);
        }
        var comment = _comments.GetValueOrDefault(fingerprint);
        var keyLabel = string.IsNullOrWhiteSpace(comment) ? fingerprint : comment;

        var line = PowerShellInspector.ReadCommandLine(pid);
        var args = line is null ? [] : PowerShellInspector.SplitCommandLine(line);
        var command = SshClientCommand.Parse(args);
        if (command.User is null && SshAgentProtocol.UserOf(sign.Data) is { } user)
            command = command with { User = user };
        var path = args.Count > 0 ? args[0] : null;
        // The Approval Gate blocks until the person answers; keep the pipe thread free.
        return await Task.Run(() => gate.AuthorizeSshSign(pid, command, keyLabel, fingerprint, path), ct).ConfigureAwait(false);
    }

    private void Remember(byte[] identitiesAnswer)
    {
        foreach (var (blob, comment) in SshAgentProtocol.ParseIdentities(identitiesAnswer))
            _comments[SshAgentProtocol.Fingerprint(blob)] = comment;
    }

    private async Task<NamedPipeClientStream> ConnectUpstreamAsync(CancellationToken ct)
    {
        var name = state.Upstream.StartsWith(SshGate.PipePrefix, StringComparison.OrdinalIgnoreCase)
            ? state.Upstream[SshGate.PipePrefix.Length..]
            : state.Upstream;
        var upstream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await upstream.ConnectAsync(2000, ct).ConfigureAwait(false);
            return upstream;
        }
        catch
        {
            await upstream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<byte[]> RoundTripAsync(Stream upstream, byte[] request, CancellationToken ct)
    {
        await SshAgentProtocol.WriteMessageAsync(upstream, request, ct).ConfigureAwait(false);
        return await SshAgentProtocol.ReadMessageAsync(upstream, ct).ConfigureAwait(false)
            ?? throw new IOException("The real ssh-agent closed the pipe.");
    }
}
