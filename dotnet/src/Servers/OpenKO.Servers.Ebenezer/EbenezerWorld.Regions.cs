using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The EbenezerApp region broadcast/population helpers (EbenezerApp.cpp):
/// Send_Region/Send_UnitRegion and the 3×3 in/out sweeps a user receives when
/// entering the world or crossing a region border.
/// </summary>
public sealed partial class EbenezerWorld
{
    /// <summary>The C++ visits CENTER, NW, N, NE, W, E, SW, S, SE in this order.</summary>
    private static readonly (int Dx, int Dz)[] NineRegions =
    [
        (0, 0), (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1),
    ];

    /// <summary>EbenezerApp::Send_All — every in-game user (nation 0 = everyone).</summary>
    public void SendAll(ReadOnlySpan<byte> buf, GameUser? except = null, int nation = 0)
    {
        foreach (GameUser? user in Users)
        {
            if (user is null || user == except)
                continue;

            if (user.State != ConnectionState.GameStart)
                continue;

            if (nation == 0 || nation == user.UserData?.Nation)
                user.Send(buf);
        }
    }

    /// <summary>
    /// EbenezerApp::Send_NearRegion — the local-chat broadcast: the own region
    /// plus the three neighbours on the side the speaker stands on, filtered to
    /// 32 meters and always via the region buffer.
    /// </summary>
    public void SendNearRegion(ReadOnlySpan<byte> buf, int zone, int regionX, int regionZ,
        float curX, float curZ, GameUser? except = null)
    {
        GameZone? map = GetZoneById(zone);
        if (map is null)
            return;

        float leftBorder = regionX * GameZone.ViewDistance;
        float topBorder = regionZ * GameZone.ViewDistance;

        SendFilterUnitRegion(map, buf, regionX, regionZ, curX, curZ, except);

        int dx = curX - leftBorder > GameZone.ViewDistance / 2.0f ? 1 : -1;
        int dz = curZ - topBorder > GameZone.ViewDistance / 2.0f ? 1 : -1;

        SendFilterUnitRegion(map, buf, regionX + dx, regionZ, curX, curZ, except);
        SendFilterUnitRegion(map, buf, regionX, regionZ + dz, curX, curZ, except);
        SendFilterUnitRegion(map, buf, regionX + dx, regionZ + dz, curX, curZ, except);
    }

    /// <summary>EbenezerApp::Send_FilterUnitRegion — 32m distance filter, buffered.</summary>
    private void SendFilterUnitRegion(GameZone map, ReadOnlySpan<byte> buf, int x, int z,
        float refX, float refZ, GameUser? except)
    {
        if (!map.IsValidRegion(x, z))
            return;

        foreach (int uid in map.Regions[x, z].Users)
        {
            GameUser? user = uid >= 0 && uid < Users.Length ? Users[uid] : null;
            if (user is null || user == except)
                continue;

            if (user.State != ConnectionState.GameStart || user.UserData is not { } data)
                continue;

            double dist = Math.Sqrt(Math.Pow(data.CurX - refX, 2) + Math.Pow(data.CurZ - refZ, 2));
            if (dist < 32)
                user.RegionPacketAdd(buf);
        }
    }

    /// <summary>EbenezerApp::Send_Region — the 3×3 region block around (x, z). Like the C++, bDirect defaults to true.</summary>
    public void SendRegion(ReadOnlySpan<byte> buf, int zone, int x, int z, GameUser? except = null, bool direct = true)
    {
        GameZone? map = GetZoneById(zone);
        if (map is null)
            return;

        foreach ((int dx, int dz) in NineRegions)
            SendUnitRegion(map, buf, x + dx, z + dz, except, direct);
    }

    /// <summary>EbenezerApp::Send_UnitRegion — every in-game user of one region.</summary>
    public void SendUnitRegion(GameZone map, ReadOnlySpan<byte> buf, int x, int z, GameUser? except = null, bool direct = true)
    {
        if (!map.IsValidRegion(x, z))
            return;

        foreach (int uid in map.Regions[x, z].Users)
        {
            GameUser? user = uid >= 0 && uid < Users.Length ? Users[uid] : null;
            if (user is null || user == except)
                continue;

            if (user.State != ConnectionState.GameStart)
                continue;

            if (direct)
                user.Send(buf);
            else
                user.RegionPacketAdd(buf);
        }
    }

