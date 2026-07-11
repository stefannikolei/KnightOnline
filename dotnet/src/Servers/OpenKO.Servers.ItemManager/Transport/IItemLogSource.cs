namespace OpenKO.Servers.ItemManager.Transport;

/// <summary>
/// Transport abstraction replacing the boost::interprocess queue ITEMLOG_SEND
/// (512-byte messages, unframed <c>[opcode][body]</c> payloads).
/// boost's queue layout is compiler/ABI-specific and not portably readable from
/// .NET, so the C# topology uses pluggable transports instead: in-memory for
/// tests/in-process hosting, TCP loopback for split-process deployments. The
/// stage-4 C# Ebenezer will write to this same contract.
/// </summary>
public interface IItemLogSource
{
    /// <summary>Maximum message size, mirroring SharedMemoryQueue's MAX_MSG_SIZE.</summary>
    const int MaxMessageSize = 512;

    IAsyncEnumerable<byte[]> ReadAllAsync(CancellationToken cancellationToken = default);
}
