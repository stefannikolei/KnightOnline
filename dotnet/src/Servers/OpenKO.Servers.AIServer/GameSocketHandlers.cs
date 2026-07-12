using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using OpenKO.Servers.AIServer.Ai;

namespace OpenKO.Servers.AIServer;

/// <summary>
/// Port of the AG_* game-opcode handlers of <c>CGameSocket::Parsing</c>
/// (Server/AIServer/GameSocket.cpp) plus <c>CParty::PartyProcess</c> (Party.cpp).
/// AI_SERVER_CONNECT, AG_COMPRESSED_DATA and AG_CHECK_ALIVE_REQ are handled by
/// <see cref="EbenezerLink"/> before packets reach this class. Handlers whose
/// logic sits on the not-yet-ported CNpc combat/magic systems parse their
/// packets and stop at a TODO.
/// </summary>
public sealed class GameSocketHandlers(AiWorld world, ILogger logger)
{
    private const int MaxIdSize = 20;       // MAX_ID_SIZE
    private const int TileSize = 4;         // TILE_SIZE
    private const int UserBand = 0;         // USER_BAND
    private const int NpcBand = 10000;      // NPC_BAND
    private const int InvalidBand = 20000;  // INVALID_BAND

    private const byte BattleZoneOpen = 0x00;   // BATTLEZONE_OPEN
    private const byte BattleZoneClose = 0x01;  // BATTLEZONE_CLOSE
    private const byte KarusZone = 1;           // KARUS_ZONE
    private const byte ElmoradZone = 2;         // ELMORAD_ZONE

    private const byte NpcDoor = 50;          // NPC_DOOR / NPC_GATE
    private const byte NpcPhoenixGate = 51;   // NPC_PHOENIX_GATE
    private const byte NpcGateLever = 55;     // NPC_GATE_LEVER

    private const byte PartyCreate = 0x01;    // PARTY_CREATE
    private const byte PartyInsert = 0x03;    // PARTY_INSERT
    private const byte PartyRemove = 0x04;    // PARTY_REMOVE
    private const byte PartyDelete = 0x05;    // PARTY_DELETE

    // ---- AIServerApp state the handlers mutate (time/weather/battle event) ----
    public short Year;
    public short Month;
    public short DayOfMonth;
    public short Hour;
    public short Minute;
    public byte WeatherType;
    public short WeatherAmount;
    public byte NightMode;
    public int AliveSocketCount;
    public byte BattleEventType;
    public short BattleNpcsKilledByKarus;
    public short BattleNpcsKilledByElmorad;

    public void Attach(EbenezerLink link)
    {
        link.PacketReceived += HandleAsync;
    }