    /// <summary>EbenezerApp::UserInOutForMe — the full user-info download (always compressed).</summary>
    public void UserInOutForMe(GameUser sendUser)
    {
        GameZone? map = GetZoneByIndex(sendUser.ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[49152];
        var writer = new PacketWriter(buffer) { Index = 3 };
        int count = 0;

        foreach ((int dx, int dz) in NineRegions)
            GetRegionUserIn(map, sendUser.RegionX + dx, sendUser.RegionZ + dz, ref writer, ref count);

        buffer[0] = (byte)GameOpcode.WIZ_REQ_USERIN;
        buffer[1] = (byte)count;
        buffer[2] = (byte)(count >> 8);

        sendUser.SendCompressingPacket(buffer.AsSpan(0, writer.Index));
    }

    /// <summary>EbenezerApp::GetRegionUserIn — [uid][GetUserInfo] per in-game user of a region.</summary>
    private void GetRegionUserIn(GameZone map, int regionX, int regionZ, ref PacketWriter writer, ref int count)
    {
        if (!map.IsValidRegion(regionX, regionZ))
            return;

        foreach (int uid in map.Regions[regionX, regionZ].Users)
        {
            GameUser? user = uid >= 0 && uid < Users.Length ? Users[uid] : null;
            if (user is null)
                continue;

            if (user.RegionX != regionX || user.RegionZ != regionZ)
                continue;

            if (user.State != ConnectionState.GameStart)
                continue;

            writer.SetShort(user.SocketId);
            user.GetUserInfo(ref writer);
            count++;
        }
    }

    /// <summary>EbenezerApp::RegionUserInOutForMe — the WIZ_REGIONCHANGE uid list.</summary>
    public void RegionUserInOutForMe(GameUser sendUser)
    {
        GameZone? map = GetZoneByIndex(sendUser.ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[16384];
        var writer = new PacketWriter(buffer) { Index = 3 };
        int count = 0;

        foreach ((int dx, int dz) in NineRegions)
        {
            int regionX = sendUser.RegionX + dx;
            int regionZ = sendUser.RegionZ + dz;
            if (!map.IsValidRegion(regionX, regionZ))
                continue;

            foreach (int uid in map.Regions[regionX, regionZ].Users)
            {
                GameUser? user = uid >= 0 && uid < Users.Length ? Users[uid] : null;
                if (user is null || user.State != ConnectionState.GameStart)
                    continue;

                writer.SetShort(user.SocketId);
                count++;
            }
        }

        buffer[0] = (byte)GameOpcode.WIZ_REGIONCHANGE;
        buffer[1] = (byte)count;
        buffer[2] = (byte)(count >> 8);

        sendUser.Send(buffer.AsSpan(0, writer.Index));
    }

    /// <summary>EbenezerApp::NpcInOutForMe — the full NPC download (always compressed).</summary>
    public void NpcInOutForMe(GameUser sendUser)
    {
        GameZone? map = GetZoneByIndex(sendUser.ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[49152];
        var writer = new PacketWriter(buffer) { Index = 3 };
        int count = 0;

        foreach ((int dx, int dz) in NineRegions)
            GetRegionNpcIn(map, sendUser.RegionX + dx, sendUser.RegionZ + dz, ref writer, ref count);

        buffer[0] = (byte)GameOpcode.WIZ_REQ_NPCIN;
        buffer[1] = (byte)count;
        buffer[2] = (byte)(count >> 8);

        sendUser.SendCompressingPacket(buffer.AsSpan(0, writer.Index));
    }

    /// <summary>EbenezerApp::GetRegionNpcIn.</summary>
    private void GetRegionNpcIn(GameZone map, int regionX, int regionZ, ref PacketWriter writer, ref int count)
    {
        if (!PointCheckFlag)
            return;

        if (!map.IsValidRegion(regionX, regionZ))
            return;

        foreach (int nid in map.Regions[regionX, regionZ].Npcs)
        {
            if (nid < 0)
                continue;

            GameNpc? npc = Npcs.GetValueOrDefault(nid);
            if (npc is null)
                continue;

            if (npc.RegionX != regionX || npc.RegionZ != regionZ)
                continue;

            writer.SetShort(npc.Nid);
            npc.GetNpcInfo(ref writer);
            count++;
        }
    }

    /// <summary>EbenezerApp::RegionNpcInfoForMe — the WIZ_NPC_REGION nid list.</summary>
    public void RegionNpcInfoForMe(GameUser sendUser)
    {
        GameZone? map = GetZoneByIndex(sendUser.ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[16384];
        var writer = new PacketWriter(buffer) { Index = 3 };
        int count = 0;

        foreach ((int dx, int dz) in NineRegions)
        {
            int regionX = sendUser.RegionX + dx;
            int regionZ = sendUser.RegionZ + dz;

            if (!PointCheckFlag || !map.IsValidRegion(regionX, regionZ))
                continue;

            foreach (int nid in map.Regions[regionX, regionZ].Npcs)
            {
                if (nid < 0)
                    continue;

                GameNpc? npc = Npcs.GetValueOrDefault(nid);
                if (npc is null)
                    continue;

                writer.SetShort(npc.Nid);
                count++;
            }
        }

        buffer[0] = (byte)GameOpcode.WIZ_NPC_REGION;
        buffer[1] = (byte)count;
        buffer[2] = (byte)(count >> 8);

        sendUser.Send(buffer.AsSpan(0, writer.Index));
    }
}
