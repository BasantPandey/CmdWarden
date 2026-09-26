using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using CmdWarden.Contracts.Ssh;

namespace CmdWarden.Tests;

/// <summary>
/// A real ssh-agent on a named pipe with one ecdsa-sha2-nistp256 key, for the ssh gate tests.
/// It lists the key and signs with it; every other request gets SSH_AGENT_FAILURE.
/// </summary>
internal sealed class FakeSshAgent : IAsyncDisposable
{
    private const string KeyType = "ecdsa-sha2-nistp256";
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _signs;

    public string PipeName { get; } = "cw-fake-ssh-agent-" + Guid.NewGuid().ToString("N");
    public string PipePath => SshGate.PipePrefix + PipeName;
    public string Comment { get; } = "test@cmdwarden";
    public byte[] KeyBlob { get; }
    public int Signs => Volatile.Read(ref _signs);

    /// <summary>The line for a .pub file.</summary>
    public string PublicKeyLine => $"{KeyType} {Convert.ToBase64String(KeyBlob)} {Comment}";

    public FakeSshAgent()
    {
        var q = _key.ExportParameters(false).Q;
        KeyBlob = Concat(Str(KeyType), Str("nistp256"), Str([0x04, .. q.X!, .. q.Y!]));
        _loop = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }
            _ = Task.Run(() => ServeAsync(server));
        }
    }

    private async Task ServeAsync(NamedPipeServerStream client)
    {
        await using var _ = client;
        try
        {
            while (await SshAgentProtocol.ReadMessageAsync(client, _stop.Token) is { } request)
                await SshAgentProtocol.WriteMessageAsync(client, Reply(request), _stop.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException)
        {
            // The client left.
        }
    }

    private byte[] Reply(byte[] request)
    {
        if (request[0] == SshAgentProtocol.RequestIdentities)
            return Concat([SshAgentProtocol.IdentitiesAnswer], UInt32(1), Str(KeyBlob), Str(Comment));
        if (request[0] != SshAgentProtocol.SignRequest)
            return SshAgentProtocol.FailureMessage;
        var sign = SshAgentProtocol.ParseSign(request);
        if (!sign.KeyBlob.AsSpan().SequenceEqual(KeyBlob))
            return SshAgentProtocol.FailureMessage;
        Interlocked.Increment(ref _signs);
        var rs = _key.SignData(sign.Data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var signature = Concat(Str(KeyType), Str(Concat(MpInt(rs[..32]), MpInt(rs[32..]))));
        return Concat([14], Str(signature));
    }

    private static byte[] MpInt(byte[] value)
    {
        var trimmed = value.SkipWhile(b => b == 0).ToArray();
        return Str(trimmed.Length > 0 && trimmed[0] >= 0x80 ? [0, .. trimmed] : trimmed);
    }

    private static byte[] Str(string text) => Str(Encoding.ASCII.GetBytes(text));

    private static byte[] Str(byte[] data) => Concat(UInt32((uint)data.Length), data);

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try { await _loop; } catch (OperationCanceledException) { }
        _key.Dispose();
        _stop.Dispose();
    }
}
