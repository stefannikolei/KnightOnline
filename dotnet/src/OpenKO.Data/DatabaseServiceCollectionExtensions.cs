using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenKO.Data;

/// <summary>DI wiring for the game database (the modern config seam).</summary>
public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="SqlConnectionFactory"/> as a singleton. Prefers a
    /// standard <c>ConnectionStrings:GameDb</c> connection string; when absent,
    /// builds it from the <see cref="DatabaseOptions"/> component section
    /// (<c>Database</c>) via <see cref="SqlConnectionFactory.FromOdbcConfig"/>.
    /// </summary>
    public static IServiceCollection AddGameDatabase(
        this IServiceCollection services, IConfiguration configuration, string connectionName = "GameDb")
    {
        string? connectionString = configuration.GetConnectionString(connectionName);

        SqlConnectionFactory factory;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            factory = new SqlConnectionFactory(connectionString);
        }
        else
        {
            var db = new DatabaseOptions();
            configuration.GetSection(DatabaseOptions.SectionName).Bind(db);
            factory = SqlConnectionFactory.FromOdbcConfig(
                db.Dsn, db.Uid, db.Pwd, string.IsNullOrWhiteSpace(db.Server) ? null : db.Server);
        }

        services.AddSingleton(factory);
        return services;
    }
}
