using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser zone-change slice (User.cpp): ZoneChange with the battle-event
/// gates, the client loading handshake, the warp-gate list and the map object
/// events (bind points, gate levers, battle flags).
/// </summary>
public sealed partial class GameUser
{
    // e_ZoneChangeOpcode (shared/packets.h).
    private const byte ZoneChangeLoading = 1;
    private const byte ZoneChangeLoaded = 2;
    private const byte ZoneChangeTeleport = 3;

    // e_ZoneID (shared/globals.h).
    private const int ZoneIdKarus = 1;
    private const int ZoneIdElmorad = 2;
    private const int ZoneIdMoradonBorder = 21; // ZONE_MORADON
    private const int ZoneIdArenaZone = 48;     // ZONE_ARENA
    private const int ZoneIdBattle = 101;
    private const int ZoneIdSnowBattle = 111;
    private const int ZoneIdFrontier = 201;

    /// <summary>m_bZoneChangeFlag.</summary>
    public bool ZoneChangeFlag;

    /// <summary>m_bZoneChangeSameZone (in-zone warp gates skip the state reset).</summary>
    public bool ZoneChangeSameZone;

    /// <summary>CUser::ZoneChange.</summary>
    public void ZoneChange(int zone, float x, float z)
    {
        ZoneChangeFlag = true;

        if (world.ServerDownFlag || UserData is not { } user)
            return;

        int zoneIndex = world.GetZoneIndex(zone);
        GameZone? map = world.GetZoneByIndex(zoneIndex);
        if (map is null)
            return;

        // Frontier needs level 20 (except during the snow battle).
        if (map.Type == 2 && user.Level < 20 && world.BattleOpen != SnowBattle)
            return;

        if (world.BattleOpen == NationBattle)
        {
            if (user.Zone == ZoneIdBattle)
            {
                // No invading through a closed gate.
                if (map.Type == 1 && user.Nation != zone)
                {
                    if (user.Nation == 1 && world.ElmoradOpenFlag == 0)
                    {
                        logger.LogError("ZoneChange: zone not open for invasion [charId={CharId} nation={Nation}]",
                            user.CharId, user.Nation);
                        return;
                    }

                    if (user.Nation == 2 && world.KarusOpenFlag == 0)
                    {
                        logger.LogError("ZoneChange: zone not open for invasion [charId={CharId} nation={Nation}]",
                            user.CharId, user.Nation);
                        return;
                    }
                }
            }
            else if (map.Type == 1 && user.Nation != zone)
            {
                return;
            }
            else if (map.Type == 2 && zone == ZoneIdFrontier)
            {
                // The frontier is closed during the war — WIZ_WARP_LIST failure.
                var noticeBuffer = new byte[4];
                var noticeWriter = new PacketWriter(noticeBuffer);
                noticeWriter.SetByte((byte)GameOpcode.WIZ_WARP_LIST);
                noticeWriter.SetByte(2);
                noticeWriter.SetByte(0);
                Send(noticeWriter.Written);
                return;
            }
        }
        else if (world.BattleOpen == SnowBattle)
        {
            if (map.Type == 1 && user.Nation != zone)
                return;

            if (map.Type == 2 && zone is ZoneIdFrontier or ZoneIdBattle)
                return;
        }
        else
        {
            if (map.Type == 1)
            {
                if (user.Nation != zone && zone > ZoneIdMoradonBorder && zone != ZoneIdArenaZone)
                    return;

                if (user.Nation != zone && zone < 3)
                    return;
            }
        }

        Warp = 0x01;

        UserInOut(UserOut);

        if (user.Zone == ZoneIdSnowBattle)
            SetMaxHp(1);

        if (user.Zone != zone)
            SetZoneAbilityChange(zone);

        ZoneIndex = (short)zoneIndex;
        user.Zone = (byte)zone;
        user.CurX = WillX = x;
        user.CurZ = WillZ = z;

        if (user.Zone == ZoneIdSnowBattle)
            SetMaxHp();

        PartyRemoveMember(SocketId);

        if (world.ServerNo != map.ServerNo)
        {
            ZoneServerInfo? info = world.ServerInfos.GetValueOrDefault(map.ServerNo);
            if (info is null)
                return;

            UserDataSaveToAgent();

            logger.LogDebug("ZoneChange: server change [userId={UserId} charId={CharId} zoneId={Zone}]",
                SocketId, user.CharId, zone);

            user.Logout = 2; // server change flag

            byte[] ip = Encoding.Latin1.GetBytes(info.ServerIp);
            var changeBuffer = new byte[16 + ip.Length];
            var changeWriter = new PacketWriter(changeBuffer);
            changeWriter.SetByte((byte)GameOpcode.WIZ_SERVER_CHANGE);
            changeWriter.SetString2(ip);
            changeWriter.SetShort(info.Port);
            changeWriter.SetByte(0x02); // mid-session change
            changeWriter.SetByte(user.Zone);
            changeWriter.SetByte(world.OldVictory);
            Send(changeWriter.Written);
            return;
        }

        user.Bind = -1;

        RegionX = (short)(user.CurX / GameZone.ViewDistance);
        RegionZ = (short)(user.CurZ / GameZone.ViewDistance);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ZONE_CHANGE);
        writer.SetByte(ZoneChangeTeleport);
        writer.SetByte(user.Zone);
        writer.SetByte(0); // subzone
        writer.SetShort((short)(ushort)user.CurX * 10);
        writer.SetShort((short)(ushort)user.CurZ * 10);
        writer.SetShort((short)(user.CurY * 10));
        writer.SetByte(world.OldVictory);
        Send(writer.Written);

