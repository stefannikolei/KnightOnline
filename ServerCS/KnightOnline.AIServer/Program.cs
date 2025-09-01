using KnightOnline.Shared;
using KnightOnline.Shared.Networking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace KnightOnline.AIServer;

/// <summary>
/// Main entry point for the Knight Online AI Server
/// </summary>
public class Program
{
    private static AIServerManager? _serverManager;
    private static readonly CancellationTokenSource _cancellationTokenSource = new();

    public static async Task Main(string[] args)
    {
        Console.Title = "Knight Online - AI Server";
        Console.WriteLine("Knight Online AI Server v1.0");
        Console.WriteLine("Cross-platform C# implementation");
        Console.WriteLine("==================================");

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
            
            // Create and start the server manager
            _serverManager = new AIServerManager(config);
            await _serverManager.StartAsync(_cancellationTokenSource.Token);

            Console.WriteLine("AI Server started successfully");
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
            if (_serverManager != null)
            {
                Console.WriteLine("Stopping AI Server...");
                await _serverManager.StopAsync();
                _serverManager.Dispose();
            }
            Console.WriteLine("AI Server stopped.");
        }
    }

    private static AIServerConfig LoadConfiguration()
    {
        const string configFile = "aiserver_config.json";
        
        if (!File.Exists(configFile))
        {
            Console.WriteLine($"Configuration file '{configFile}' not found. Creating default configuration...");
            var defaultConfig = new AIServerConfig();
            var defaultJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, defaultJson);
            Console.WriteLine($"Default configuration created in '{configFile}'. Please edit it with your settings.");
        }

        try
        {
            var json = File.ReadAllText(configFile);
            var config = JsonSerializer.Deserialize<AIServerConfig>(json);
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
            return new AIServerConfig();
        }
    }
}