using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenKO.Network.Framing;

namespace OpenKO.Network.Tcp;

/// <summary>
/// One client connection. Replaces the C++ TcpServerSocket/asio pair with an
/// async receive loop; outgoing frames are serialized through a channel so sends
/// never interleave. Packets of a single session are processed sequentially,
/// which preserves the ordering the C++ recursive-mutex design guaranteed.
/// </summary>
public sealed class KoSession : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly ILogger _logger;
    private readonly PacketFramer _framer = new();
    private readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    public int Id { get; }

    public EndPoint? RemoteEndPoint { get; }

    /// <summary>Invoked once per de-framed payload (opcode + body).</summary>
    public Func<KoSession, byte[], ValueTask>? OnPacket { get; set; }

    public event Action<KoSession>? Closed;

    public KoSession(int id, Socket socket, ILogger logger)
    {
        Id = id;
        _socket = socket;
        _logger = logger;
        RemoteEndPoint = socket.RemoteEndPoint;
    }

    /// <summary>Frames and queues a payload. Returns false for oversized payloads (like the C++ Send).</summary>
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
            _logger.LogDebug("session {Id} socket error: {Error}", Id, ex.SocketErrorCode);
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

            Closed?.Invoke(this);
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

    public void Close() => _cts.Cancel();

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