    /// <summary>Dispatch mirroring the CGameSocket::Parsing switch (game opcodes only).</summary>
    public ValueTask HandleAsync(EbenezerLink? link, byte opcode, byte[] body)
    {
        switch (opcode)
        {
            case AiOpcode.AG_USER_INFO:
                RecvUserInfo(body);
                break;

            case AiOpcode.AG_USER_INOUT:
                RecvUserInOut(body);
                break;

            case AiOpcode.AG_USER_MOVE:
                RecvUserMove(body);
                break;

            case AiOpcode.AG_USER_MOVEEDGE:
                RecvUserMoveEdge(body);
                break;

            case AiOpcode.AG_ATTACK_REQ:
                RecvAttackReq(link, body);
                break;

            case AiOpcode.AG_USER_LOG_OUT:
                RecvUserLogOut(body);
                break;

            case AiOpcode.AG_USER_REGENE:
                RecvUserRegene(body);
                break;

            case AiOpcode.AG_USER_SET_HP:
                RecvUserSetHP(body);
                break;

            case AiOpcode.AG_USER_UPDATE:
                RecvUserUpdate(body);
                break;

            case AiOpcode.AG_ZONE_CHANGE:
                RecvZoneChange(body);
                break;

            case AiOpcode.AG_USER_PARTY:
                PartyProcess(body);
                break;

            case AiOpcode.AG_MAGIC_ATTACK_REQ:
                RecvMagicAttackReq(link, body);
                break;

            case AiOpcode.AG_USER_INFO_ALL:
                RecvUserInfoAllData(body);
                break;

            case AiOpcode.AG_PARTY_INFO_ALL:
                RecvPartyInfoAllData(body);
                break;

            case AiOpcode.AG_HEAL_MAGIC:
                RecvHealMagic(body);
                break;

            case AiOpcode.AG_TIME_WEATHER:
                RecvTimeAndWeather(body);
                break;

            case AiOpcode.AG_USER_FAIL:
                RecvUserFail(body);
                break;

            case AiOpcode.AG_BATTLE_EVENT:
                RecvBattleEvent(body);
                break;

            case AiOpcode.AG_NPC_GATE_OPEN:
                RecvGateOpen(body);
                break;

            case AiOpcode.AG_NPC_HP_REQ:
                RecvNpcHpRequest(body);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private void RecvUserInfo(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        short len = r.GetShort();

        // the len > Remaining clause guards the out-of-bounds read the C++ would do
        if (len > MaxIdSize || len <= 0 || len > r.Remaining)
        {
            logger.LogError("RecvUserInfo: charId len={Len} overflow for userId={Uid}", len, uid);
            return;
        }

        string name = Encoding.Latin1.GetString(r.GetString(len));
        byte zone = r.GetByte();
        short zoneIndex = r.GetShort();
        byte nation = r.GetByte();
        byte level = r.GetByte();
        short hp = r.GetShort();
        short mp = r.GetShort();
        short damage = r.GetShort();
        short ac = r.GetShort();
        float hitAgi = r.GetFloat();
        float avoidAgi = r.GetFloat();
        short itemAc = r.GetShort();
        byte typeLeft = r.GetByte();
        byte typeRight = r.GetByte();
        short amountLeft = r.GetShort();
        short amountRight = r.GetShort();
        byte authority = r.GetByte();

        var user = new AiUser();
        user.Initialize();

        user.Uid = uid;
        user.UserId = name;
        user.CurZone = zone;
        user.ZoneIndex = zoneIndex;
        user.Nation = nation;
        user.Level = level;
        user.HP = hp;
        user.MP = mp;
        user.HitDamage = damage;
        user.HitRate = hitAgi;
        user.AvoidRate = avoidAgi;
        user.AC = ac;
        user.Live = AiUser.UserLive;
        user.ItemAC = itemAc;
        user.MagicTypeLeftHand = typeLeft;
        user.MagicTypeRightHand = typeRight;
        user.MagicAmountLeftHand = amountLeft;
        user.MagicAmountRightHand = amountRight;
        user.IsOperator = authority;

        logger.LogDebug("RecvUserInfo: userId={Uid} charId={CharId}", uid, name);

        if (uid >= UserBand && uid < AiConstants.MaxUser)
            world.Users[uid] = user;

        // the C++ writes this to the dedicated AIServerUser log
        logger.LogInformation("Login: level={Level}, charId={CharId}", user.Level, user.UserId);
    }

    private void RecvUserInOut(byte[] body)
    {
        var r = new PacketReader(body);
        byte type = r.GetByte();
        short uid = r.GetShort();
        short len = r.GetShort();

        // the C++ copies len bytes unchecked; guard the overread only
        if (len < 0 || len > r.Remaining)
        {
            logger.LogError("RecvUserInOut: invalid charId length [userId={Uid} len={Len}]", uid, len);
            return;
        }

        string name = Encoding.Latin1.GetString(r.GetString(len));
        float fX = r.GetFloat();
        float fZ = r.GetFloat();

        if (fX < 0 || fZ < 0)
        {
            logger.LogError("RecvUserInOut: invalid position charId={CharId} fX={X} fZ={Z}", name, fX, fZ);
            return;
        }

        int x1 = (int)fX / TileSize;
        int z1 = (int)fZ / TileSize;
        int regionX = (int)fX / AiConstants.ViewDistance;
        int regionZ = (int)fZ / AiConstants.ViewDistance;

        AiUser? user = GetUserPtr(uid);
        if (user is null)
            return;

        if (user.Live == AiUser.UserDead || user.HP <= 0)
        {
            if (user.HP > 0)
                user.Live = AiUser.UserLive;

            logger.LogWarning("RecvUserInOut: UserHeal error[charId={CharId} isAlive={Live} hp={Hp} fX={X} fZ={Z}]",
                user.UserId, user.Live, user.HP, fX, fZ);
        }

        AiZone? zone = GetMapByIndex(user.ZoneIndex);
        if (zone is null)
        {
            logger.LogError("RecvUserInOut: Map not found for zoneIndex={ZoneIndex} [charId={CharId} x1={X1} z1={Z1}]",
                user.ZoneIndex, user.UserId, x1, z1);
            return;
        }

        if (x1 < 0 || z1 < 0 || x1 > zone.Map.MapSize || z1 > zone.Map.MapSize)
        {
            logger.LogError("RecvUserInOut: Character position out of bounds [charId={CharId} x1={X1} z1={Z1}]",
                user.UserId, x1, z1);
            return;
        }

        if (regionX > zone.RegionsX - 1 || regionZ > zone.RegionsZ - 1)
        {
            logger.LogError("RecvUserInOut: region out of bounds [charId={CharId} nRX={Rx} nRZ={Rz}]",
                user.UserId, regionX, regionZ);
            return;
        }

        user.CurX = user.WillX = fX;
        user.CurZ = user.WillZ = fZ;

        // region out
        if (type == 2)
        {
            zone.RegionUserRemove(regionX, regionZ, uid);
        }
        // region in (the C++ does not remove the user from its previous region here)
        else
        {
            if (user.RegionX != regionX || user.RegionZ != regionZ)
            {
                user.RegionX = (short)regionX;
                user.RegionZ = (short)regionZ;
                zone.RegionUserAdd(regionX, regionZ, uid);
            }
        }
    }

    private void RecvUserMove(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        float fX = r.GetFloat();
        float fZ = r.GetFloat();
        r.GetFloat(); // fY, unused
        short speed = r.GetShort();

        SetUid(fX, fZ, uid, speed);
    }

    private void RecvUserMoveEdge(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        float fX = r.GetFloat();
        float fZ = r.GetFloat();
        r.GetFloat(); // fY, unused

        SetUid(fX, fZ, uid, speed: 0);
    }

    /// <summary>Port of CGameSocket::SetUid.</summary>
    private bool SetUid(float x, float z, int id, int speed)
    {
        int x1 = (int)x / TileSize;
        int z1 = (int)z / TileSize;
        int nRX = (int)x / AiConstants.ViewDistance;
        int nRZ = (int)z / AiConstants.ViewDistance;

        AiUser? user = GetUserPtr(id);
        if (user is null)
        {
            logger.LogError("SetUid: userId={Id} is null", id);
            return false;
        }

        AiZone? zone = GetMapByIndex(user.ZoneIndex);
        if (zone is null)
        {
            logger.LogError("SetUid: map not found [charId={CharId} zoneIndex={ZoneIndex}]",
                user.UserId, user.ZoneIndex);
            return false;
        }

        if (x1 < 0 || z1 < 0 || x1 > zone.Map.MapSize || z1 > zone.Map.MapSize)
        {
            logger.LogError("SetUid: character position out of bounds [userId={Id}, charId={CharId} x1={X1} z1={Z1}]",
                id, user.UserId, x1, z1);
            return false;
        }

        if (nRX > zone.RegionsX - 1 || nRZ > zone.RegionsZ - 1)
        {
            logger.LogError("SetUid: region bounds exceeded [userId={Id} charId={CharId} nRX={Rx} nRZ={Rz}]",
                id, user.UserId, nRX, nRZ);
            return false;
        }

        if (user.Live == AiUser.UserDead || user.HP <= 0)
        {
            if (user.HP > 0)
            {
                user.Live = AiUser.UserLive;
                logger.LogDebug("SetUid: user healed [charId={CharId} isAlive={Live} hp={Hp}]",
                    user.UserId, user.Live, user.HP);
            }
            else
            {
                logger.LogError("SetUid: user is dead [charId={CharId} isAive={Live} hp={Hp}]",
                    user.UserId, user.Live, user.HP);
                return false;
            }
        }

        if (speed != 0)
        {
            user.CurX = user.WillX;
            user.CurZ = user.WillZ;
            user.WillX = x;
            user.WillZ = z;
        }
        else
        {
            user.CurX = user.WillX = x;
            user.CurZ = user.WillZ = z;
        }

        if (user.RegionX != nRX || user.RegionZ != nRZ)
        {
            zone.RegionUserRemove(user.RegionX, user.RegionZ, id);
            user.RegionX = (short)nRX;
            user.RegionZ = (short)nRZ;
            zone.RegionUserAdd(user.RegionX, user.RegionZ, id);
        }

        // TODO: MAP::IsRoomCheck (dungeon room events) — room system not yet ported.
        return true;
    }

    private void RecvAttackReq(EbenezerLink? link, byte[] body)
    {
        var r = new PacketReader(body);
        r.GetByte(); // type, unused
        r.GetByte(); // result, unused
        short sid = r.GetShort();
        short tid = r.GetShort();
        short damage = r.GetShort();
        short ac = r.GetShort();
        float hitAgi = r.GetFloat();
        float avoidAgi = r.GetFloat();
        short itemAc = r.GetShort();
        byte typeLeft = r.GetByte();
        byte typeRight = r.GetByte();
        short amountLeft = r.GetShort();
        short amountRight = r.GetShort();

        AiUser? user = GetUserPtr(sid);
        if (user is null)
            return;

        if (user.Live == AiUser.UserDead || user.HP <= 0)
        {
            if (user.HP > 0)
            {
                user.Live = AiUser.UserLive;
                logger.LogDebug("RecvAttackReq: user healed [userId={Uid} charId={CharId} isAlive={Live} hp={Hp}]",
                    user.Uid, user.UserId, user.Live, user.HP);
            }
            else
            {
                logger.LogError("RecvAttackReq: user is dead [userId={Uid} charId={CharId} isAlive={Live} hp={Hp}]",
                    user.Uid, user.UserId, user.Live, user.HP);
                SendUserError(link, sid, tid);
                return;
            }
        }

        user.HitDamage = damage;
        user.HitRate = hitAgi;
        user.AvoidRate = avoidAgi;
        user.AC = ac;
        user.ItemAC = itemAc;
        user.MagicTypeLeftHand = typeLeft;
        user.MagicTypeRightHand = typeRight;
        user.MagicAmountLeftHand = amountLeft;
        user.MagicAmountRightHand = amountRight;

        // TODO: CUser::Attack (User.cpp) — waits on the CNpc combat/exp port.
        logger.LogDebug("RecvAttackReq: CUser::Attack not yet ported [sid={Sid} tid={Tid}]", sid, tid);
    }

    private void RecvUserLogOut(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        short len = r.GetShort();

        // the C++ copies len bytes unchecked; guard the overread only
        if (len < 0 || len > r.Remaining)
        {
            logger.LogError("RecvUserLogOut: invalid charId length [userId={Uid} len={Len}]", uid, len);
            return;
        }

        string name = Encoding.Latin1.GetString(r.GetString(len));

        // the C++ only warns here (its early return is commented out)
        if (len > MaxIdSize || len <= 0)
        {
            logger.LogWarning("RecvUserLogOut: character name length out of bounds [userId={Uid} charId={CharId} len={Len}]",
                uid, name, len);
        }

        AiUser? user = GetUserPtr(uid);
        if (user is null)
            return;

        // the C++ writes this to the dedicated AIServerUser log
        logger.LogInformation("Logout: level={Level}, charId={CharId}", user.Level, user.UserId);

        DeleteUserList(uid);
        logger.LogDebug("RecvUserLogOut: processed [userId={Uid} charId={CharId}]", uid, name);
    }

    /// <summary>Port of AIServerApp::DeleteUserList.</summary>
    private void DeleteUserList(int uid)
    {
        if (uid < 0 || uid >= AiConstants.MaxUser)
        {
            logger.LogError("DeleteUserList: userId invalid: {Uid}", uid);
            return;
        }

        AiUser? user = world.Users[uid];
        if (user is null)
        {
            logger.LogError("DeleteUserList: userId not found: {Uid}", uid);
            return;
        }

        if (user.Uid != uid)
        {
            logger.LogWarning("DeleteUserList: userId mismatch : userId={Uid} pUserId={UserUid}", uid, user.Uid);
            return;
        }

        world.Users[uid] = null;
        logger.LogDebug("DeleteUserList: User Logout: userId={Uid}, charId={CharId}", uid, user.UserId);
    }

    private void RecvUserRegene(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        short hp = r.GetShort();

        AiUser? user = GetUserPtr(uid);
        if (user is null)
            return;

        user.Live = AiUser.UserLive;
        user.HP = hp;

        logger.LogDebug("RecvUserRegene: processed [userId={Uid} charId={CharId} hp={Hp}]",
            user.Uid, user.UserId, user.HP);
    }

    private void RecvUserSetHP(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        int nHp = (int)r.GetDWord(); // the C++ stores GetDWORD into an int

        AiUser? user = GetUserPtr(uid);
        if (user is null)
            return;

        if (user.HP != nHp)
        {
            user.HP = unchecked((short)nHp); // the C++ truncates to the int16 m_sHP
            if (user.HP <= 0)
                user.Dead(world, -100, 0, logger);
        }
    }

    private void RecvUserUpdate(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        byte level = r.GetByte();
        short hp = r.GetShort();
        short mp = r.GetShort();
        short damage = r.GetShort();
        short ac = r.GetShort();
        float hitAgi = r.GetFloat();
        float avoidAgi = r.GetFloat();
        short itemAc = r.GetShort();
        byte typeLeft = r.GetByte();
        byte typeRight = r.GetByte();
        short amountLeft = r.GetShort();
        short amountRight = r.GetShort();

        AiUser? user = GetUserPtr(uid);
        if (user is null)
            return;

        // level up
        if (user.Level < level)
        {
            user.HP = hp;
            user.MP = mp;

            // the C++ writes this to the dedicated AIServerUser log
            logger.LogInformation("LevelUp: level={Level}, charId={CharId}", level, user.UserId);
        }

        user.Level = level;
        user.HitDamage = damage;
        user.HitRate = hitAgi;
        user.AvoidRate = avoidAgi;
        user.AC = ac;
        user.ItemAC = itemAc;
        user.MagicTypeLeftHand = typeLeft;
        user.MagicTypeRightHand = typeRight;
        user.MagicAmountLeftHand = amountLeft;
        user.MagicAmountRightHand = amountRight;
    }

    /// <summary>Port of CGameSocket::Send_UserError (AG_USER_FAIL reply).</summary>
    private void SendUserError(EbenezerLink? link, short uid, short tid)
    {
        Span<byte> buff = stackalloc byte[5];
        var w = new PacketWriter(buff);
        w.SetByte(AiOpcode.AG_USER_FAIL);
        w.SetShort(uid);
        w.SetShort(tid);
        link?.Send(w.Written);

        logger.LogTrace("Send_UserError: AG_USER_FAIL [uid={Uid} tid={Tid}]", uid, tid);
    }

    private void RecvZoneChange(byte[] body)
    {
        var r = new PacketReader(body);
        short uid = r.GetShort();
        byte zoneIndex = r.GetByte();
        byte zoneNumber = r.GetByte();

        AiUser? user = GetUserPtr(uid);
        if (user is null)
            return;

        user.ZoneIndex = zoneIndex;
        user.CurZone = zoneNumber;

        logger.LogTrace("RecvZoneChange: [charId={CharId} userId={Uid} zoneId={ZoneId}]",
            user.UserId, user.Uid, zoneNumber);
    }

    private void RecvMagicAttackReq(EbenezerLink? link, byte[] body)
    {
        var r = new PacketReader(body);
        short sid = r.GetShort();

        AiUser? user = GetUserPtr(sid);
        if (user is null)
            return;

        if (user.Live == AiUser.UserDead || user.HP <= 0)
        {
            if (user.HP > 0)
            {
                user.Live = AiUser.UserLive;
                logger.LogDebug("RecvMagicAttackReq: user healed [charId={CharId} isAlive={Live}, hp={Hp}]",
                    user.UserId, user.Live, user.HP);
            }
            else
            {
                logger.LogError("RecvMagicAttackReq: user is dead [charId={CharId} isAlive={Live}, hp={Hp}]",
                    user.UserId, user.Live, user.HP);
                SendUserError(link, sid, -1);
                return;
            }
        }

        // TODO: CMagicProcess::MagicPacket (the rest of the body) — magic system not yet ported.
        logger.LogDebug("RecvMagicAttackReq: CMagicProcess::MagicPacket not yet ported [sid={Sid} bytes={Bytes}]",
            sid, r.Remaining);
    }

    private void RecvUserInfoAllData(byte[] body)
    {
        var r = new PacketReader(body);

        logger.LogDebug("RecvUserInfoAllData: begin");

        byte count = r.GetByte();
        for (int i = 0; i < count; i++)
        {
            short uid = r.GetShort();
            short len = r.GetShort();

            // the C++ copies len bytes unchecked before validating; guard the overread only
            if (len < 0 || len > r.Remaining)
            {
                logger.LogError("RecvUserInfoAllData: invalid charId length [userId={Uid} len={Len}]", uid, len);
                return;
            }

            string name = Encoding.Latin1.GetString(r.GetString(len));
            byte zone = r.GetByte();
            short zoneIndex = r.GetShort();
            byte nation = r.GetByte();
            byte level = r.GetByte();
            short hp = r.GetShort();
            short mp = r.GetShort();
            short damage = r.GetShort();
            short ac = r.GetShort();
            float hitAgi = r.GetFloat();
            float avoidAgi = r.GetFloat();
            short partyIndex = r.GetShort();
            byte authority = r.GetByte();

            if (len > MaxIdSize || len <= 0)
            {
                logger.LogError("RecvUserInfoAllData: character name length is out of bounds [userId={Uid} charId={CharId} len={Len}]",
                    uid, name, len);
                continue;
            }

            var user = new AiUser();
            user.Initialize();

            user.Uid = uid;
            user.UserId = name;
            user.CurZone = zone;
            user.ZoneIndex = zoneIndex;
            user.Nation = nation;
            user.Level = level;
            user.HP = hp;
            user.MP = mp;
            user.HitDamage = damage;
            user.HitRate = hitAgi;
            user.AvoidRate = avoidAgi;
            user.AC = ac;
            user.IsOperator = authority;
            user.Live = AiUser.UserLive;

            if (partyIndex != -1)
            {
                user.NowParty = 1;
                user.PartyNumber = partyIndex;
                logger.LogDebug("RecvUserInfoAllData: party info [userId={Uid} charId={CharId} partyNumber={Party}]",
                    uid, name, user.PartyNumber);
            }

            if (uid >= UserBand && uid < AiConstants.MaxUser)
                world.Users[uid] = user;
        }

        logger.LogDebug("RecvUserInfoAllData: end");
    }

    private void RecvGateOpen(byte[] body)
    {
        var r = new PacketReader(body);
        short nid = r.GetShort();
        byte gateOpen = r.GetByte();

        // C++ quirk kept as-is: `nid < NPC_BAND || nid < INVALID_BAND` rejects every
        // id below 20000, i.e. all valid NPC serials (presumably meant >= INVALID_BAND).
        if (nid < NpcBand || nid < InvalidBand)
        {
            logger.LogError("RecvGateOpen: invalid npcId={Nid}", nid);
            return;
        }

        // C++ quirk kept as-is: the raw wire id is used, elsewhere the key is nid - NPC_BAND.
        if (!world.Npcs.TryGetValue(nid, out Npc? npc))
            return;

        if (npc.NpcType == NpcDoor || npc.NpcType == NpcGateLever || npc.NpcType == NpcPhoenixGate)
        {
            // C++ quirk kept as-is: `byGateOpen < 0 || byGateOpen < 2` rejects states 0 and 1.
            if (gateOpen < 2)
            {
                logger.LogError("RecvGateOpen: invalid gateOpen={GateOpen} state for npcId={Nid}", gateOpen, nid);
                return;
            }

            npc.GateOpen = gateOpen;

            logger.LogDebug("RecvGateOpen: updated [npcId={Nid} gateOpen={GateOpen}]", nid, gateOpen);
        }
        else
        {
            logger.LogError("RecvGateOpen: invalid npcType={NpcType} for npcId={Nid}", npc.NpcType, nid);
        }
    }

    private void RecvPartyInfoAllData(byte[] body)
    {
        var r = new PacketReader(body);
        short partyIndex = r.GetShort();

        if (partyIndex >= 32767 || partyIndex < 0)
        {
            logger.LogError("RecvPartyInfoAllData: partyIndex={Party} out of bounds", partyIndex);
            return;
        }

        var party = new PartyGroup { Index = partyIndex };
        for (int i = 0; i < PartyGroup.MaxMembers; i++)
            party.Users[i] = r.GetShort();

        if (world.Parties.TryAdd(party.Index, party))
            logger.LogDebug("RecvPartyInfoAllData: created partyIndex={Party}", partyIndex);
    }

    private void RecvHealMagic(byte[] body)
    {
        var r = new PacketReader(body);
        short sid = r.GetShort();

        AiUser? user = GetUserPtr(sid);
        if (user is null)
            return;

        if (user.Live == AiUser.UserDead || user.HP <= 0)
        {
            if (user.HP > 0)
            {
                user.Live = AiUser.UserLive;
                logger.LogDebug("RecvHealMagic: user healed [userId={Uid} charId={CharId} isAlive={Live} hp={Hp}]",
                    user.Uid, user.UserId, user.Live, user.HP);
            }
            else
            {
                logger.LogWarning("RecvHealMagic: user is dead [userId={Uid} charId={CharId} isAlive={Live} hp={Hp}]",
                    user.Uid, user.UserId, user.Live, user.HP);
                return;
            }
        }

        // TODO: CUser::HealMagic (region scan + CNpc::ChangeTarget) — waits on the CNpc AI port.
        logger.LogDebug("RecvHealMagic: CUser::HealMagic not yet ported [sid={Sid}]", sid);
    }

    private void RecvTimeAndWeather(byte[] body)
    {
        var r = new PacketReader(body);

        Year = r.GetShort();
        Month = r.GetShort();
        DayOfMonth = r.GetShort();
        Hour = r.GetShort();
        Minute = r.GetShort();
        WeatherType = r.GetByte();
        WeatherAmount = r.GetShort();

        // day
        if (Hour >= 5 && Hour < 21)
            NightMode = 1;
        // night
        else
            NightMode = 2;

        AliveSocketCount = 0; // doubles as the socket alive-check in the C++
    }

    private void RecvUserFail(byte[] body)
    {
        var r = new PacketReader(body);
        short sid = r.GetShort();
        r.GetShort(); // tid, unused
        short hp = r.GetShort();

        AiUser? user = GetUserPtr(sid);
        if (user is null)
            return;

        user.Live = AiUser.UserLive;
        user.HP = hp;
    }

    private void RecvBattleEvent(byte[] body)
    {
        var r = new PacketReader(body);
        r.GetByte(); // nType, unused
        int nEvent = r.GetByte();

        if (nEvent == BattleZoneOpen)
        {
            BattleNpcsKilledByKarus = 0;
            BattleNpcsKilledByElmorad = 0;
            BattleEventType = BattleZoneOpen;
            logger.LogDebug("RecvBattleEvent: battle zone open");
        }
        else if (nEvent == BattleZoneClose)
        {
            BattleNpcsKilledByKarus = 0;
            BattleNpcsKilledByElmorad = 0;
            BattleEventType = BattleZoneClose;
            logger.LogDebug("RecvBattleEvent: battle zone closed");

            // TODO: AIServerApp::ResetBattleZone — waits on the map room/event port.
            logger.LogDebug("RecvBattleEvent: ResetBattleZone not yet ported");
        }

        int affected = 0;
        foreach (Npc npc in world.Npcs.Values)
        {
            // nation-owned NPCs only (npcType > 10)
            if (npc.NpcType > 10 && (npc.Group == KarusZone || npc.Group == ElmoradZone))
                affected++;
        }

        // TODO: CNpc::ChangeAbility(BATTLEZONE_OPEN/CLOSE) per matching NPC — waits on the CNpc combat/HP port.
        if (affected > 0)
            logger.LogDebug("RecvBattleEvent: CNpc::ChangeAbility not yet ported [npcs={Count} event={Event}]",
                affected, nEvent);
    }

    private void RecvNpcHpRequest(byte[] body)
    {
        // Ebenezer sends AG_NPC_HP_REQ (nid + hp) on NPC state desync, but the C++
        // CGameSocket::Parsing has no case for it — parse only, no behavior to port.
        var r = new PacketReader(body);
        short nid = r.GetShort();
        int hp = (int)r.GetDWord();

        // TODO: decide on a resync reply (AG_NPC_HP_CHANGE) once the CNpc HP port lands.
        logger.LogDebug("RecvNpcHpRequest: not handled by the C++ AIServer [npcId={Nid} hp={Hp}]", nid, hp);
    }

    // ---- CParty::PartyProcess (Server/AIServer/Party.cpp) ----

    private void PartyProcess(byte[] body)
    {
        var r = new PacketReader(body);
        byte subcommand = r.GetByte();

        switch (subcommand)
        {
            case PartyCreate:
                DoPartyCreate(ref r);
                break;

            case PartyInsert:
                DoPartyInsert(ref r);
                break;

            case PartyRemove:
                DoPartyRemove(ref r);
                break;

            case PartyDelete:
                DoPartyDelete(ref r);
                break;
        }
    }

    private void DoPartyCreate(ref PacketReader r)
    {
        short partyIndex = r.GetShort();
        short uid = r.GetShort();

        AiUser? user = GetUserPtr(uid);
        if (user is not null)
        {
            user.NowParty = 1;
            user.PartyNumber = partyIndex;
        }

        var party = new PartyGroup { Index = partyIndex };
        party.Users[0] = uid;

        if (!world.Parties.TryAdd(party.Index, party))
        {
            logger.LogError("Party::PartyCreate: failed [partyId={Party} uid0={Uid0} uid1={Uid1}]",
                partyIndex, party.Users[0], party.Users[1]);
            return;
        }

        logger.LogDebug("Party::PartyCreate: success [partyId={Party} uid0={Uid0} uid1={Uid1}]",
            partyIndex, party.Users[0], party.Users[1]);
    }

    private void DoPartyInsert(ref PacketReader r)
    {
        short partyIndex = r.GetShort();
        byte memberIndex = r.GetByte();
        short uid = r.GetShort();

        if (!world.Parties.TryGetValue(partyIndex, out PartyGroup? party))
            return;

        if (memberIndex < PartyGroup.MaxMembers)
        {
            party.Users[memberIndex] = uid;

            AiUser? user = GetUserPtr(uid);
            if (user is not null)
            {
                user.NowParty = 1;
                user.PartyNumber = partyIndex;
            }
        }
    }

    private void DoPartyRemove(ref PacketReader r)
    {
        short partyIndex = r.GetShort();
        short uid = r.GetShort();

        // C++ quirk kept as-is: `sUid > MAX_USER` (not >=)
        if (uid < 0 || uid > AiConstants.MaxUser)
            return;

        if (partyIndex <= -1)
            return;

        if (!world.Parties.TryGetValue(partyIndex, out PartyGroup? party))
            return;

        for (int i = 0; i < PartyGroup.MaxMembers; i++)
        {
            if (party.Users[i] != -1 && party.Users[i] == uid)
            {
                party.Users[i] = -1;

                AiUser? user = GetUserPtr(uid);
                if (user is not null)
                {
                    user.NowParty = 0;
                    user.PartyNumber = -1;
                }
            }
        }
    }

    private void DoPartyDelete(ref PacketReader r)
    {
        short partyIndex = r.GetShort();

        if (partyIndex <= -1)
            return;

        if (!world.Parties.TryGetValue(partyIndex, out PartyGroup? party))
            return;

        for (int i = 0; i < PartyGroup.MaxMembers; i++)
        {
            if (party.Users[i] != -1)
            {
                AiUser? user = GetUserPtr(party.Users[i]);
                if (user is not null)
                {
                    user.NowParty = 0;
                    user.PartyNumber = -1;
                }
            }
        }

        world.Parties.Remove(party.Index);
    }

    // ---- helpers ----

    /// <summary>Port of AIServerApp::GetUserPtr (slot must hold the matching uid).</summary>
    private AiUser? GetUserPtr(int nid)
    {
        if (nid < 0 || nid >= AiConstants.MaxUser)
        {
            if (nid != -1)
                logger.LogError("GetUserPtr: User Array Overflow [{Nid}]", nid);

            return null;
        }

        AiUser? user = world.Users[nid];
        if (user is null)
            return null;

        if (user.Uid < 0 || user.Uid >= AiConstants.MaxUser)
            return null;

        return user.Uid == nid ? user : null;
    }

    /// <summary>Port of AIServerApp::GetMapByIndex.</summary>
    private AiZone? GetMapByIndex(int zoneIndex)
    {
        if (zoneIndex < 0 || zoneIndex >= world.Zones.Count)
        {
            logger.LogError("GetMapByIndex: zoneIndex={ZoneIndex} out of bounds", zoneIndex);
            return null;
        }

        return world.Zones[zoneIndex];
    }
}
