// Schema readiness health check + dashboard commands for the kodb-util loader.
//
// The kodb-util container (built from docker/kodb-util, an upstream artifact left
// unchanged) clones + builds the Go tool that imports the real OpenKO schema from
// GitHub. We drive it entirely from the AppHost:
//   * SchemaHealthCheck probes KN_online for user tables, so the loader only turns
//     "Healthy" once the schema is actually present (fresh import OR restored from
//     the persistent volume) — that is the gate the DB servers WaitFor.
//   * The two dashboard commands re-run docker/kodb-util/cleanImport.sh inside the
//     already-running loader container via `docker exec`, mirroring the existing
//     docker/reset_database.sh flow without editing any docker/* file.

using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace OpenKO.AppHost;

/// <summary>
/// Reports the KN_online database as healthy only when the real game schema has
/// been imported (at least one non-system table exists). Used as the readiness
/// gate for the kodb-util loader resource, which the DB servers wait for.
/// </summary>
internal sealed class SchemaHealthCheck(Func<CancellationToken, ValueTask<string?>> resolveConnectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        string? connectionString;
        try
        {
            connectionString = await resolveConnectionString(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Connection string not available yet.", ex);
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            return HealthCheckResult.Unhealthy("Connection string not available yet.");
        }

        // Keep probes snappy while SQL Server is still warming up.
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = 5,
            TrustServerCertificate = true,
        };

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;";
            command.CommandTimeout = 5;

            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return count > 0
                ? HealthCheckResult.Healthy($"Schema present ({count} user tables).")
                : HealthCheckResult.Unhealthy("Database reachable but schema not imported yet.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database not reachable / schema not ready.", ex);
        }
    }
}

/// <summary>
/// Dashboard command handlers that re-run the kodb-util import inside the running
/// loader container. Both shell out to the local Docker CLI (the AppHost host runs
/// the containers), locating the Aspire-managed container by its resource-name
/// prefix and executing the unchanged docker/kodb-util/cleanImport.sh.
/// </summary>
internal static class DatabaseCommands
{
    private const string SentinelPath = "/var/lib/app/.openko-imported";

    /// <summary>Clean import: drop + recreate KN_online from the latest schema.</summary>
    public static async Task<ExecuteCommandResult> ResetDatabaseAsync(ExecuteCommandContext context)
    {
        // Remove the "already imported" sentinel first so a fresh clean import runs,
        // exactly like docker/reset_database.sh.
        var script = $"rm -f {SentinelPath}; /usr/local/bin/cleanImport.sh && touch {SentinelPath}";
        return await RunInLoaderAsync(context, script, "reset").ConfigureAwait(false);
    }

    /// <summary>Refresh schema: git pull + submodule update + import (cleanImport already does this).</summary>
    public static async Task<ExecuteCommandResult> ReloadSchemaAsync(ExecuteCommandContext context)
    {
        // cleanImport.sh itself does git pull + submodule --remote + import; keep the
        // sentinel touched so a later container restart still skips the auto-import.
        var script = $"/usr/local/bin/cleanImport.sh && touch {SentinelPath}";
        return await RunInLoaderAsync(context, script, "reload").ConfigureAwait(false);
    }

    private static async Task<ExecuteCommandResult> RunInLoaderAsync(ExecuteCommandContext context, string script, string label)
    {
        var logger = context.ServiceProvider.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory
            ? factory.CreateLogger("OpenKO.AppHost.DatabaseCommands")
            : null;

        string container;
        try
        {
            container = await ResolveContainerAsync(context.ResourceName, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Could not locate the kodb-util container for '{Resource}'.", context.ResourceName);
            return CommandResults.Failure(ex.Message);
        }

        logger?.LogInformation("kodb-util {Label}: running import in container {Container}.", label, container);

        var (exitCode, output) = await RunDockerAsync(
            new[] { "exec", container, "/bin/sh", "-c", script },
            context.CancellationToken).ConfigureAwait(false);

        if (exitCode == 0)
        {
            logger?.LogInformation("kodb-util {Label} completed successfully.", label);
            return CommandResults.Success();
        }

        logger?.LogError("kodb-util {Label} failed (exit {Exit}):\n{Output}", label, exitCode, output);
        var tail = output.Length > 500 ? output[^500..] : output;
        return CommandResults.Failure($"kodb-util {label} failed (exit {exitCode}). {tail}");
    }

    /// <summary>Find the running Aspire-managed container whose name starts with the resource name.</summary>
    private static async Task<string> ResolveContainerAsync(string resourceName, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunDockerAsync(
            new[] { "ps", "--filter", $"name={resourceName}", "--format", "{{.Names}}" },
            cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"`docker ps` failed (exit {exitCode}): {output}");
        }

        var name = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(n => n.StartsWith(resourceName, StringComparison.Ordinal));

        return name ?? throw new InvalidOperationException(
            $"No running container found for resource '{resourceName}'. Is the kodb-util loader started?");
    }

    private static async Task<(int ExitCode, string Output)> RunDockerAsync(string[] args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, string.Concat(stdout, stderr));
    }
}
