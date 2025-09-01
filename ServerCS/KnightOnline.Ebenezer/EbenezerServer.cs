using KnightOnline.Shared;
using KnightOnline.Shared.Networking;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace KnightOnline.Ebenezer;

/// <summary>
/// Knight Online Game Server (Ebenezer) - handles client connections and game logic
/// </summary>
public class EbenezerServer : SocketServer<GameUser>, IDisposable
{
    private readonly EbenezerConfig _config;
    private readonly Timer _gameTimer;
    private readonly Timer _weatherTimer;
    private readonly Timer _autoSaveTimer;
    private readonly ConcurrentDictionary<int, GameUser> _users = new();
    private Socket? _databaseSocket;
    private volatile bool _disposed = false;
    private int _nextUserIndex = 1;

    public EbenezerServer(EbenezerConfig config) : base(config.Port, config.MaxUsers)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        // Setup timers
        _gameTimer = new Timer(OnGameTimer, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _weatherTimer = new Timer(OnWeatherTimer, null, 
            TimeSpan.FromSeconds(_config.WeatherUpdateInterval), 
            TimeSpan.FromSeconds(_config.WeatherUpdateInterval));
        _autoSaveTimer = new Timer(OnAutoSaveTimer, null,
            TimeSpan.FromSeconds(_config.AutoSaveInterval),
            TimeSpan.FromSeconds(_config.AutoSaveInterval));

        Console.WriteLine($"Ebenezer server initialized:");
        Console.WriteLine($"  Port: {_config.Port}");
        Console.WriteLine($"  Max Users: {_config.MaxUsers}");
        Console.WriteLine($"  Database Server: {_config.DatabaseServer.Host}:{_config.DatabaseServer.Port}");
        Console.WriteLine($"  Server Name: {_config.ServerName}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Connect to database server
        await ConnectToDatabaseServerAsync();

        // Start the socket server
        await StartListeningAsync(cancellationToken);
    }

    private async Task ConnectToDatabaseServerAsync()
    {
        try
        {
            Console.WriteLine("Connecting to database server...");
            _databaseSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await _databaseSocket.ConnectAsync(_config.DatabaseServer.Host, _config.DatabaseServer.Port);
            Console.WriteLine("Connected to database server successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to database server: {ex.Message}");
            throw;
        }
    }

    protected override GameUser CreateConnection(Socket socket)
    {
        var userId = Interlocked.Increment(ref _nextUserIndex);
        var user = new GameUser(socket, userId, _config, this);
        _users[userId] = user;
        return user;
    }

    protected override void OnClientConnected(GameUser user)
    {
        if (_config.EnableDebugLogging)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Game client connected: {user.RemoteEndPoint} (ID: {user.UserId})");
        }
    }

    protected override void OnClientDisconnected(GameUser user)
    {
        _users.TryRemove(user.UserId, out _);
        
        if (_config.EnableDebugLogging)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Game client disconnected: {user.RemoteEndPoint} (ID: {user.UserId})");
        }
    }

    public async Task SendToDatabaseAsync(Packet packet)
    {
        if (_databaseSocket?.Connected == true)
        {
            try
            {
                var data = packet.ToArray();
                await _databaseSocket.SendAsync(data, SocketFlags.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending to database: {ex.Message}");
                // TODO: Implement reconnection logic
            }
        }
    }

    public void BroadcastPacket(Packet packet, GameUser? excludeUser = null)
    {
        foreach (var user in _users.Values)
        {
            if (user != excludeUser && user.IsConnected)
            {
                user.Send(packet);
            }
        }
    }

    public GameUser? GetUser(int userId)
    {
        _users.TryGetValue(userId, out var user);
        return user;
    }

    public IEnumerable<GameUser> GetAllUsers()
    {
        return _users.Values.Where(u => u.IsConnected);
    }

    public int GetUserCount()
    {
        return _users.Count(kvp => kvp.Value.IsConnected);
    }

    private void OnGameTimer(object? state)
    {
        if (_disposed) return;

        try
        {
            // Send time update to all users
            var timePacket = new Packet(Opcodes.WIZ_TIME);
            timePacket.WriteInt32((int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond));
            BroadcastPacket(timePacket);

            // Update user positions and states
            foreach (var user in GetAllUsers())
            {
                user.OnGameTick();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Game timer error: {ex.Message}");
        }
    }

    private void OnWeatherTimer(object? state)
    {
        if (_disposed) return;

        try
        {
            // Generate random weather
            var weather = Random.Shared.Next(1, 5); // 1-4 weather types
            var weatherPacket = new Packet(Opcodes.WIZ_WEATHER);
            weatherPacket.WriteByte((byte)weather);
            BroadcastPacket(weatherPacket);

            if (_config.EnableDebugLogging)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Weather changed to: {weather}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Weather timer error: {ex.Message}");
        }
    }

    private void OnAutoSaveTimer(object? state)
    {
        if (_disposed) return;

        try
        {
            // Auto-save all user data
            foreach (var user in GetAllUsers())
            {
                if (user.ShouldAutoSave())
                {
                    user.SaveUserData();
                }
            }

            if (_config.EnableDebugLogging)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Auto-save completed for active users.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auto-save timer error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _gameTimer?.Dispose();
            _weatherTimer?.Dispose();
            _autoSaveTimer?.Dispose();
            _databaseSocket?.Close();
            Stop(); // Stop the socket server
        }
    }
}