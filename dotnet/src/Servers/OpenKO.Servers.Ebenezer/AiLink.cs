using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using OpenKO.Network.Framing;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Port of the Ebenezer-side <c>CAISocket</c> (Server/Ebenezer/AISocket.cpp):
/// one outbound link per socket index (0..MAX_AI_SOCKET-1) to the AIServer.
/// The handlers fill the <see cref="GameNpc"/> mirror and broadcast the world
/// updates; they run on the single-writer game loop like every other packet.
/// Combat results (AG_ATTACK_RESULT, AG_USER_EXP, item drops …) attach with the
/// stage-4.6 combat slice.
/// </summary>
public sealed partial class AiLink(int socketIndex, EbenezerWorld world, ILogger logger)
{
    private const int MaxNpcNameSize = 30; // MAX_NPC_NAME_SIZE

    private readonly HashSet<byte> _loggedUnknown = [];

    public int SocketIndex { get; } = socketIndex;

    /// <summary>Wired by the host to the outbound TCP link (plain framing, no cryption).</summary>
    public Func<byte[], bool>? Transmit;

    public bool Send(ReadOnlySpan<byte> payload)
        => Transmit?.Invoke(payload.ToArray()) ?? false;

    /// <summary>CAISocket::Parsing.</summary>
    public void Parsing(ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 0)
            return;

        byte opcode = packet[0];
        ReadOnlySpan<byte> body = packet[1..];

