using KnightOnline.Shared;
using KnightOnline.Shared.Database;
using KnightOnline.Shared.Networking;
using System.Text.Json;

namespace KnightOnline.Aujard;

/// <summary>
/// Main entry point for the Knight Online Database Server (Aujard)
/// </summary>
public class Program
{
    private static AujardServer? _server;
    private static readonly CancellationTokenSource _cancellationTokenSource = new();

    public static async Task Main(string[] args)
    {
        Console.Title = "Knight Online - Database Server (Aujard)";
        Console.WriteLine("Knight Online Database Server (Aujard) v1.0");
        Console.WriteLine("Cross-platform C# implementation");
        Console.WriteLine("==========================================");

        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutdown requested...");
            _cancellationTokenSource.Cancel();
        };

        try
        {
            // Load configuration
            var config = LoadConfiguration();
            
            // Create and start the server
            _server = new AujardServer(config);
            await _server.StartAsync(_cancellationTokenSource.Token);

            Console.WriteLine($"Database server started on port {config.Port}");
            Console.WriteLine("Press Ctrl+C to shutdown...");

            // Wait for cancellation
            try
            {
                await Task.Delay(-1, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            if (_server != null)
            {
                Console.WriteLine("Stopping database server...");
                await _server.StopAsync();
                _server.Dispose();
            }
            Console.WriteLine("Database server stopped.");
        }
    }

    private static AujardConfig LoadConfiguration()
    {
        const string configFile = "aujard_config.json";
        
        if (!File.Exists(configFile))
        {
            Console.WriteLine($"Configuration file '{configFile}' not found. Creating default configuration...");
            var defaultConfig = new AujardConfig();
            var defaultJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, defaultJson);
            Console.WriteLine($"Default configuration created in '{configFile}'. Please edit it with your database settings.");
        }

        try
        {
            var json = File.ReadAllText(configFile);
            var config = JsonSerializer.Deserialize<AujardConfig>(json);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration");
            }
            
            Console.WriteLine($"Configuration loaded from '{configFile}'");
            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading configuration: {ex.Message}");
            Console.WriteLine("Using default configuration...");
            return new AujardConfig();
        }
    }
}
