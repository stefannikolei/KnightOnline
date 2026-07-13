using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The client-side connection (CAPISocket): one TCP link that carries the
/// unencrypted login-server traffic and, after <see cref="EnableCryption"/> from
/// the WIZ_VERSION_CHECK reply, the encrypted Ebenezer traffic. Frames/decrypts
/// through <see cref="GameClientSocketCore"/> and unwraps WIZ_COMPRESS_PACKET
/// before dispatching each payload (opcode at byte 0) to <see cref="OnPacket"/>.
/// </summary>
public sealed class KoClientConnection : IAsyncDisposable
{
    private readonly Socket _socket = new(SocketType.Stream, ProtocolType.Tcp);
    private readonly GameClientSocketCore _core = new();
    private readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Invoked per decoded payload (after decrypt + decompress).</summary>
    public Func<KoClientConnection, byte[], ValueTask>? OnPacket { get; set; }

    /// <summary>Raised when the link drops or a protocol violation closes it.</summary>
    public Action? OnClosed { get; set; }

    public bool CryptionEnabled => _core.CryptionEnabled;

    public async Task ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(endPoint, cancellationToken);
        _socket.NoDelay = true;
    }

    /// <summary>CAPISocket::InitCrypt — switch on encryption with the server's public key.</summary>
    public void EnableCryption(ulong publicKey) => _core.InitCrypt(publicKey);

    /// <summary>Frames (and encrypts when keyed) a payload. False if oversized.</summary>
    public bool Send(ReadOnlySpan<byte> payload)
    {
        byte[]? frame = _core.BuildFrame(payload);
        return frame != null && _sendQueue.Writer.TryWrite(frame);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        Task sendTask = SendLoopAsync(linked.Token);
        bool closedByProtocol = false;

        try
        {
            var buffer = new byte[8192];
            while (!linked.Token.IsCancellationRequested)
            {
                int received = await _socket.ReceiveAsync(buffer, SocketFlags.None, linked.Token);
                if (received == 0)
                    break;

                _core.Feed(buffer.AsSpan(0, received));

                while (true)
                {
                    ClientFrameResult result = _core.TryReadPacket(out byte[] payload);
                    if (result == ClientFrameResult.NeedMore)
                        break;
                    if (result == ClientFrameResult.Close)
                    {
                        closedByProtocol = true;
                        break;
                    }

                    await DispatchAsync(payload);
                }

                if (closedByProtocol)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
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

            OnClosed?.Invoke();
        }
    }

    private async ValueTask DispatchAsync(byte[] payload)
    {
        if (payload.Length == 0)
            return;

        // Unwrap a compressed packet and dispatch the inner one (C++ recursion).
        if (GameClientSocketCore.TryDecompress(payload, out byte[] inner))
            payload = inner;

        if (OnPacket is { } handler)
            await handler(this, payload);
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
