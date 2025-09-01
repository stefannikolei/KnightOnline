using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KnightOnline.Shared;

namespace KnightOnline.Shared.Networking;

/// <summary>
/// C# port of the C++ IOCPort class for managing socket connections
/// </summary>
public class SocketServer<T> : IDisposable where T : SocketConnection
{
    private Socket? _listenSocket;
    private readonly object _lock = new object();
    private readonly ConcurrentDictionary<int, T> _connections = new();
    private volatile bool _disposed = false;
    private CancellationTokenSource? _cancellationTokenSource;
    private int _nextConnectionId = 1;
    private int _maxConnections;
    private readonly Func<T> _connectionFactory;

    public int Port { get; private set; }
    public bool IsListening => _listenSocket?.IsBound ?? false;

    public SocketServer(int maxConnections = 1000, Func<T>? connectionFactory = null)
    {
        _maxConnections = maxConnections;
        _connectionFactory = connectionFactory ?? (() => (T)Activator.CreateInstance(typeof(T))!);
    }

    public bool StartListening(int port)
    {
        if (_disposed || IsListening)
            return false;

        try
        {
            Port = port;
            _cancellationTokenSource = new CancellationTokenSource();
            
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            
            var localEndPoint = new IPEndPoint(IPAddress.Any, port);
            _listenSocket.Bind(localEndPoint);
            _listenSocket.Listen(100);

            Console.WriteLine($"SocketServer: Listening on port {port}");

            // Start accepting connections
            _ = Task.Run(AcceptConnectionsAsync, _cancellationTokenSource.Token);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start listening on port {port}: {ex.Message}");
            return false;
        }
    }

    private async Task AcceptConnectionsAsync()
    {
        if (_listenSocket == null || _cancellationTokenSource == null)
            return;

        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested && IsListening)
            {
                try
                {
                    var clientSocket = await AcceptAsync(_listenSocket, _cancellationTokenSource.Token);
                    if (clientSocket != null)
                    {
                        _ = Task.Run(() => HandleNewConnection(clientSocket), _cancellationTokenSource.Token);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Socket was disposed, exit loop
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accepting connection: {ex.Message}");
                    await Task.Delay(100, _cancellationTokenSource.Token); // Small delay to prevent tight loop
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
    }

    private async Task<Socket?> AcceptAsync(Socket listenSocket, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Factory.FromAsync(listenSocket.BeginAccept, listenSocket.EndAccept, null);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void HandleNewConnection(Socket clientSocket)
    {
        if (_connections.Count >= _maxConnections)
        {
            Console.WriteLine("Maximum connections reached, rejecting new connection");
            clientSocket.Close();
            return;
        }

        try
        {
            var connection = _connectionFactory();
            var connectionId = Interlocked.Increment(ref _nextConnectionId);
            
            // Set up the connection using reflection to access protected field
            var socketField = typeof(SocketConnection).GetField("_socket", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            socketField?.SetValue(connection, clientSocket);
            
            connection.Initialize();
            
            _connections[connectionId] = connection;
            Console.WriteLine($"New connection accepted: {connectionId} (Total: {_connections.Count})");

            // Start receiving data
            _ = Task.Run(() => HandleConnection(connection, connectionId), _cancellationTokenSource?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling new connection: {ex.Message}");
            clientSocket.Close();
        }
    }

    private async Task HandleConnection(T connection, int connectionId)
    {
        var buffer = new byte[Types.SOCKET_BUFF_SIZE];
        
        try
        {
            var socketField = typeof(SocketConnection).GetField("_socket", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var socket = socketField?.GetValue(connection) as Socket;

            if (socket == null)
                return;

            while (connection.IsConnected && !_disposed)
            {
                try
                {
                    int received = await ReceiveAsync(socket, buffer);
                    if (received > 0)
                    {
                        connection.OnReceive(buffer, received);
                    }
                    else
                    {
                        break; // Connection closed
                    }
                }
                catch (SocketException)
                {
                    break; // Connection error
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in connection {connectionId}: {ex.Message}");
        }
        finally
        {
            // Clean up connection
            connection.OnDisconnect();
            _connections.TryRemove(connectionId, out _);
            connection.Dispose();
            Console.WriteLine($"Connection {connectionId} disconnected (Total: {_connections.Count})");
        }
    }

    private async Task<int> ReceiveAsync(Socket socket, byte[] buffer)
    {
        try
        {
            return await Task.Factory.FromAsync<int>(
                (callback, state) => socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, callback, state),
                socket.EndReceive,
                null);
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    public void StopListening()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            
            // Close all connections
            foreach (var connection in _connections.Values)
            {
                connection.Dispose();
            }
            _connections.Clear();

            _listenSocket?.Close();
            _listenSocket?.Dispose();
            _listenSocket = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping server: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            StopListening();
            _cancellationTokenSource?.Dispose();
        }
    }
}