using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CmdWarden.Contracts.Ssh;

/// <summary>
/// The parts of the ssh-agent protocol (draft-miller-ssh-agent) that the ssh gate reads (#39).
/// A message is a uint32 length and a body; the first body byte is the type.
/// </summary>
public static class SshAgentProtocol
{
    public const byte Failure = 5;
    public const byte RequestIdentities = 11;
    public const byte IdentitiesAnswer = 12;
    public const byte SignRequest = 13;
    public const int MaxMessage = 256 * 1024;

    public static byte[] FailureMessage => [Failure];

    /// <summary>One message body, or null at a clean end of stream.</summary>
    public static async Task<byte[]?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
            return null;
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > MaxMessage)
            throw new InvalidDataException($"ssh-agent message length {length} is out of range.");
        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
            throw new EndOfStreamException("ssh-agent message ends early.");
        return body;
    }

    public static async Task WriteMessageAsync(Stream stream, byte[] body, CancellationToken ct)
    {
        var message = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(message, (uint)body.Length);
        body.CopyTo(message, 4);
        await stream.WriteAsync(message, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public sealed record Sign(byte[] KeyBlob, byte[] Data, uint Flags);

    public static Sign ParseSign(byte[] body)
    {
        var reader = new Reader(body, 1);
        return new Sign(reader.Bytes(), reader.Bytes(), reader.UInt32());
    }

    public static IReadOnlyList<(byte[] Blob, string Comment)> ParseIdentities(byte[] body)
    {
        var reader = new Reader(body, 1);
        var count = reader.UInt32();
        var keys = new List<(byte[], string)>();
        for (var i = 0; i < count && i < 1024; i++)
            keys.Add((reader.Bytes(), Encoding.UTF8.GetString(reader.Bytes())));
        return keys;
    }

    /// <summary>The key fingerprint as ssh-keygen -l prints it.</summary>
    public static string Fingerprint(byte[] keyBlob) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBlob)).TrimEnd('=');

    /// <summary>The user of an SSH_MSG_USERAUTH_REQUEST in the data to sign, or null for other data.</summary>
    public static string? UserOf(byte[] data)
    {
        try
        {
            var reader = new Reader(data, 0);
            reader.Bytes();
            if (reader.Byte() != 50)
                return null;
            return Encoding.UTF8.GetString(reader.Bytes());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static string KeyType(byte[] keyBlob)
    {
        try
        {
            return Encoding.ASCII.GetString(new Reader(keyBlob, 0).Bytes());
        }
        catch (InvalidDataException)
        {
            return "unknown";
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
                return read == 0 ? false : throw new EndOfStreamException("ssh-agent message ends early.");
            read += n;
        }
        return true;
    }

    private sealed class Reader(byte[] data, int position)
    {
        private int _position = position;

        public byte Byte() => _position < data.Length ? data[_position++] : throw new InvalidDataException("ssh-agent data ends early.");

        public uint UInt32()
        {
            if (_position + 4 > data.Length)
                throw new InvalidDataException("ssh-agent data ends early.");
            var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(_position));
            _position += 4;
            return value;
        }

        public byte[] Bytes()
        {
            var length = UInt32();
            if (length > data.Length - _position)
                throw new InvalidDataException("ssh-agent string is longer than the message.");
            var value = data.AsSpan(_position, (int)length).ToArray();
            _position += (int)length;
            return value;
        }
    }
}
