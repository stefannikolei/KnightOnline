using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace OpenKO.Servers.ItemManager.Transport;

/// <summary>In-process transport (tests, or Ebenezer co-hosted in the same process).</summary>
public sealed class InMemoryItemLogSource : IItemLogSource
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();

    public bool TryWrite(byte[] message)
    {
        if (message.Length > IItemLogSource.MaxMessageSize)
            return false;

        return _channel.Writer.TryWrite(message);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<byte[]> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (byte[] message in _channel.Reader.ReadAllAsync(cancellationToken))
            yield return message;
    }
}
