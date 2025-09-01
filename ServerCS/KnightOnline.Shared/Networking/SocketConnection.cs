using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KnightOnline.Shared;

namespace KnightOnline.Shared.Networking;

/// <summary>
/// Base class for socket connections in the Knight Online server
/// </summary>
public abstract class SocketConnection : IDisposable
{
    protected Socket? _socket;
    protected CircularBuffer _receiveBuffer;
    protected readonly object _sendLock = new object();
    protected volatile bool _disposed = false;

    protected SocketConnection()
    {
        _receiveBuffer = new CircularBuffer(Types.SOCKET_BUFF_SIZE);
    }

    public bool IsConnected => _socket?.Connected ?? false;

    public abstract void Initialize();
    public abstract void OnReceive(byte[] data, int length);
    public abstract void OnDisconnect();

    public virtual async Task<bool> SendAsync(byte[] data)
    {
        if (_disposed || _socket == null || !_socket.Connected)
            return false;

        try
        {
            lock (_sendLock)
            {
                int sent = 0;
                while (sent < data.Length)
                {
                    int bytesToSend = Math.Min(data.Length - sent, Types.SOCKET_BUFF_SIZE);
                    int sentNow = _socket.Send(data, sent, bytesToSend, SocketFlags.None);
                    if (sentNow <= 0)
                        return false;
                    sent += sentNow;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Send error: {ex.Message}");
            return false;
        }
    }

    public virtual bool Send(byte[] data)
    {
        if (_disposed || _socket == null || !_socket.Connected)
            return false;

        try
        {
            lock (_sendLock)
            {
                int sent = 0;
                while (sent < data.Length)
                {
                    int bytesToSend = Math.Min(data.Length - sent, Types.SOCKET_BUFF_SIZE);
                    int sentNow = _socket.Send(data, sent, bytesToSend, SocketFlags.None);
                    if (sentNow <= 0)
                        return false;
                    sent += sentNow;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Send error: {ex.Message}");
            return false;
        }
    }

    public virtual void Close()
    {
        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
            _socket?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Close error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Close();
            _socket?.Dispose();
        }
    }
}