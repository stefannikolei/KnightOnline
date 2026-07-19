using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.GameData.Maps;
using OpenKO.Network;
using OpenKO.Network.Framing;
using OpenKO.Servers.AIServer.Ai;
using OpenKO.Servers.AIServer.Db;
using AiNpc = OpenKO.Servers.AIServer.Ai.Npc;

namespace OpenKO.Servers.AIServer;

/// <summary>
/// Port of <c>AIServerApp</c> (Server/AIServer/AIServerApp.cpp): the startup
/// sequence (table loads → maps/rooms → NPC spawn → per-NPC wiring), the
/// Ebenezer connection bookkeeping (RecvServerConnect tail, AllNpcInfo push),
/// the 10s CheckAliveTest and the zone-routed Send. All game-state mutation is
/// driven by the host's single-writer loop (replacing the C++ mutex model).
/// </summary>
public sealed class AiServerApp(
    AiWorld world,
    GameSocketHandlers handlers,
    int serverZoneType,
    ILogger logger)
{
    private const int MaxAiSocket = 10;     // MAX_AI_SOCKET
    private const int NpcsPerBatch = 20;    // NPC_NUM
    private const byte ServerInfoStart = 1; // SERVER_INFO_START
    private const byte ServerInfoEnd = 2;   // SERVER_INFO_END
    private const byte SpecialObject = 1;   // SPECIAL_OBJECT

    private readonly Dictionary<short, EbenezerLink> _links = [];

    // RecvServerConnect / CheckAliveTest bookkeeping.
    private int _socketCount;
    private int _reconnectSocketCount;
    private double _reconnectStartTime;

    // SendThreadMain::_nextRoundRobinSocketId.
    private int _nextSendLink;

    /// <summary>_mapEventNpcCount: serials taken by map object-event NPCs.</summary>
    public int MapEventNpcCount { get; private set; }

    /// <summary>_totalNpcCount.</summary>
    public int TotalNpcCount { get; private set; }

    public AiWorld World => world;

    /// <summary>_firstServerFlag — public in the C++ too (set from CGameSocket).</summary>
    public bool FirstServerFlag { get; set; }

    // ------------------------------------------------------------------
    //  Startup (AIServerApp::OnStart)
    // ------------------------------------------------------------------

    /// <summary>
    /// Loads every startup table, the maps (with room events and object-event
    /// NPCs) and spawns the NPCs — the DB/map part of OnStart. Returns false on
    /// the same fatal conditions the C++ aborts with.
    /// </summary>
    public async Task<bool> StartupAsync(AiServerDb db, string mapDirectory, CancellationToken ct)
    {
        if (!await LoadTablesAsync(db, ct))
            return false;

        if (!MapFileLoad(mapDirectory))
        {
            logger.LogError("AiServerApp: failed to load maps, closing server");
            return false;
        }

        List<NpcPos>? npcPosRows = await db.LoadNpcPosTableAsync(ct);
        if (npcPosRows is null)
        {
            logger.LogError("AiServerApp: K_NPCPOS load failed");
            return false;
        }

        var spawner = new NpcSpawner(world, logger) { NextSerial = MapEventNpcCount };
        if (!spawner.SpawnAll(npcPosRows, serverZoneType, GetServerNumber))
            return false;

        TotalNpcCount = spawner.NextSerial;

        // CreateNpcThread: bucket assignment + Init + hook wiring per NPC.
        int index = 0;
        foreach (int nid in world.Npcs.Keys.Order())
        {
            AiNpc npc = world.Npcs[nid];
            npc.ThreadNumber = (short)(index / NpcsPerBatch);
            index++;

            WireNpc(npc);
            npc.Init();
        }

        handlers.SendToZone = (zone, payload) =>
        {
            Send(zone, payload);
            return ValueTask.CompletedTask;
        };

        logger.LogInformation("AiServerApp: Monsters/NPCs loaded: {Count}", TotalNpcCount);
        return true;
    }

    private async Task<bool> LoadTablesAsync(AiServerDb db, CancellationToken ct)
    {
        // Load order and fatality follow OnStart; every loader logs its own error.
        List<Magic>? magic = await db.LoadMagicTableAsync(ct);
        List<MagicType1>? type1 = await db.LoadMagicType1TableAsync(ct);
        List<MagicType2>? type2 = await db.LoadMagicType2TableAsync(ct);
        List<MagicType3>? type3 = await db.LoadMagicType3TableAsync(ct);
        List<MagicType4>? type4 = await db.LoadMagicType4TableAsync(ct);
        List<MagicType7>? type7 = await db.LoadMagicType7TableAsync(ct);
        List<MonsterItem>? monsterItems = await db.LoadMonsterItemTableAsync(ct);
        List<MakeWeapon>? makeWeapon = await db.LoadMakeWeaponTableAsync(ct);
        List<MakeDefensive>? makeDefensive = await db.LoadMakeDefensiveTableAsync(ct);
        List<MakeItemGradeCode>? makeGrade = await db.LoadMakeItemGradeCodeTableAsync(ct);
        List<MakeItemRareCode>? makeRare = await db.LoadMakeItemRareCodeTableAsync(ct);
        List<MakeItemGroup>? makeGroup = await db.LoadMakeItemGroupTableAsync(ct);
        List<ZoneInfo>? zoneInfo = await db.LoadZoneInfoTableAsync(ct);
        List<Monster>? monsters = await db.LoadMonsterTableAsync(ct);
        List<Data.Models.Npc>? npcs = await db.LoadNpcTableAsync(ct);

        if (magic is null || type1 is null || type2 is null || type3 is null || type4 is null
            || type7 is null || monsterItems is null || makeWeapon is null || makeDefensive is null
            || makeGrade is null || makeRare is null || makeGroup is null || zoneInfo is null
            || monsters is null || npcs is null)
            return false;

        world.MagicTable = magic.ToDictionary(r => r.ID);
        world.MagicType1Table = type1.ToDictionary(r => r.ID);
        world.MagicType2Table = type2.ToDictionary(r => r.ID);
        world.MagicType3Table = type3.ToDictionary(r => r.ID);
        world.MagicType4Table = type4.ToDictionary(r => r.ID);
        world.MagicType7Table = type7.ToDictionary(r => r.ID);
        world.MonsterItemTable = monsterItems;
        world.MakeWeaponTable = makeWeapon.ToDictionary(r => (int)r.Level);
        world.MakeDefensiveTable = makeDefensive.ToDictionary(r => (int)r.Level);
        world.MakeGradeItemTable = makeGrade.ToDictionary(r => (int)r.ItemIndex);
        world.MakeRareItemTable = makeRare.ToDictionary(r => (int)r.LevelGrade);
        world.MakeItemGroupTable = makeGroup.ToDictionary(r => r.ItemGroupNumber);
        world.ZoneInfoTable = zoneInfo.ToDictionary(r => r.ZoneId);
        // The C++ loads K_MONSTER through a Monster→Npc binder into the same
        // Npc-shaped map as K_NPC (identical column layout).
        world.MonsterTable = monsters.ToDictionary(r => (int)r.MonsterId, ToNpcRow);
        world.NpcTable = npcs.ToDictionary(r => (int)r.NpcId);

        return true;
    }

    /// <summary>AIServerApp::MapFileLoad — one MAP/AiZone per ZONE_INFO row.</summary>
    public bool MapFileLoad(string mapDirectory)
    {
        if (world.ZoneInfoTable.Count == 0)
            return false;

        foreach (ZoneInfo row in world.ZoneInfoTable.Values.OrderBy(r => r.ZoneId))
        {
            string mapPath = Path.Combine(mapDirectory, row.Name);
            if (!File.Exists(mapPath))
            {
                logger.LogError("AiServerApp: failed to open map file: {Path}", mapPath);
                return false;
            }

            GameMap map;
            try
            {
                map = GameMap.Load(mapPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AiServerApp: failed to load map file: {Path}", mapPath);
                return false;
            }

            AiZone zone = AiZone.Create(row.ServerId, row.ZoneId, map);

            // MAP::LoadObjectEvent registers gate/lever/artifact objects as NPCs
            // while the map loads (their ZoneIndex stays -1, like the C++).
            foreach (ObjectEvent ev in map.ObjectEvents)
            {
                // OBJECT_TYPE_GATE/DOOR_TOPDOWN/GATE_LEVER/BARRICADE/REMOVE_BIND/ANVIL/ARTIFACT
                if (ev.Type is 1 or 2 or 3 or 6 or 7 or 8 or 9)
                    AddObjectEventNpc(ev, row.ZoneId);
            }

            // dungeon work: <RoomEvent>.evt next to the maps.
            if (row.RoomEvent > 0)
            {
                if (!zone.LoadRoomEvents(mapDirectory, row.RoomEvent))
                {
                    logger.LogError("AiServerApp: LoadRoomEvent failed: {Path}", mapPath);
                    return false;
                }

                zone.RoomEventFlag = 1;
            }

            world.Zones.Add(zone);
        }

        return true;
    }

    /// <summary>AIServerApp::AddObjectEventNpc — gates/levers/artifacts from map data.</summary>
    public bool AddObjectEventNpc(ObjectEvent ev, int zoneNumber)
    {
        int serverNum = GetServerNumber((short)zoneNumber);
        if (serverZoneType != serverNum)
            return false;

        Data.Models.Npc? table = world.NpcTable.GetValueOrDefault(ev.Index);
        if (table is null)
        {
            logger.LogError("AiServerApp: AddObjectEventNpc error: eventId={EventId} zoneId={ZoneId}",
                ev.Index, zoneNumber);
            return false;
        }

        var npc = new AiNpc
        {
            Nid = (short)MapEventNpcCount,
            Sid = ev.Index,
            MoveType = 0,
            InitMoveType = 0,
            BattlePos = 0,
            SecForMeter = 4.0f,
        };
        MapEventNpcCount++;

        npc.Load(table, transformSpeeds: false);

        npc.CurZone = (short)zoneNumber;
        npc.GateOpen = (byte)ev.Status;
        npc.CurX = ev.PosX;
        npc.CurY = ev.PosY;
        npc.CurZ = ev.PosZ;

        npc.InitMinX = (int)(ev.PosX - 1);
        npc.InitMinY = (int)(ev.PosZ - 1);
        npc.InitMaxX = (int)(ev.PosX + 1);
        npc.InitMaxY = (int)(ev.PosZ + 1);

        npc.RegenTime = 10000 * 1000;
        npc.MaxPathCount = 0;

        npc.ZoneIndex = -1; // the C++ never resolves these to a zone index
        npc.ObjectType = SpecialObject;
        npc.FirstLive = true;

        if (!world.Npcs.TryAdd(npc.Nid, npc))
            logger.LogWarning("AiServerApp: AddObjectEventNpc Npc PutData Fail [serial={Serial}]", npc.Nid);

        TotalNpcCount = MapEventNpcCount;
        return true;
    }

    /// <summary>Wires one NPC's m_pMain replacements to this app.</summary>
    public void WireNpc(AiNpc npc)
    {
        npc.World = world;
        npc.Battle = handlers.Battle;
        npc.GetNightMode = () => handlers.NightMode;
        npc.GetBattleEventType = () => handlers.BattleEventType;
        npc.SendToZone = payload =>
        {
            Send(npc.CurZone, payload);
            return ValueTask.CompletedTask;
        };
        npc.LogItemDrop = itemId =>
            logger.LogInformation("ItemDrop: npcId={NpcId} npcName={Name} itemId={ItemId}",
                npc.Sid, npc.Name, itemId);
    }

    /// <summary>AIServerApp::GetServerNumber (ZONE_INFO lookup).</summary>
    public int GetServerNumber(short zoneId)
    {
        ZoneInfo? info = world.ZoneInfoTable.GetValueOrDefault(zoneId);
        if (info is null)
        {
            logger.LogError("AiServerApp: GetServerNumber: zoneId={ZoneId} not found", zoneId);
            return -1;
        }

        return info.ServerId;
    }

    // ------------------------------------------------------------------
    //  Ebenezer connection bookkeeping (RecvServerConnect tail)
    // ------------------------------------------------------------------

    /// <summary>Called from <see cref="EbenezerLink.ZoneConnected"/>.</summary>
    public void OnZoneConnected(EbenezerLink link, byte zoneNumber, bool reconnect)
    {
        _links[zoneNumber] = link;

        double now = world.Clock();

        if (reconnect)
        {
            if (_reconnectSocketCount == 0)
                _reconnectStartTime = now;

            _reconnectSocketCount++;
            logger.LogInformation("Ebenezer reconnect: zone={Zone} sockets={Count}", zoneNumber, _reconnectSocketCount);

            // All sockets took longer than 2 minutes: reset the window.
            if (now > _reconnectStartTime + 120)
            {
                _reconnectSocketCount = 0;
                _reconnectStartTime = 0.0;
            }

            if (_reconnectSocketCount == MaxAiSocket)
            {
                // All sockets reconnected within a minute → resync everything.
                if (world.Clock() < _reconnectStartTime + 60)
                {
                    FirstServerFlag = true;
                    _reconnectSocketCount = 0;
                    AllNpcInfo();
                }
                else
                {
                    _reconnectSocketCount = 0;
                    _reconnectStartTime = 0.0;
                }
            }
        }
        else
        {
            _socketCount++;
            logger.LogInformation("Ebenezer connected [zone={Zone}, sockets={Count}]", zoneNumber, _socketCount);

            if (_socketCount == MaxAiSocket)
            {
                logger.LogInformation("Ebenezer sockets all connected [sockets={Count}]", _socketCount);
                FirstServerFlag = true;
                _socketCount = 0;
                AllNpcInfo();
            }
        }
    }

    /// <summary>
    /// AIServerApp::AllNpcInfo — pushes every NPC to the game servers in
    /// NPC_INFO_ALL batches of 20 (compressed), bracketed by AG_SERVER_INFO.
    /// </summary>
    public void AllNpcInfo()
    {
        foreach (AiZone zoneEntry in world.Zones)
        {
            int nZone = zoneEntry.ZoneNumber;

            var startBuf = new byte[3];
            var startWriter = new PacketWriter(startBuf);
            startWriter.SetByte(AiOpcode.AG_SERVER_INFO);
            startWriter.SetByte(ServerInfoStart);
            startWriter.SetByte((byte)nZone);
            Send(nZone, startBuf);

            var batch = new byte[2048];
            var writer = new PacketWriter(batch) { Index = 2 };
            int count = 0;

            foreach (int nid in world.Npcs.Keys.Order())
            {
                AiNpc npc = world.Npcs[nid];
                if (npc.CurZone != nZone)
                    continue;

                npc.SendNpcInfoAll(ref writer, count);
                count++;

                if (count == NpcsPerBatch)
                {
                    batch[0] = AiOpcode.NPC_INFO_ALL;
                    batch[1] = (byte)count;
                    SendCompressed(nZone, batch.AsSpan(0, writer.Index));

                    batch = new byte[2048];
                    writer = new PacketWriter(batch) { Index = 2 };
                    count = 0;
                }
            }

            // Remainder goes out uncompressed, like the C++.
            if (count is > 0 and < NpcsPerBatch)
            {
                batch[0] = AiOpcode.NPC_INFO_ALL;
                batch[1] = (byte)count;
                Send(nZone, batch.AsSpan(0, writer.Index).ToArray());
            }

            var endBuf = new byte[8];
            var endWriter = new PacketWriter(endBuf);
            endWriter.SetByte(AiOpcode.AG_SERVER_INFO);
            endWriter.SetByte(ServerInfoEnd);
            endWriter.SetByte((byte)nZone);
            endWriter.SetShort(TotalNpcCount);
            Send(nZone, endWriter.Written.ToArray());

            logger.LogDebug("AllNpcInfo: done for zoneId={Zone}", nZone);
        }
    }

    /// <summary>AIServerApp::SendCompressedData — LZF + CRC32 envelope.</summary>
    private void SendCompressed(int zone, ReadOnlySpan<byte> payload)
    {
        byte[]? body = AgCompressedCodec.Encode(payload);
        if (body is null)
        {
            logger.LogError("SendCompressedData: failed to compress packet");
            return;
        }

        var packet = new byte[1 + body.Length];
        packet[0] = AiOpcode.AG_COMPRESSED_DATA;
        body.CopyTo(packet.AsSpan(1));
        Send(zone, packet);
    }

    /// <summary>
    /// AIServerApp::Send / SendThreadMain::tick — the zone is NOT a routing key:
    /// the Ebenezer sockets are parallel pipes to the same host, so packets go
    /// out round-robin over whichever links are up (falling through to the next
    /// on a send failure). Drops everything until all sockets are connected
    /// (_firstServerFlag), like the C++.
    /// </summary>
    public int Send(int zone, byte[] payload)
    {
        if (!FirstServerFlag)
            return 0;

        if (payload.Length <= 0)
            return 0;

        EbenezerLink[] links = [.. _links.Values];
        for (int attempt = 0; attempt < links.Length; attempt++)
        {
            _nextSendLink %= links.Length;
            EbenezerLink link = links[_nextSendLink++];
            if (link.Send(payload))
                return 0;
        }

        if (links.Length > 0)
            logger.LogError("Send: all {Count} Ebenezer links failed, packet dropped (opcode={Opcode:X2})",
                links.Length, payload[0]);

        return 0;
    }

    private void Send(int zone, ReadOnlySpan<byte> payload) => Send(zone, payload.ToArray());

    // ------------------------------------------------------------------
    //  CheckAliveTest (10s timer)
    // ------------------------------------------------------------------

    /// <summary>AIServerApp::CheckAliveTest — alive pings + region activity flags.</summary>
    public void CheckAliveTest()
    {
        var ping = new byte[] { AiOpcode.AG_CHECK_ALIVE_REQ };

        int alive = 0;
        foreach (EbenezerLink link in _links.Values)
        {
            if (link.Send(ping))
                alive++;
        }

        if (alive <= 0)
            DeleteAllUserList(9999);

        RegionCheck();
    }

    /// <summary>AIServerApp::DeleteAllUserList — full user wipe once all sockets die.</summary>
    public void DeleteAllUserList(int zone)
    {
        if (zone < 0)
            return;

        if (zone == 9999 && FirstServerFlag)
        {
            logger.LogDebug("DeleteAllUserList: start");

            foreach (AiZone zoneEntry in world.Zones)
            {
                for (int i = 0; i < zoneEntry.RegionsX; i++)
                {
                    for (int j = 0; j < zoneEntry.RegionsZ; j++)
                        zoneEntry.Regions[i, j].Users.Clear();
                }
            }

            Array.Clear(world.Users);
            world.Parties.Clear();

            FirstServerFlag = false;
            logger.LogDebug("DeleteAllUserList: end");
        }
        else if (zone != 9999)
        {
            logger.LogInformation("DeleteAllUserList: ebenezer zone {Zone} disconnected", zone);
        }
    }

    /// <summary>AIServerApp::RegionCheck — marks regions with users as active.</summary>
    public void RegionCheck()
    {
        foreach (AiZone zoneEntry in world.Zones)
        {
            for (int i = 0; i < zoneEntry.RegionsX; i++)
            {
                for (int j = 0; j < zoneEntry.RegionsZ; j++)
                {
                    Region region = zoneEntry.Regions[i, j];
                    region.Moving = region.Users.Count > 0 ? (byte)1 : (byte)0;
                }
            }
        }
    }

    private static Data.Models.Npc ToNpcRow(Monster m) => new()
    {
        NpcId = m.MonsterId,
        Name = m.Name,
        PictureId = m.PictureId,
        Size = m.Size,
        Weapon1 = m.Weapon1,
        Weapon2 = m.Weapon2,
        Group = m.Group,
        ActType = m.ActType,
        Type = m.Type,
        Family = m.Family,
        Rank = m.Rank,
        Title = m.Title,
        SellingGroup = m.SellingGroup,
        Level = m.Level,
        Exp = m.Exp,
        Loyalty = m.Loyalty,
        HitPoints = m.HitPoints,
        ManaPoints = m.ManaPoints,
        Attack = m.Attack,
        Armor = m.Armor,
        HitRate = m.HitRate,
        EvadeRate = m.EvadeRate,
        Damage = m.Damage,
        AttackDelay = m.AttackDelay,
        WalkSpeed = m.WalkSpeed,
        RunSpeed = m.RunSpeed,
        StandTime = m.StandTime,
        Magic1 = m.Magic1,
        Magic2 = m.Magic2,
        Magic3 = m.Magic3,
        FireResist = m.FireResist,
        ColdResist = m.ColdResist,
        LightningResist = m.LightningResist,
        MagicResist = m.MagicResist,
        DiseaseResist = m.DiseaseResist,
        PoisonResist = m.PoisonResist,
        LightResist = m.LightResist,
        Bulk = m.Bulk,
        AttackRange = m.AttackRange,
        SearchRange = m.SearchRange,
        TracingRange = m.TracingRange,
        Money = m.Money,
        Item = m.Item,
        DirectAttack = m.DirectAttack,
        MagicAttack = m.MagicAttack,
        MoneyType = m.MoneyType,
    };
}