        switch (opcode)
        {
            case AiOpcode.AG_CHECK_ALIVE_REQ:
                RecvCheckAlive();
                break;
            case AiOpcode.AI_SERVER_CONNECT:
                LoginProcess(body);
                break;
            case AiOpcode.AG_SERVER_INFO:
                RecvServerInfo(body);
                break;
            case AiOpcode.NPC_INFO_ALL:
                RecvNpcInfoAll(body);
                break;
            case AiOpcode.MOVE_RESULT:
                RecvNpcMoveResult(body);
                break;
            case AiOpcode.AG_NPC_INFO:
                RecvNpcInfo(body);
                break;
            case AiOpcode.AG_USER_SET_HP:
                RecvUserHp(body);
                break;
            case AiOpcode.AG_COMPRESSED_DATA:
                RecvCompressedData(body);
                break;
            case AiOpcode.AG_NPC_GATE_DESTORY:
                RecvGateDestroy(body);
                break;
            case AiOpcode.AG_DEAD:
                RecvNpcDead(body);
                break;
            case AiOpcode.AG_NPC_INOUT:
                RecvNpcInOut(body);
                break;
            case AiOpcode.AG_NPC_GATE_OPEN:
                RecvGateOpen(body);
                break;
            case AiOpcode.AG_ATTACK_RESULT:
                RecvNpcAttack(body);
                break;
            case AiOpcode.AG_MAGIC_ATTACK_RESULT:
                RecvMagicAttackResult(body);
                break;
            case AiOpcode.AG_USER_EXP:
                RecvUserExp(body);
                break;
            case AiOpcode.AG_SYSTEM_MSG:
                RecvSystemMsg(body);
                break;
            case AiOpcode.AG_NPC_GIVE_ITEM:
                RecvNpcGiveItem(body);
                break;
            case AiOpcode.AG_USER_FAIL:
                RecvUserFail(body);
                break;
            case AiOpcode.AG_NPC_EVENT_ITEM:
                RecvNpcEventItem(body);
                break;
            case AiOpcode.AG_BATTLE_EVENT:
                RecvBattleEvent(body);
                break;
            default:
                if (_loggedUnknown.Add(opcode))
                    logger.LogDebug("AiLink {Index}: unhandled AI opcode 0x{Opcode:X2}", SocketIndex, opcode);
                break;
        }
    }

    /// <summary>CAISocket::RecvCheckAlive — reset the error counter, echo the request.</summary>
    private void RecvCheckAlive()
    {
        world.ErrorSocketCount = 0;
        Send([AiOpcode.AG_CHECK_ALIVE_REQ]);
    }

    /// <summary>CAISocket::LoginProcess — the AI_SERVER_CONNECT reply.</summary>
    private void LoginProcess(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte zone = reader.GetByte();
        byte reconnect = reader.GetByte();

        if (zone == 0xFF)
        {
            logger.LogError("AI Server Version Fail!!");
            return;
        }

        logger.LogInformation("AiLink: AIServer zone={Zone} connected", zone);

        if (reconnect == 0)
        {
            world.SocketCount++;
            if (world.SocketCount == EbenezerWorld.MaxAiSocket)
            {
                world.ServerCheckFlag = true;
                world.SocketCount = 0;
                logger.LogDebug("AiLink: all AI sockets connected, sending all user info...");
                world.SendAllUserInfo();
            }
        }
        else if (reconnect == 1)
        {
            if (world.ReSocketCount == 0)
                world.ReConnectStart = world.Clock();

            world.ReSocketCount++;

            logger.LogInformation("AiLink: reconnect zone={Zone} socketCount={Count}",
                zone, world.ReSocketCount);

            double end = world.Clock();
            if (end > world.ReConnectStart + 120)
            {
                world.ReSocketCount = 0;
                world.ReConnectStart = 0.0;
            }

            if (world.ReSocketCount == EbenezerWorld.MaxAiSocket)
            {
                end = world.Clock();

                // All sockets back within a minute → resync the user info.
                if (end < world.ReConnectStart + 60)
                {
                    world.ServerCheckFlag = true;
                    world.ReSocketCount = 0;
                    world.SendAllUserInfo();
                }
                else
                {
                    world.ReSocketCount = 0;
                    world.ReConnectStart = 0.0;
                }
            }
        }
    }

    /// <summary>CAISocket::RecvServerInfo — the per-zone NPC download brackets.</summary>
    private void RecvServerInfo(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        byte zone = reader.GetByte();

        if (type == EbenezerWorld.ServerInfoStart)
        {
            logger.LogInformation("AiLink: receiving NPC information for zoneId={Zone}", zone);
        }
        else if (type == EbenezerWorld.ServerInfoEnd)
        {
            reader.GetShort(); // total monster count, unused

            logger.LogInformation("NPC info received for zoneId {Zone}", zone);

            world.ZoneCount++;
            if (world.ZoneCount == world.Zones.Count)
            {
                logger.LogInformation("NPC info received for all zones");

                if (!world.FirstServerFlag)
                {
                    world.UserAccept?.Invoke();
                    logger.LogInformation("AiLink: accepting user connections...");
                }

                world.ZoneCount = 0;
                world.FirstServerFlag = true;
                world.PointCheckFlag = true;
            }
        }
    }

    /// <summary>CAISocket::RecvNpcInfoAll — the initial batched NPC download.</summary>
    private void RecvNpcInfoAll(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte count = reader.GetByte();

        for (int i = 0; i < count; i++)
        {
            byte spawnType = reader.GetByte();
            short instanceId = reader.GetShort();
            short npcId = reader.GetShort();
            short pictureId = reader.GetShort();
            short size = reader.GetShort();
            int weapon1 = reader.GetInt();
            int weapon2 = reader.GetInt();
            short zone = reader.GetShort();
            short zoneIndex = reader.GetShort();
            ReadOnlySpan<byte> nameBytes = reader.GetVarString(sizeof(byte));
            byte group = reader.GetByte();
            byte level = reader.GetByte();
            float posX = reader.GetFloat();
            float posZ = reader.GetFloat();
            float posY = reader.GetFloat();
            byte direction = reader.GetByte();
            byte npcType = reader.GetByte();
            int sellingGroup = reader.GetInt();
            int maxHp = reader.GetInt();
            int hp = reader.GetInt();
            byte gateOpen = reader.GetByte();
            short hitRate = reader.GetShort();
            byte objectType = reader.GetByte();
            byte trapNumber = reader.GetByte();

            string name = Encoding.Latin1.GetString(nameBytes);

            // The C++ validates only after reading the full entry.
            if (nameBytes.Length > MaxNpcNameSize)
            {
                logger.LogError("AiLink: npc name size out of bounds [npcId={NpcId} npcName={Name}]", npcId, name);
                continue;
            }

            GameZone? map = world.GetZoneByIndex(zoneIndex);
            if (map is null)
            {
                logger.LogError("AiLink: map not found for zoneIndex [serial={Serial} npcId={NpcId} zoneIndex={ZoneIndex}]",
                    instanceId, npcId, zoneIndex);
                continue;
            }

            if (instanceId < 0)
            {
                logger.LogError("AiLink: invalid serial [serial={Serial} npcId={NpcId}]", instanceId, npcId);
                continue;
            }

            if (pictureId < 0)
            {
                logger.LogError("AiLink: invalid pictureId [serial={Serial} npcId={NpcId}]", instanceId, npcId);
                continue;
            }

            var npc = new GameNpc
            {
                Nid = instanceId,
                Sid = npcId,
                Pid = pictureId,
                Size = size,
                Weapon1 = weapon1,
                Weapon2 = weapon2,
                Name = name,
                Group = group,
                Level = level,
                CurZone = zone,
                ZoneIndex = zoneIndex,
                CurX = posX,
                CurZ = posZ,
                CurY = posY,
                Direction = direction,
                NpcState = GameNpc.StateLive,
                NpcType = npcType,
                SellingGroup = sellingGroup,
                MaxHP = maxHp,
                HP = hp,
                GateOpen = gateOpen,
                HitRate = hitRate,
                ObjectType = objectType,
                TrapNumber = trapNumber,
            };

            int regX = (int)(posX / GameZone.ViewDistance);
            int regZ = (int)(posZ / GameZone.ViewDistance);
            npc.RegionX = (short)regX;
            npc.RegionZ = (short)regZ;

            if (npc.ObjectType == GameNpc.SpecialObject
                && map.GetObjectEvent(npc.Sid) is { } objectEvent)
            {
                objectEvent.Life = 1;
            }

            if (regX < 0 || regZ < 0)
            {
                logger.LogError("AiLink: region out of bounds [serial={Serial} npcId={NpcId} x={X} z={Z}]",
                    instanceId, npcId, regX, regZ);
                continue;
            }

            if (!world.Npcs.TryAdd(npc.Nid, npc))
            {
                logger.LogError("AiLink: NpcMap put failed [serial={Serial} npcId={NpcId}]", instanceId, npcId);
                continue;
            }

            // C++ quirk kept as-is: byType == 0 (not initially spawned) is stored
            // in the mirror but never added to a region, and logged as an error.
            if (spawnType == 0)
            {
                logger.LogError("AiLink: invalid byType={Type} [serial={Serial} npcId={NpcId}]",
                    spawnType, instanceId, npcId);
                continue;
            }

            map.RegionNpcAdd(npc.RegionX, npc.RegionZ, npc.Nid);
        }
    }

    /// <summary>CAISocket::RecvNpcMoveResult.</summary>
    private void RecvNpcMoveResult(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.GetByte(); // flag (INFO_MODIFY/INFO_DELETE), unused
        short nid = reader.GetShort();
        float posX = reader.GetFloat();
        float posZ = reader.GetFloat();
        float posY = reader.GetFloat();
        float speed = reader.GetFloat();

        GameNpc? npc = world.Npcs.GetValueOrDefault(nid);
        if (npc is null)
            return;

        // State desync: a dead mirror NPC is moving → re-request its HP.
        if (npc.NpcState == GameNpc.StateDead || npc.HP <= 0)
        {
            var buffer = new byte[8];
            var writer = new PacketWriter(buffer);
            writer.SetByte(AiOpcode.AG_NPC_HP_REQ);
            writer.SetShort(nid);
            writer.SetDWord((uint)npc.HP);
            Send(writer.Written);
        }

        npc.MoveResult(world, posX, posY, posZ, speed);
    }

    /// <summary>CAISocket::RecvNpcInfo — a single NPC (re)spawn.</summary>
    private void RecvNpcInfo(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte mode = reader.GetByte();
        short instanceId = reader.GetShort();
        short npcId = reader.GetShort();
        short pictureId = reader.GetShort();
        short size = reader.GetShort();
        int weapon1 = reader.GetInt();
        int weapon2 = reader.GetInt();
        short zone = reader.GetShort();
        short zoneIndex = reader.GetShort();
        ReadOnlySpan<byte> nameBytes = reader.GetVarString(sizeof(byte));

        // Unlike RecvNpcInfoAll, the name check aborts before the remaining fields.
        if (nameBytes.Length > MaxNpcNameSize)
            return;

        byte group = reader.GetByte();
        byte level = reader.GetByte();
        float posX = reader.GetFloat();
        float posZ = reader.GetFloat();
        float posY = reader.GetFloat();
        byte direction = reader.GetByte();
        byte state = reader.GetByte();
        byte npcKind = reader.GetByte();
        int sellingGroup = reader.GetInt();
        int maxHp = reader.GetInt();
        int hp = reader.GetInt();
        byte gateOpen = reader.GetByte();
        short hitRate = reader.GetShort();
        byte objectType = reader.GetByte();
        byte trapNumber = reader.GetByte();

        GameNpc? npc = world.Npcs.GetValueOrDefault(instanceId);
        if (npc is null)
            return;

        // C++ quirk kept as-is: the state is set to NPC_DEAD immediately before
        // the regen check, so the "still alive" log can never fire.
        npc.NpcState = GameNpc.StateDead;
        if (npc.NpcState == GameNpc.StateLive)
        {
            logger.LogInformation("AiLink: npc regen check [serial={Serial} npcId={NpcId}]", instanceId, npcId);
        }

        npc.NpcState = GameNpc.StateLive;

        npc.Nid = instanceId;
        npc.Sid = npcId;
        npc.Pid = pictureId;
        npc.Size = size;
        npc.Weapon1 = weapon1;
        npc.Weapon2 = weapon2;
        npc.Name = Encoding.Latin1.GetString(nameBytes);
        npc.Group = group;
        npc.Level = level;
        npc.CurZone = zone;
        npc.ZoneIndex = zoneIndex;
        npc.CurX = posX;
        npc.CurZ = posZ;
        npc.CurY = posY;
        npc.Direction = direction;
        npc.NpcState = state;
        npc.NpcType = npcKind;
        npc.SellingGroup = sellingGroup;
        npc.MaxHP = maxHp;
        npc.HP = hp;
        npc.GateOpen = gateOpen;
        npc.HitRate = hitRate;
        npc.ObjectType = objectType;
        npc.TrapNumber = trapNumber;

        int regX = (int)(posX / GameZone.ViewDistance);
        int regZ = (int)(posZ / GameZone.ViewDistance);
        npc.RegionX = (short)regX;
        npc.RegionZ = (short)regZ;

        GameZone? map = world.GetZoneByIndex(npc.ZoneIndex);
        if (map is null)
        {
            npc.NpcState = GameNpc.StateDead;
            logger.LogError("AiLink: map not found for zoneIndex [serial={Serial} npcId={NpcId} zoneIndex={ZoneIndex}]",
                instanceId, npcId, npc.ZoneIndex);
            return;
        }

        if (npc.ObjectType == GameNpc.SpecialObject
            && map.GetObjectEvent(npc.Sid) is { } objectEvent)
        {
            objectEvent.Life = 1;
        }

        if (mode == 0)
        {
            logger.LogError("AiLink: dead monster [serial={Serial} npcId={NpcId}]", instanceId, npcId);
            return;
        }

        var buffer = new byte[1024];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NPC_INOUT);
        writer.SetByte(GameNpc.NpcIn);
        writer.SetShort(npc.Nid);
        npc.GetNpcInfo(ref writer);

        world.SendRegion(writer.Written, npc.CurZone, regX, regZ);

        map.RegionNpcAdd(npc.RegionX, npc.RegionZ, npc.Nid);
    }

    /// <summary>CAISocket::RecvUserHP — HP sync for users and NPCs.</summary>
    private void RecvUserHp(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int nid = reader.GetShort();
        int hp = (int)reader.GetDWord();
        int maxHp = (int)reader.GetDWord();

        if (nid >= EbenezerWorld.UserBand && nid < EbenezerWorld.NpcBand)
        {
            GameUser? user = nid < world.Users.Length ? world.Users[nid] : null;
            if (user?.UserData is not { } data)
                return;

            data.Hp = (short)hp;
        }
        else if (nid >= EbenezerWorld.NpcBand)
        {
            GameNpc? npc = world.Npcs.GetValueOrDefault(nid);
            if (npc is null)
                return;

            npc.HP = hp;
            npc.MaxHP = maxHp;
        }
    }

    /// <summary>CAISocket::RecvCompressedData — unwrap AG_COMPRESSED_DATA and re-dispatch.</summary>
    private void RecvCompressedData(ReadOnlySpan<byte> body)
    {
        byte[]? decompressed = AgCompressedCodec.Decode(body);
        if (decompressed is null)
        {
            logger.LogError("AiLink {Index}: bad AG_COMPRESSED_DATA payload", SocketIndex);
            return;
        }

        Parsing(decompressed);
    }

    /// <summary>CAISocket::RecvGateDestroy — only mirrors the gate status.</summary>
    private void RecvGateDestroy(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int instanceId = reader.GetShort();
        byte gateStatus = reader.GetByte();
        reader.GetShort(); // zone, unused
        reader.GetShort(); // region x, unused
        reader.GetShort(); // region z, unused

        if (instanceId < EbenezerWorld.NpcBand)
            return;

        GameNpc? npc = world.Npcs.GetValueOrDefault(instanceId);
        if (npc is null)
        {
            logger.LogError("AiLink: NPC not found serial={Serial}", instanceId);
            return;
        }

        npc.GateOpen = gateStatus;
    }

    /// <summary>CAISocket::RecvNpcDead — remove from the region, WIZ_DEAD broadcast.</summary>
    private void RecvNpcDead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int nid = reader.GetShort();

        if (nid < EbenezerWorld.NpcBand)
            return;

        GameNpc? npc = world.Npcs.GetValueOrDefault(nid);
        if (npc is null)
            return;

        GameZone? map = world.GetZoneByIndex(npc.ZoneIndex);
        if (map is null)
            return;

        if (npc.ObjectType == GameNpc.SpecialObject
            && map.GetObjectEvent(npc.Sid) is { } objectEvent)
        {
            objectEvent.Life = 0;
        }

        map.RegionNpcRemove(npc.RegionX, npc.RegionZ, nid);

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_DEAD);
        writer.SetShort(nid);
        world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);

        npc.RegionX = 0;
        npc.RegionZ = 0;
    }

    /// <summary>CAISocket::RecvNpcInOut.</summary>
    private void RecvNpcInOut(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        int nid = reader.GetShort();
        float x = reader.GetFloat();
        float z = reader.GetFloat();
        float y = reader.GetFloat();

        if (nid < EbenezerWorld.NpcBand)
            return;

        GameNpc? npc = world.Npcs.GetValueOrDefault(nid);
        npc?.NpcInOut(world, type, x, z, y);
    }

    /// <summary>CAISocket::RecvGateOpen — WIZ_OBJECT_EVENT for gate NPCs.</summary>
    private void RecvGateOpen(ReadOnlySpan<byte> body)
    {
        // Wire format is short nid + byte gateOpen (CNpc::NpcStanding, CUser
        // gate handlers). The C++ receiver reads an extra short here — an
        // out-of-bounds read it survives silently; we take the event id from
        // the NPC itself instead.
        var reader = new PacketReader(body);
        short instanceId = reader.GetShort();
        byte gateFlag = reader.GetByte();

        GameNpc? npc = world.Npcs.GetValueOrDefault(instanceId);
        short npcId = npc?.Sid ?? -1;
        if (npc is null)
        {
            logger.LogError("AiLink: Npc not found [serial={Serial} npcId={NpcId}]", instanceId, npcId);
            return;
        }

        npc.GateOpen = gateFlag;

        GameZone? map = world.GetZoneByIndex(npc.ZoneIndex);
        ObjectEvent? objectEvent = map?.GetObjectEvent(npcId);
        if (objectEvent is null)
        {
            logger.LogError("AiLink: Npc ObjectEvent not found [serial={Serial} npcId={NpcId}]", instanceId, npcId);
            return;
        }

        if (npc.NpcType is GameNpc.TypeGate or GameNpc.TypePhoenixGate or GameNpc.TypeSpecialGate)
        {
            var buffer = new byte[16];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_OBJECT_EVENT);
            writer.SetByte((byte)objectEvent.Type);
            writer.SetByte(0x01);
            writer.SetShort(instanceId);
            writer.SetByte(npc.GateOpen);
            world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);
        }
    }
}
