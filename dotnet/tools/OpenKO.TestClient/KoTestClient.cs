using System.Net;
using System.Net.Sockets;
using OpenKO.Network.Framing;

namespace OpenKO.TestClient;

/// <summary>
/// Minimal scripted protocol client: sends framed payloads and reads framed
/// responses. Used by the integration tests and the parity harness.
/// </summary>
public sealed class KoTestClient : IDisposable
{
    private readonly Socket _socket = new(SocketType.Stream, ProtocolType.Tcp);
    private readonly PacketFramer _framer = new();
    private readonly byte[] _receiveBuffer = new byte[8192];

    public async Task ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(endPoint, cancellationToken);
        _socket.NoDelay = true;
    }

    public async Task SendPayloadAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        if (!PacketFramer.TryFrame(payload, out byte[] frame))
            throw new ArgumentException("payload too large", nameof(payload));

        int sent = 0;
        while (sent < frame.Length)
            sent += await _socket.SendAsync(frame.AsMemory(sent), SocketFlags.None, cancellationToken);
    }

    /// <summary>Reads until one complete frame is available; returns its payload.</summary>
    public async Task<byte[]> ReceivePayloadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_framer.TryReadFrame(out byte[] payload))
                return payload;

            int received = await _socket.ReceiveAsync(_receiveBuffer, SocketFlags.None, cancellationToken);
            if (received == 0)
                throw new IOException("connection closed");

            _framer.Feed(_receiveBuffer.AsSpan(0, received));
        }
    }

    public async Task<byte[]> RequestAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        await SendPayloadAsync(payload, cancellationToken);
        return await ReceivePayloadAsync(cancellationToken);
    }

    public void Dispose() => _socket.Dispose();
}
