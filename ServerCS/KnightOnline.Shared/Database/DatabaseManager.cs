using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace KnightOnline.Shared.Database;

/// <summary>
/// Database configuration for Knight Online servers
/// </summary>
public class DatabaseConfig
{
    public string Server { get; set; } = "localhost";
    public string Database { get; set; } = "KnightOnline";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool IntegratedSecurity { get; set; } = true;
    public int ConnectionTimeout { get; set; } = 30;
    public int CommandTimeout { get; set; } = 30;

    public string GetConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = Database,
            ConnectTimeout = ConnectionTimeout,
            CommandTimeout = CommandTimeout
        };

        if (IntegratedSecurity)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = Username;
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}

/// <summary>
/// Database connection manager for Knight Online servers
/// </summary>
public class DatabaseManager : IDisposable
{
    private readonly DatabaseConfig _config;
    private readonly string _connectionString;
    private volatile bool _disposed = false;

    public DatabaseManager(DatabaseConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _connectionString = _config.GetConnectionString();
    }

    public async Task<SqlConnection> CreateConnectionAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseManager));

        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public SqlConnection CreateConnection()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseManager));

        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = await CreateConnectionAsync();
            return connection.State == ConnectionState.Open;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database connection test failed: {ex.Message}");
            return false;
        }
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseManager));

        try
        {
            using var connection = await CreateConnectionAsync();
            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = _config.CommandTimeout;
            
            if (parameters != null)
                command.Parameters.AddRange(parameters);

            var result = await command.ExecuteScalarAsync();
            return result is T value ? value : default(T);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database ExecuteScalar error: {ex.Message}");
            throw;
        }
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, params SqlParameter[] parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseManager));

        try
        {
            using var connection = await CreateConnectionAsync();
            using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = _config.CommandTimeout;
            
            if (parameters != null)
                command.Parameters.AddRange(parameters);

            return await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database ExecuteNonQuery error: {ex.Message}");
            throw;
        }
    }

    public async Task<SqlDataReader> ExecuteReaderAsync(string sql, params SqlParameter[] parameters)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseManager));

        try
        {
            var connection = await CreateConnectionAsync();
            var command = new SqlCommand(sql, connection);
            command.CommandTimeout = _config.CommandTimeout;
            
            if (parameters != null)
                command.Parameters.AddRange(parameters);

            // Note: Connection will be disposed when reader is disposed
            return await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database ExecuteReader error: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            // Any cleanup if needed
        }
    }
}