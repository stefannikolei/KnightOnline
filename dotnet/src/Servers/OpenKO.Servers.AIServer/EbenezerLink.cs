using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using OpenKO.Network.Framing;
using OpenKO.Network.Tcp;

namespace OpenKO.Servers.AIServer;

/// <summary>
/// Server-side handler for one Ebenezer connection (port of <c>CGameSocket</c>):
/// the AIServer LISTENS (10020 karus/unify, 10030 elmorad, 10040 battle) and
/// Ebenezer connects in with one socket per zone. Handles the AI_SERVER_CONNECT
/// handshake, the AG_COMPRESSED_DATA envelope and alive-check bookkeeping; all
/// game opcodes are raised via <see cref="PacketReceived"/> for the NPC/user
/// logic to attach as it is ported.
/// </summary>
public sealed class EbenezerLink(KoSession session, ILogger logger)
{
    private readonly HashSet<byte> _reportedUnhandled = [];

    /// <summary>Zone this socket serves; -1 until AI_SERVER_CONNECT arrives.</summary>
    public short ZoneNo { get; private set; } = -1;

    public KoSession Session { get; } = session;

    /// <summary>Raised per game opcode after de-framing/decompression (opcode, body).</summary>
    public event Func<EbenezerLink, byte, byte[], ValueTask>? PacketReceived;

    /// <summary>Raised when Ebenezer (re)connects a zone: (zoneNo, isReconnect).</summary>
    public event Action<EbenezerLink, byte, bool>? ZoneConnected;

    /// <summary>Raised on AG_CHECK_ALIVE_REQ (the C++ resets its alive counter).</summary>
    public event Action<EbenezerLink>? AliveCheckReceived;

    public bool Send(ReadOnlySpan<byte> payload) => Session.Send(payload);

    public async ValueTask DispatchAsync(byte[] payload)
    {
        byte opcode = payload[0];

        switch (opcode)
        {
            case AiOpcode.AI_SERVER_CONNECT:
                HandleServerConnect(payload);
                break;

            case AiOpcode.AG_CHECK_ALIVE_REQ:
                AliveCheckReceived?.Invoke(this);
                break;

            case AiOpcode.AG_COMPRESSED_DATA:
            {
                byte[]? inner = AgCompressedCodec.Decode(payload.AsSpan(1));
                if (inner is null)
                {
                    logger.LogWarning("zone {Zone}: dropping corrupt AG_COMPRESSED_DATA", ZoneNo);
                    return;
                }

                await DispatchAsync(inner);
                return;
            }

            default:
                if (PacketReceived is { } handler)
                {
                    await handler(this, opcode, payload[1..]);
                }
                else if (_reportedUnhandled.Add(opcode))
                {
                    logger.LogDebug("zone {Zone}: no handler for opcode {Opcode} yet", ZoneNo, opcode);
                }

                break;
        }
    }

    /// <summary>Port of RecvServerConnect: reads zone + reconnect flag, echoes them back.</summary>
    private void HandleServerConnect(byte[] payload)
    {
        var reader = new PacketReader(payload) { Index = 1 };
        byte zoneNumber = reader.GetByte();
        byte reconnect = reader.GetByte(); // 0: first connect, 1: reconnect

        logger.LogInformation("Ebenezer connected to zone={Zone} (reconnect={Reconnect})", zoneNumber, reconnect);

        ZoneNo = zoneNumber;

        var buffer = new byte[3];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AI_SERVER_CONNECT);
        writer.SetByte(zoneNumber);
        writer.SetByte(reconnect);
        Send(writer.Written);

        ZoneConnected?.Invoke(this, zoneNumber, reconnect == 1);
    }
}

/// <summary>AIServer listen ports by server zone type (Server/AIServer/Define.h).</summary>
public static class AiServerPorts
{
    public const int Karus = 10020;   // AI_KARUS_SOCKET_PORT (also UNIFY_ZONE)
    public const int Elmorad = 10030; // AI_ELMO_SOCKET_PORT
    public const int Battle = 10040;  // AI_BATTLE_SOCKET_PORT
    public const int MaxSockets = 100;
}