        if (!ZoneChangeSameZone)
        {
            WhoKilledMe = -1;
            LostExp = 0;
            RegeneType = 0;
            LastRegeneTime = 0.0;
            user.Bind = -1;
            InitType3();
            InitType4();
        }

        ZoneChangeSameZone = false;

        var aiBuffer = new byte[8];
        var aiWriter = new PacketWriter(aiBuffer);
        aiWriter.SetByte(AiOpcode.AG_ZONE_CHANGE);
        aiWriter.SetShort(SocketId);
        aiWriter.SetByte((byte)ZoneIndex);
        aiWriter.SetByte(user.Zone);
        world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());

        ZoneChangeFlag = false;
    }

    /// <summary>CUser::RecvZoneChange — the client's loading handshake.</summary>
    public void RecvZoneChange(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte opcode = reader.GetByte();

        if (opcode == ZoneChangeLoading)
        {
            world.UserInOutForMe(this);
            world.NpcInOutForMe(this);

            var buffer = new byte[4];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_ZONE_CHANGE);
            writer.SetByte(ZoneChangeLoaded);
            Send(writer.Written);
        }
        else if (opcode == ZoneChangeLoaded)
        {
            UserInOut(UserRegene);
            Warp = 0;

            if (ZoneChangeSameZone)
                ZoneChangeSameZone = false;
            // The !same-zone branch (BlinkStart/ItemMallMagicRecast) is
            // disabled in the C++ (#if 0).
        }
    }

    /// <summary>CUser::SelectWarpList — WIZ_WARP_LIST warp selection.</summary>
    public void SelectWarpList(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        int warpId = reader.GetShort();

        GameZone? currentMap = world.GetZoneByIndex(ZoneIndex);
        WarpInfo? warp = currentMap?.Warps.GetValueOrDefault(warpId);
        if (warp is null)
            return;

        // We cannot use warp gates when invading.
        if (user.Nation != warp.Zone && warp.Zone <= ZoneIdElmorad)
            return;

        // We cannot use warp gates belonging to another nation.
        if (warp.Nation != 0 && warp.Nation != user.Nation)
            return;

        GameZone? targetMap = world.GetZoneById(warp.Zone);
        if (targetMap is null)
            return;

        if (!world.ServerInfos.ContainsKey(targetMap.ServerNo))
            return;

        float rx = world.Rand(0, (int)warp.R * 2);
        if (rx < warp.R)
            rx = -rx;

        float rz = world.Rand(0, (int)warp.R * 2);
        if (rz < warp.R)
            rz = -rz;

        if (user.Zone == warp.Zone)
        {
            ZoneChangeSameZone = true;

            var buffer = new byte[4];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_WARP_LIST);
            writer.SetByte(2);
            writer.SetByte(1);
            Send(writer.Written);
        }

        ZoneChange(warp.Zone, warp.X + rx, warp.Z + rz);
    }

    /// <summary>CUser::ServerChangeOk — WIZ_VIRTUAL_SERVER warp confirmation.</summary>
    public void ServerChangeOk(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int warpId = reader.GetShort();

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        WarpInfo? warp = map?.Warps.GetValueOrDefault(warpId);
        if (warp is null)
            return;

        float rx = world.Rand(0, (int)warp.R * 2);
        if (rx < warp.R)
            rx = -rx;

        float rz = world.Rand(0, (int)warp.R * 2);
        if (rz < warp.R)
            rz = -rz;

        ZoneChange(warp.Zone, warp.X + rx, warp.Z + rz);
    }

    /// <summary>CUser::GetWarpList — the WIZ_WARP_LIST catalogue for one warp group.</summary>
    public bool GetWarpList(int warpGroup)
    {
        GameZone? currentMap = world.GetZoneByIndex(ZoneIndex);
        if (currentMap is null)
            return false;

        var entries = new byte[8192];
        var entryWriter = new PacketWriter(entries);
        int count = 0;

        foreach (WarpInfo warp in currentMap.Warps.Values)
        {
            if (warp.WarpId / 10 != warpGroup)
                continue;

            entryWriter.SetShort(warp.WarpId);
            entryWriter.SetString2(TrimAtNul(warp.WarpName));
            entryWriter.SetString2(TrimAtNul(warp.Announce));
            entryWriter.SetShort(warp.Zone);

            GameZone? targetMap = world.GetZoneById(warp.Zone);
            entryWriter.SetShort(targetMap?.MaxUsers ?? 0);

            entryWriter.SetDWord(warp.Pay);
            entryWriter.SetShort((short)(warp.X * 10));
            entryWriter.SetShort((short)(warp.Z * 10));
            entryWriter.SetShort((short)(warp.Y * 10));
            count++;
        }

        var buffer = new byte[8 + entryWriter.Index];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_WARP_LIST);
        writer.SetByte(1);
        writer.SetShort(count);
        writer.SetString(entries.AsSpan(0, entryWriter.Index));
        Send(writer.Written);

        return true;
    }

    private static ReadOnlySpan<byte> TrimAtNul(byte[] raw)
    {
        int nul = Array.IndexOf(raw, (byte)0);
        return nul < 0 ? raw : raw.AsSpan(0, nul);
    }

    /// <summary>CUser::KickOutZoneUser — send the user back to the home zone.</summary>
    public void KickOutZoneUser(bool home = false)
    {
        if (UserData is not { } user)
            return;

        int zoneIndex = world.GetZoneIndex(user.Nation);
        if (zoneIndex < 0)
            return;

        GameZone? map = world.GetZoneByIndex(zoneIndex);
        if (map is null)
            return;

        if (home)
        {
            int regeneEvent = 0;
            int random = world.Rand(0, 9000);
            if (random < 3000)
                regeneEvent = 0;
            else if (random < 6000)
                regeneEvent = 1;
            else if (random < 9001)
                regeneEvent = 2;

            RegeneEvent? regene = map.GetRegeneEvent(regeneEvent);
            if (regene is null)
            {
                logger.LogError("KickOutZoneUser: no regene event found [charId={CharId} regeneEventId={Event}]",
                    user.CharId, regeneEvent);
                KickOutZoneUser();
                return;
            }

            int deltaX = world.Rand(0, (int)regene.AreaX);
            int deltaZ = world.Rand(0, (int)regene.AreaZ);

            ZoneChange(map.ZoneNumber, regene.PosX + deltaX, regene.PosZ + deltaZ);
        }
        else
        {
            // Fixed native-town coordinates.
            if (user.Nation == 1)
                ZoneChange(map.ZoneNumber, 1335, 83);
            else
                ZoneChange(map.ZoneNumber, 445, 1950);
        }
    }

    /// <summary>CUser::ObjectEvent — WIZ_OBJECT_EVENT dispatch by event type.</summary>
    public void ObjectEventProcess(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        short objectIndex = reader.GetShort();
        short nid = reader.GetShort();

        byte objectType = 0;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        ObjectEvent? objectEvent = map?.GetObjectEvent(objectIndex);
        bool ok = objectEvent is not null;

        if (objectEvent is not null)
        {
            objectType = (byte)objectEvent.Type;

            switch (objectEvent.Type)
            {
                case 0: // bind point
                case 7: // destroyed bind point
                    ok = BindObjectEvent(objectIndex);
                    break;

                case 1: // gate objects — disabled upstream (2002.12.23)
                case 2:
                    break;

                case 3:
                    ok = GateLeverObjectEvent(objectIndex, nid);
                    break;

                case 4:
                    ok = FlagObjectEvent(objectIndex, nid);
                    break;

                case 5:
                    ok = WarpListObjectEvent(objectIndex);
                    break;
            }
        }

        if (ok)
            return;

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_OBJECT_EVENT);
        writer.SetByte(objectType);
        writer.SetByte(0);
        Send(writer.Written);
    }

    /// <summary>CUser::BindObjectEvent — set the respawn bind point.</summary>
    public bool BindObjectEvent(short objectIndex)
    {
        if (UserData is not { } user)
            return false;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        ObjectEvent? objectEvent = map?.GetObjectEvent(objectIndex);
        if (objectEvent is null)
            return false;

        int result;
        if (objectEvent.Belong != 0 && objectEvent.Belong != user.Nation)
        {
            result = 0;
        }
        else
        {
            user.Bind = objectEvent.Index;
            result = 1;
        }

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_OBJECT_EVENT);
        writer.SetByte((byte)objectEvent.Type);
        writer.SetByte((byte)result);
        Send(writer.Written);

        return true;
    }

    /// <summary>CUser::GateLeverObjectEvent — toggle a lever + its gate.</summary>
    public bool GateLeverObjectEvent(short objectIndex, short nid)
    {
        if (!world.PointCheckFlag || UserData is not { } user)
            return false;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        ObjectEvent? objectEvent = map?.GetObjectEvent(objectIndex);
        if (map is null || objectEvent is null)
            return false;

        GameNpc? lever = world.Npcs.GetValueOrDefault(nid);
        if (lever is null)
            return false;

        ObjectEvent? gateEvent = map.GetObjectEvent(objectEvent.ControlNpcId);
        if (gateEvent is null)
            return false;

        int result = 0;
        GameNpc? gateNpc = GetNpcByPid(objectEvent.ControlNpcId, user.Zone);
        if (gateNpc is not null)
        {
            if (gateNpc.NpcType is GameNpc.TypeGate or GameNpc.TypePhoenixGate or GameNpc.TypeSpecialGate)
            {
                if (lever.Group != user.Nation && lever.Group != 0 && lever.GateOpen == 0)
                    return false;

                lever.GateOpen = lever.GateOpen == 0 ? (byte)1 : (byte)0;
                result = 1;
                SendAiGateOpen(lever.Nid, lever.GateOpen, user.Zone);

                gateNpc.GateOpen = gateNpc.GateOpen == 0 ? (byte)1 : (byte)0;
                SendAiGateOpen(gateNpc.Nid, gateNpc.GateOpen, user.Zone);

                SendObjectEventRegion((byte)gateEvent.Type, 1, gateNpc.Nid, gateNpc.GateOpen, user.Zone);
            }
        }

        SendObjectEventRegion((byte)objectEvent.Type, result, nid, lever.GateOpen, user.Zone);
        return true;
    }

    /// <summary>CUser::FlagObjectEvent — capture a battle-zone flag lever.</summary>
    public bool FlagObjectEvent(short objectIndex, short nid)
    {
        if (!world.PointCheckFlag || UserData is not { } user)
            return false;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        ObjectEvent? objectEvent = map?.GetObjectEvent(objectIndex);
        if (map is null || objectEvent is null)
            return false;

        GameNpc? lever = world.Npcs.GetValueOrDefault(nid);
        if (lever is null)
            return false;

        ObjectEvent? flagEvent = map.GetObjectEvent(objectEvent.ControlNpcId);
        if (flagEvent is null)
            return false;

        int result = 0;
        GameNpc? flagNpc = GetNpcByPid(objectEvent.ControlNpcId, user.Zone);
        if (flagNpc is not null)
        {
            if (flagNpc.NpcType is GameNpc.TypeGate or GameNpc.TypePhoenixGate or GameNpc.TypeSpecialGate)
            {
                if (world.Victory > 0)
                    return false;

                if (lever.GateOpen == 0)
                    return false;

                result = 1;

                lever.GateOpen = 0; // the lever stays down
                SendAiGateOpen(lever.Nid, lever.GateOpen, user.Zone);

                flagNpc.GateOpen = 0; // the flag comes down
                SendAiGateOpen(flagNpc.Nid, flagNpc.GateOpen, user.Zone);

                SendObjectEventRegion((byte)flagEvent.Type, result, flagNpc.Nid, flagNpc.GateOpen, user.Zone);

                if (user.Nation == 1)
                    ++world.KarusFlag;
                else if (user.Nation == 2)
                    ++world.ElmoradFlag;

                world.BattleZoneVictoryCheck();
            }
        }

        SendObjectEventRegion((byte)objectEvent.Type, result, nid, lever.GateOpen, user.Zone);
        return true;
    }

    /// <summary>CUser::WarpListObjectEvent — a warp gate object opens the warp list.</summary>
    public bool WarpListObjectEvent(short objectIndex)
    {
        if (UserData is not { } user)
            return false;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        ObjectEvent? objectEvent = map?.GetObjectEvent(objectIndex);
        if (objectEvent is null)
            return false;

        // We cannot use warp gates belonging to another nation.
        if (objectEvent.Belong != 0 && objectEvent.Belong != user.Nation)
            return false;

        // We cannot use warp gates when invading.
        if (user.Nation != user.Zone && user.Zone <= ZoneIdElmorad)
            return false;

        return GetWarpList(objectEvent.ControlNpcId);
    }

    /// <summary>EbenezerApp::GetNpcPtr — NPC by spawn id (m_sPid) within a zone.</summary>
    private GameNpc? GetNpcByPid(int pid, int zone)
    {
        if (!world.PointCheckFlag)
            return null;

        foreach (GameNpc npc in world.Npcs.Values)
        {
            if (npc.CurZone == zone && npc.Pid == pid)
                return npc;
        }

        return null;
    }

    private void SendAiGateOpen(short nid, byte open, int zone)
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_NPC_GATE_OPEN);
        writer.SetShort(nid);
        writer.SetByte(open);
        world.SendToAiServer?.Invoke(zone, writer.Written.ToArray());
    }

    private void SendObjectEventRegion(byte objectType, int result, short nid, byte open, int zone)
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_OBJECT_EVENT);
        writer.SetByte(objectType);
        writer.SetByte((byte)result);
        writer.SetShort(nid);
        writer.SetByte(open);
        world.SendRegion(writer.Written, zone, RegionX, RegionZ, except: null, direct: false);
    }
}
