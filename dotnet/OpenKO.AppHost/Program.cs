// .NET Aspire AppHost — orchestrates the full OpenKO stack: the SQL Server
// database (with the real game schema loaded via kodb-util), all five servers in
// dependency order, and the client.
//
// Startup order (enforced with WaitFor):
//   sqlserver (KN_online)
//        └─ kodb-util ── imports the real schema (only when empty) ── Healthy = schema present
//               ├─ itemmanager (no DB)  [waits only implicitly; started independently]
//               ├─ aujard        (DB)
//               ├─ versionmanager(DB, login server :15100)
//               ├─ aiserver      (DB, NPC zones)
//               └─ ebenezer      (DB) ── waits for aiserver ── game server
//                    client ── waits for versionmanager + ebenezer
//
// The database resource is named "GameDb", so `WithReference(gameDb)` injects
// ConnectionStrings__GameDb into each server — exactly the key AddGameDatabase
// reads. Env vars win over appsettings.json, so the servers use the container.
//
// The SQL Server resource MUST be named "sqlserver": docker/kodb-util's
// kodb-util-config.yaml hard-codes `host: sqlserver`, and Aspire uses the
// resource name as the container network alias.

using Microsoft.Extensions.DependencyInjection;
using OpenKO.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// Shared SA password for the SQL Server container AND the kodb-util loader (both
// must authenticate with the same credentials). Default matches docker/default.env.
var sqlPassword = builder.AddParameter("sql-password", "D0ckeIzKn!ght", secret: true);

// SQL Server container (native Aspire resource) with a persistent data volume; the
// actual database is KN_online, exposed to the services under the name "GameDb".
var sql = builder
    .AddSqlServer("sqlserver", sqlPassword, port: 1433)
    .WithDataVolume("openko-sqldata")
    .WithLifetime(ContainerLifetime.Persistent)
    // DbGate — web UI pre-connected to this SQL Server instance for browsing/
    // querying KN_online (launch link shows up on theKN_ sqlserver resource).
    .WithDbGate();

var gameDb = sql.AddDatabase("GameDb", databaseName: "KN_online");

// Readiness gate: the kodb-util loader is only "Healthy" once KN_online actually
// contains the imported schema (fresh import OR restored from the persistent
// volume). The DB servers WaitFor this, so they never start against an empty DB.
builder
    .Services.AddHealthChecks()
    .AddCheck(
        "kn-online-schema",
        new SchemaHealthCheck(ct =>
            ((IResourceWithConnectionString)gameDb.Resource).GetConnectionStringAsync(ct)
        )
    );

// kodb-util loader — builds the upstream docker/kodb-util image (Go tool that
// clones + imports the real OpenKO-db schema from GitHub) and runs it as a
// long-lived container. The entrypoint imports ONCE (guarded by a sentinel in the
// persistent volume) and then idles so the dashboard commands can `docker exec`
// into it. docker/kodb-util/* is left unchanged; only the entrypoint is overridden.
var kodb = builder
    .AddDockerfile("kodb-util", "../../docker/kodb-util")
    .WithEnvironment("MSSQL_SA_PASSWORD", sqlPassword)
    .WithEnvironment("SQL_PORT", "1433")
    .WithEnvironment("GAME_DB_NAME", "KN_online")
    .WithEnvironment("GAME_DB_USER", "knight")
    .WithEnvironment("GAME_DB_PASS", "knight")
    .WithEnvironment("GAME_DB_SCHEMA", "knight")
    .WithVolume("openko-kodb-util-data", "/var/lib/app")
    .WithEntrypoint("/bin/sh")
    .WithArgs("-c", DatabaseCommands.EntrypointScript)
    .WaitFor(sql)
    .WithHealthCheck("kn-online-schema")
    .WithCommand(
        "reset-database",
        "Reset database (clean import)",
        DatabaseCommands.ResetDatabaseAsync,
        new CommandOptions
        {
            Description = "Drops KN_online and re-imports the schema from scratch via kodb-util.",
            ConfirmationMessage =
                "This DROPS the KN_online database and re-imports it from scratch. Continue?",
            IconName = "ArrowReset",
        }
    )
    .WithCommand(
        "reload-schema",
        "Reload schema (git pull + import)",
        DatabaseCommands.ReloadSchemaAsync,
        new CommandOptions
        {
            Description = "Pulls the latest OpenKO-db schema and re-imports it via kodb-util.",
            ConfirmationMessage =
                "This pulls the latest schema and re-imports KN_online. Continue?",
            IconName = "DatabaseArrowUp",
        }
    );

// ItemManager has no database dependency (TCP loopback log sink).
builder.AddProject<Projects.OpenKO_Servers_ItemManager>("itemmanager");

// Aujard standalone host — validates DB connectivity (normally embedded in Ebenezer).
builder
    .AddProject<Projects.OpenKO_Servers_Aujard>("aujard")
    .WithReference(gameDb)
    .WaitFor(kodb);

// VersionManager (login server, :15100).
var versionManager = builder
    .AddProject<Projects.OpenKO_Servers_VersionManager>("versionmanager")
    .WithReference(gameDb)
    .WaitFor(kodb);

// AIServer (NPC zones) must be up before Ebenezer, which connects to it on start.
var aiServer = builder
    .AddProject<Projects.OpenKO_Servers_AIServer>("aiserver")
    .WithReference(gameDb)
    .WaitFor(kodb);

// Ebenezer (game server, :15001) — needs the DB (schema) and a live AIServer.
var ebenezer = builder
    .AddProject<Projects.OpenKO_Servers_Ebenezer>("ebenezer")
    .WithReference(gameDb)
    .WaitFor(kodb)
    .WaitFor(aiServer);

// The client connects to the login server first, then to Ebenezer.
builder
    .AddProject<Projects.OpenKO_Client>("client")
    .WithArgs("--server", "127.0.0.1:15100", "--account", "test", "--password", "test")
    .WaitFor(versionManager)
    .WaitFor(ebenezer);

builder.Build().Run();
