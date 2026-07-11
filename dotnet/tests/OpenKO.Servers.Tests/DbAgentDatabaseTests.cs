using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data;
using OpenKO.Servers.Aujard;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// DB-backed smoke tests against a real KN_online database (the repo's
/// docker-compose MSSQL). They no-op (pass trivially) unless OPENKO_TEST_DB is set:
///   OPENKO_TEST_DB="Server=localhost;Database=KN_online;User Id=knight;Password=knight;TrustServerCertificate=true"
/// Run explicitly with: dotnet test --filter "Category=Database"
/// </summary>
[Trait("Category", "Database")]
public class DbAgentDatabaseTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("OPENKO_TEST_DB");

    private static bool DbAvailable => !string.IsNullOrEmpty(ConnectionString);

    private static DbAgent CreateAgent()
        => new(new SqlConnectionFactory(ConnectionString!), NullLogger<DbAgent>.Instance);

    [Fact]
    public async Task Init_LoadsItemTable()
    {
        if (!DbAvailable)
            return;

        var agent = CreateAgent();
        Assert.True(await agent.InitAsync());
    }

    [Fact]
    public async Task KnightsRanking_Loads()
    {
        if (!DbAvailable)
            return;

        var agent = CreateAgent();
        // Should not throw regardless of content; battle-zone variant covers all nations.
        var entries = await agent.LoadKnightsRankingAsync(3);
        Assert.NotNull(entries);
    }

    [Fact]
    public async Task ConcurrentUserCount_Updates()
    {
        if (!DbAvailable)
            return;

        var agent = CreateAgent();
        Assert.True(await agent.UpdateConcurrentUserCountAsync(serverId: 1, zoneId: 1, userCount: 0));
        Assert.False(await agent.UpdateConcurrentUserCountAsync(serverId: 1, zoneId: 4, userCount: 0));
    }
}
