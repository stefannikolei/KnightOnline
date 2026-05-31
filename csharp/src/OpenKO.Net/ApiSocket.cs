using System.Net.Sockets;
using OpenKO.Common;

namespace OpenKO.Net;

/// <summary>
/// Cross-platform port of the C++ <c>CAPISocket</c> (Client/WarFare/APISocket.cpp).
///
/// The original used Winsock with <c>WSAAsyncSelect</c> to post socket events to a window message
/// pump. Here we use <see cref="System.Net.Sockets.Socket"/> with an async receive loop and surface
/// decoded packets through the <see cref="PacketReceived"/> event (or the <see cref="Receive"/>
/// polling queue), so no Win32 message pump is required.
///
/// Wire framing and the optional JvCryption layer are preserved bit-for-bit by
/// <see cref="PacketFraming"/>, keeping this compatible with the official server.
/// </summary>
public sealed class ApiSocket : IDisposable
{
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    private readonly byte[] _accum = new byte[PacketFraming.ReceiveBufferSize];
    private int _accumLen;

    private readonly object _queueLock = new();
    private readonly Queue<Packet> _recvQueue = new();

    private JvCryption? _crypto;
    private uint _sendCounter;

    /// <summary>Raised on the receive loop's thread for each decoded packet.</summary>
    public event Action<Packet>? PacketReceived;

    /// <summary>Raised when the connection is closed or fails.</summary>
    public event Action<Exception?>? Disconnected;

    public bool IsConnected => _socket is { Connected: true };

    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }

    /// <summary>Enables/disables the JvCryption layer. Pass null (or call <see cref="DisableCrypt"/>) to disable.</summary>
    public void EnableCrypt(ulong publicKey)
    {
        _crypto = new JvCryption { PublicKey = publicKey };
        _crypto.Init();
        _sendCounter = 0;
    }

    public void DisableCrypt()
    {
        _crypto = null;
        _sendCounter = 0;
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        Disconnect();

        try
        {
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveBufferSize = PacketFraming.ReceiveBufferSize,
                NoDelay = true,
            };

            await _socket.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _socket?.Dispose();
            _socket = null;
            return false;
        }

        Host = host;
        Port = port;

        _cts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        return true;
    }

    /// <summary>Sends a packet (its full contents become the application payload).</summary>
    public void Send(Packet packet) => Send(packet.Contents);

    /// <summary>Sends a raw application payload, applying framing (and crypto, if enabled).</summary>
    public void Send(ReadOnlySpan<byte> payload)
    {
        if (_socket is not { Connected: true })
            return;

        byte[] frame = PacketFraming.BuildFrame(payload, _crypto, ref _sendCounter);

        int sent = 0;
        while (sent < frame.Length)
        {
            int n = _socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
            if (n <= 0)
                break;
            sent += n;
        }
    }

    /// <summary>Polls for the next decoded packet (port of the original receive queue), or null.</summary>
    public Packet? Receive()
    {
        lock (_queueLock)
        {
            return _recvQueue.Count > 0 ? _recvQueue.Dequeue() : null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var rented = new byte[PacketFraming.ReceiveBufferSize];
        Exception? error = null;

        try
        {
            while (!token.IsCancellationRequested && _socket != null)
            {
                int read = await _socket.ReceiveAsync(rented, SocketFlags.None, token).ConfigureAwait(false);
                if (read <= 0)
                    break; // graceful close

                Append(rented.AsSpan(0, read));
                DrainFrames();
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            error = ex;
        }

        Disconnected?.Invoke(error);
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        if (_accumLen + data.Length > _accum.Length)
        {
            // Should not happen with well-formed traffic, but guard against runaway growth.
            _accumLen = 0;
        }

        data.CopyTo(_accum.AsSpan(_accumLen));
        _accumLen += data.Length;
    }

    private void DrainFrames()
    {
        int offset = 0;
        while (true)
        {
            ReadOnlySpan<byte> window = _accum.AsSpan(offset, _accumLen - offset);
            if (window.Length < 7)
                break;

            if (PacketFraming.TryParseFrame(window, _crypto, out byte[] payload, out int consumed))
            {
                offset += consumed;
                var pkt = new Packet();
                pkt.SetContents(payload);
                EnqueueAndNotify(pkt);
            }
            else if (window.Length >= 2 && (window[0] != 0xAA || window[1] != 0x55))
            {
                // Resynchronise: drop a byte and look for the next header.
                offset += 1;
            }
            else
            {
                break; // valid header but incomplete frame — wait for more data
            }
        }

        // Compact any unconsumed tail to the front of the accumulation buffer.
        if (offset > 0)
        {
            int remaining = _accumLen - offset;
            if (remaining > 0)
                _accum.AsSpan(offset, remaining).CopyTo(_accum);
            _accumLen = remaining;
        }
    }

    private void EnqueueAndNotify(Packet pkt)
    {
        lock (_queueLock)
        {
            _recvQueue.Enqueue(pkt);
        }

        PacketReceived?.Invoke(pkt);
    }

    public void Disconnect()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            if (_socket is { Connected: true })
                _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }

        _socket?.Dispose();
        _socket = null;

        _accumLen = 0;
        DisableCrypt();

        lock (_queueLock)
        {
            _recvQueue.Clear();
        }
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }
}
