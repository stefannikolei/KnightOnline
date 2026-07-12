using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenKO.Network.Framing;

namespace OpenKO.Network.Tcp;

/// <summary>
/// Outbound KO connection (port of <c>TcpClientSocket</c>): the AIServer connects
/// to Ebenezer with one such link per zone. Same wire framing as the server side;
/// sends are serialized through a channel, received frames are dispatched
/// sequentially.
/// </summary>
public sealed class KoTcpClient(ILogger logger) : IAsyncDisposable
{
    private readonly Socket _socket = new(SocketType.Stream, ProtocolType.Tcp);
    private readonly PacketFramer _framer = new();
    private readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Invoked once per de-framed payload (opcode + body).</summary>
    public Func<KoTcpClient, byte[], ValueTask>? OnPacket { get; set; }

    public bool Connected => _socket.Connected;

    public async Task ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(endPoint, cancellationToken);
        _socket.NoDelay = true;
    }

    /// <summary>Frames and queues a payload. Returns false for oversized payloads.</summary>
    public bool Send(ReadOnlySpan<byte> payload)
    {
        if (!PacketFramer.TryFrame(payload, out byte[] frame))
            return false;

        return _sendQueue.Writer.TryWrite(frame);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        Task sendTask = SendLoopAsync(linked.Token);
        try
        {
            var buffer = new byte[8192];
            while (!linked.Token.IsCancellationRequested)
            {
                int received = await _socket.ReceiveAsync(buffer, SocketFlags.None, linked.Token);
                if (received == 0)
                    break;

                _framer.Feed(buffer.AsSpan(0, received));

                while (_framer.TryReadFrame(out byte[] payload))
                {
                    if (payload.Length == 0)
                        continue;

                    if (OnPacket is { } handler)
                        await handler(this, payload);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException ex)
        {
            logger.LogWarning("client link socket error: {Error}", ex.SocketErrorCode);
        }
        finally
        {
            _sendQueue.Writer.TryComplete();
            try
            {
                await sendTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (byte[] frame in _sendQueue.Reader.ReadAllAsync(cancellationToken))
        {
            int sent = 0;
            while (sent < frame.Length)
                sent += await _socket.SendAsync(frame.AsMemory(sent), SocketFlags.None, cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _socket.Dispose();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
