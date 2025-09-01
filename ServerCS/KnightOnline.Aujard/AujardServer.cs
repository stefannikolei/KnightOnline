using KnightOnline.Shared;
using KnightOnline.Shared.Database;
using KnightOnline.Shared.Networking;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace KnightOnline.Aujard;

/// <summary>
/// Knight Online Database Server (Aujard) - handles user authentication, character data, knights clans
/// </summary>
public class AujardServer : SocketServer<AujardConnection>, IDisposable
{
    private readonly AujardConfig _config;
    private readonly DatabaseManager _database;
    private readonly Timer _autoSaveTimer;
    private readonly ConcurrentDictionary<string, DateTime> _lastSaveTime = new();
    private volatile bool _disposed = false;

    public AujardServer(AujardConfig config) : base(config.Port, config.MaxConnections)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _database = new DatabaseManager(config.Database);
        
        // Setup auto-save timer
        _autoSaveTimer = new Timer(OnAutoSaveTimer, null, 
            TimeSpan.FromMilliseconds(_config.AutoSaveInterval), 
            TimeSpan.FromMilliseconds(_config.AutoSaveInterval));

        Console.WriteLine($"Aujard server initialized:");
        Console.WriteLine($"  Port: {_config.Port}");
        Console.WriteLine($"  Max Connections: {_config.MaxConnections}");
        Console.WriteLine($"  Database: {_config.Database.Server}/{_config.Database.Database}");
        Console.WriteLine($"  Auto-save interval: {_config.AutoSaveInterval}ms");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Test database connection before starting
        Console.WriteLine("Testing database connection...");
        if (!await _database.TestConnectionAsync())
        {
            throw new InvalidOperationException("Failed to connect to database. Please check your configuration.");
        }
        Console.WriteLine("Database connection successful.");

        // Start the socket server
        await StartListeningAsync(cancellationToken);
    }

    protected override AujardConnection CreateConnection(Socket socket)
    {
        return new AujardConnection(socket, _database, _config);
    }

    protected override void OnClientConnected(AujardConnection connection)
    {
        if (_config.EnableDebugLogging)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Database client connected: {connection.RemoteEndPoint}");
        }
    }

    protected override void OnClientDisconnected(AujardConnection connection)
    {
        if (_config.EnableDebugLogging)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Database client disconnected: {connection.RemoteEndPoint}");
        }
    }

    private void OnAutoSaveTimer(object? state)
    {
        if (_disposed) return;

        try
        {
            // Auto-save logic would go here
            // For now, just log that the timer fired
            if (_config.EnableDebugLogging)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Auto-save timer fired.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auto-save timer error: {ex.Message}");
        }
    }

    public void UpdateLastSaveTime(string accountId)
    {
        _lastSaveTime[accountId] = DateTime.UtcNow;
    }

    public bool ShouldAutoSave(string accountId)
    {
        if (!_lastSaveTime.TryGetValue(accountId, out var lastSave))
            return true;

        return (DateTime.UtcNow - lastSave).TotalMilliseconds >= _config.AutoSaveInterval;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _autoSaveTimer?.Dispose();
            _database?.Dispose();
            Stop(); // Stop the socket server
        }
    }
}