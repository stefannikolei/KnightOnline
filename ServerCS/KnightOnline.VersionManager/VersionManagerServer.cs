using System;
using System.IO;
using System.Text.Json;
using KnightOnline.Shared;
using KnightOnline.Shared.Networking;

namespace KnightOnline.VersionManager;

/// <summary>
/// Configuration for the Version Manager server
/// </summary>
public class VersionManagerConfig
{
    public int Port { get; set; } = 15100;
    public int MaxUsers { get; set; } = 3000;
    public string FtpUrl { get; set; } = "";
    public string FtpPath { get; set; } = "";
    public int LastVersion { get; set; } = 1;
    public string News { get; set; } = "Welcome to Knight Online!";
}

/// <summary>
/// User connection for the Version Manager server
/// </summary>
public class VersionManagerUser : SocketConnection
{
    private readonly VersionManagerServer _server;

    public VersionManagerUser(VersionManagerServer server)
    {
        _server = server;
    }

    public override void Initialize()
    {
        Console.WriteLine($"VersionManager: New client connected");
    }

    public override void OnReceive(byte[] data, int length)
    {
        try
        {
            // Simple packet parsing - in the real implementation this would be more robust
            if (length < 1)
                return;

            byte opcode = data[0];
            switch (opcode)
            {
                case Opcodes.LS_VERSION_REQ:
                    HandleVersionRequest();
                    break;
                case Opcodes.LS_DOWNLOADINFO_REQ:
                    HandleDownloadInfoRequest();
                    break;
                case Opcodes.LS_NEWS:
                    HandleNewsRequest();
                    break;
                default:
                    Console.WriteLine($"VersionManager: Unknown opcode {opcode:X2}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"VersionManager: Error processing packet: {ex.Message}");
        }
    }

    public override void OnDisconnect()
    {
        Console.WriteLine($"VersionManager: Client disconnected");
    }

    private void HandleVersionRequest()
    {
        var packet = new Packet(Opcodes.LS_VERSION_REQ);
        packet.Append((short)_server.Config.LastVersion);
        
        Send(packet.Contents);
        Console.WriteLine($"VersionManager: Sent version {_server.Config.LastVersion} to client");
    }

    private void HandleDownloadInfoRequest()
    {
        var packet = new Packet(Opcodes.LS_DOWNLOADINFO_REQ);
        packet.AppendString(_server.Config.FtpUrl, 256);
        packet.AppendString(_server.Config.FtpPath, 256);
        
        Send(packet.Contents);
        Console.WriteLine($"VersionManager: Sent download info to client");
    }

    private void HandleNewsRequest()
    {
        var packet = new Packet(Opcodes.LS_NEWS);
        packet.Append((short)_server.Config.News.Length);
        packet.Append(System.Text.Encoding.UTF8.GetBytes(_server.Config.News));
        
        Send(packet.Contents);
        Console.WriteLine($"VersionManager: Sent news to client");
    }
}

/// <summary>
/// C# port of the Version Manager server
/// </summary>
public class VersionManagerServer
{
    private SocketServer<VersionManagerUser>? _socketServer;
    private VersionManagerConfig _config;

    public VersionManagerConfig Config => _config;

    public VersionManagerServer()
    {
        _config = LoadConfiguration();
    }

    public bool Start()
    {
        try
        {
            Console.WriteLine($"Starting Version Manager Server...");
            Console.WriteLine($"Port: {_config.Port}");
            Console.WriteLine($"Max Users: {_config.MaxUsers}");
            Console.WriteLine($"Current Version: {_config.LastVersion}");
            Console.WriteLine($"FTP URL: {_config.FtpUrl}");
            Console.WriteLine($"FTP Path: {_config.FtpPath}");

            _socketServer = new SocketServer<VersionManagerUser>(_config.MaxUsers, () => new VersionManagerUser(this));
            
            // Pass server instance to users via a custom factory
            var originalServer = _socketServer;
            
            if (_socketServer.StartListening(_config.Port))
            {
                Console.WriteLine($"Version Manager Server started successfully on port {_config.Port}");
                return true;
            }
            else
            {
                Console.WriteLine("Failed to start Version Manager Server");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting Version Manager Server: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            Console.WriteLine("Stopping Version Manager Server...");
            _socketServer?.Dispose();
            _socketServer = null;
            Console.WriteLine("Version Manager Server stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping Version Manager Server: {ex.Message}");
        }
    }

    private VersionManagerConfig LoadConfiguration()
    {
        var configPath = "versionmanager_config.json";
        
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<VersionManagerConfig>(json);
                return config ?? new VersionManagerConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                Console.WriteLine("Using default configuration");
            }
        }
        else
        {
            Console.WriteLine("Configuration file not found, creating default configuration");
            var defaultConfig = new VersionManagerConfig();
            SaveConfiguration(defaultConfig);
            return defaultConfig;
        }
        
        return new VersionManagerConfig();
    }

    private void SaveConfiguration(VersionManagerConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("versionmanager_config.json", json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving configuration: {ex.Message}");
        }
    }
}