using System.Net.Sockets;
using Convers.Host.Uplink;
using Convers.Protocol;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// A minimal TCP-backed <see cref="IUpstreamLink"/> used <em>only</em> by the interop lane to dial the
/// conversd-saupp oracle over the loopback-published convers port. It is a thin preview of W5's
/// direct-TCP provider: connect a socket, frame outbound lines with <see cref="ConversWire.FrameLine"/>,
/// and split inbound bytes on CR/LF (Latin-1) into lines. The production providers (RF/RHP + TCP) land in
/// W5; this stays in the test project so W4 touches no composition.
/// </summary>
internal sealed class TcpUpstreamLink : IUpstreamLink
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Queue<string> _pending = new();
    private byte[] _remainder = [];

    private TcpUpstreamLink(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<TcpUpstreamLink> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        return new TcpUpstreamLink(client);
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        byte[] framed = ConversWire.FrameLine(line);
        await _stream.WriteAsync(framed, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    public async Task<string?> ReceiveLineAsync(CancellationToken cancellationToken)
    {
        while (_pending.Count == 0)
        {
            var buffer = new byte[4096];
            int read;
            try
            {
                read = await _stream.ReadAsync(buffer, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                return null;
            }

            if (read == 0)
            {
                return null; // peer closed
            }

            byte[] combined = Combine(_remainder, buffer.AsSpan(0, read));
            IReadOnlyList<string> lines = ConversWire.SplitLines(combined, out _remainder);
            foreach (string l in lines)
            {
                _pending.Enqueue(l);
            }
        }

        return _pending.Dequeue();
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static byte[] Combine(byte[] head, ReadOnlySpan<byte> tail)
    {
        if (head.Length == 0)
        {
            return tail.ToArray();
        }

        var result = new byte[head.Length + tail.Length];
        head.CopyTo(result, 0);
        tail.CopyTo(result.AsSpan(head.Length));
        return result;
    }
}

/// <summary>An <see cref="IUpstreamLinkFactory"/> that dials a TCP endpoint — the interop lane's dialer.</summary>
internal sealed class TcpUpstreamLinkFactory(string host, int port) : IUpstreamLinkFactory
{
    public async Task<IUpstreamLink> ConnectAsync(CancellationToken cancellationToken) =>
        await TcpUpstreamLink.ConnectAsync(host, port, cancellationToken);
}
