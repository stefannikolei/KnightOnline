using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenKO.AppHost;

/// <summary>
/// Reports healthy once a TCP port accepts a connection. Used as the readiness
/// gate for the game servers, whose raw sockets only start listening well after
/// the process is "Running" (Ebenezer opens :15001 only after the DB tables and
/// the AIServer NPC download are done) — WaitFor on the process state alone lets
/// the clients race ahead into "Connection refused".
/// Sticky: after the first success it stops probing, so the periodic health poll
/// does not keep opening throwaway game sessions on the server.
/// </summary>
internal sealed class TcpPortHealthCheck(int port) : IHealthCheck
{
    private volatile bool _wasOpen;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_wasOpen)
            return HealthCheckResult.Healthy($"Port {port} accepted a connection.");

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            _wasOpen = true;
            return HealthCheckResult.Healthy($"Port {port} accepted a connection.");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Port {port} is not accepting connections yet.");
        }
    }
}
