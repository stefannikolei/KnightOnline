using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenKO.Network.Tcp;

namespace OpenKO.Servers.ItemManager.Transport;

/// <summary>
/// TCP loopback transport: producers connect and send standard KO frames
/// (<c>AA 55 [len] payload 55 AA</c>) whose payloads are exactly the queue
/// message bodies (<c>[opcode][body]</c>, max 512 bytes). This defines the
/// contract the stage-4 C# Ebenezer implements when running out-of-process.
/// </summary>
public sealed class TcpItemLogSource : IItemLogSource, IAsyncDisposable
{
    public const int DefaultPort = 15200;

    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private readonly KoTcpServer _server;

    public TcpItemLogSource(IPEndPoint endPoint, ILogger logger)
    {
        _server = new KoTcpServer(endPoint, maxSessions: 32, OnPacketAsync, logger);
    }

    public IPEndPoint? LocalEndPoint => _server.LocalEndPoint;

    public void Start() => _server.Start();

    public Task RunAsync(CancellationToken cancellationToken) => _server.RunAsync(cancellationToken);

    private ValueTask OnPacketAsync(KoSession session, byte[] payload)
    {
        if (payload.Length <= IItemLogSource.MaxMessageSize)
            _channel.Writer.TryWrite(payload);

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<byte[]> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (byte[] message in _channel.Reader.ReadAllAsync(cancellationToken))
            yield return message;
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
