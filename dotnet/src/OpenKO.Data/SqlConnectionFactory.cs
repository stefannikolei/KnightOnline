using Microsoft.Data.SqlClient;

namespace OpenKO.Data;

/// <summary>
/// Replaces the nanodbc/ODBC ConnectionManager with Microsoft.Data.SqlClient.
/// The C++ config keys ([ODBC] DSN/UID/PWD) are kept: DSN maps to the database
/// name (the repo's docker MSSQL uses KN_online), and the server host comes from
/// the optional [ODBC] SERVER key or the OPENKO_DB_SERVER environment variable
/// (default: localhost, matching the docker-compose setup).
/// </summary>
public sealed class SqlConnectionFactory(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public static SqlConnectionFactory FromOdbcConfig(string dsn, string uid, string pwd, string? server = null)
    {
        server ??= Environment.GetEnvironmentVariable("OPENKO_DB_SERVER") ?? "localhost";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = dsn,
            UserID = uid,
            Password = pwd,
            // The dockerized SQL Server uses a self-signed certificate.
            TrustServerCertificate = true,
        };

        return new SqlConnectionFactory(builder.ConnectionString);
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
