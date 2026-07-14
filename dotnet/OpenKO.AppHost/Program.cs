// .NET Aspire AppHost — orchestrates the full OpenKO stack: the SQL Server
// database, all five servers in dependency order, and the client.
//
// Startup order (enforced with WaitFor):
//   sql (KN_online) ─┬─ itemmanager (no DB)
//                    ├─ aujard        (DB)
//                    ├─ versionmanager(DB, login server :15100)
//                    ├─ aiserver      (DB, NPC zones)
//                    └─ ebenezer      (DB) ── waits for aiserver ── game server
//                                       client ── waits for versionmanager + ebenezer
//
// The database resource is named "GameDb", so `WithReference(gameDb)` injects
// ConnectionStrings__GameDb into each server — exactly the key AddGameDatabase
// reads. Env vars win over appsettings.json, so the servers use the container.

var builder = DistributedApplication.CreateBuilder(args);

// SQL Server container with a persistent data volume; the actual database is
// KN_online, exposed to the services under the connection name "GameDb".
var sql = builder.AddSqlServer("sql", port: 1433)
    .WithDataVolume("openko-sqldata")
    .WithLifetime(ContainerLifetime.Persistent);

var gameDb = sql.AddDatabase("GameDb", databaseName: "KN_online");

// ItemManager has no database dependency (TCP loopback log sink).
builder.AddProject<Projects.OpenKO_Servers_ItemManager>("itemmanager");

// Aujard standalone host — validates DB connectivity (normally embedded in Ebenezer).
builder.AddProject<Projects.OpenKO_Servers_Aujard>("aujard")
    .WithReference(gameDb)
    .WaitFor(gameDb);

// VersionManager (login server, :15100).
var versionManager = builder.AddProject<Projects.OpenKO_Servers_VersionManager>("versionmanager")
    .WithReference(gameDb)
    .WaitFor(gameDb);

// AIServer (NPC zones) must be up before Ebenezer, which connects to it on start.
var aiServer = builder.AddProject<Projects.OpenKO_Servers_AIServer>("aiserver")
    .WithReference(gameDb)
    .WaitFor(gameDb);

// Ebenezer (game server, :15001) — needs the DB and a live AIServer.
var ebenezer = builder.AddProject<Projects.OpenKO_Servers_Ebenezer>("ebenezer")
    .WithReference(gameDb)
    .WaitFor(gameDb)
    .WaitFor(aiServer);

// The client connects to the login server first, then to Ebenezer.
builder.AddProject<Projects.OpenKO_Client>("client")
    .WithArgs("--server", "127.0.0.1:15100", "--account", "test", "--password", "test")
    .WaitFor(versionManager)
    .WaitFor(ebenezer);

builder.Build().Run();
