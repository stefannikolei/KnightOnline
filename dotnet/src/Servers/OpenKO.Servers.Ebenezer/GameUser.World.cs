using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser world/movement slice (User.cpp): the region packet buffer, the
/// user-info blob, movement/rotation, region transitions and the client
/// user/NPC list requests.
/// </summary>
public sealed partial class GameUser
{
    private const int RegionBuffSize = 16384; // REGION_BUFF_SIZE

    public const byte UserIn = 0x01;     // USER_IN
    public const byte UserOut = 0x02;    // USER_OUT
    public const byte UserRegene = 0x03; // USER_REGENE

    private const byte UserStanding = 1;         // USER_STANDING (e_UserResHpType)
    private const byte UserDeadResHpType = 3;    // USER_DEAD
    private const byte AbnormalBlinking = 3;     // ABNORMAL_BLINKING

    private readonly byte[] _regionBuffer = new byte[RegionBuffSize];
    private int _regionBufferLength;

    /// <summary>m_bResHpType.</summary>
    public byte ResHpType = UserStanding;

    /// <summary>m_bWarp: mid zone-change.</summary>
    public byte Warp;

    /// <summary>m_bNeedParty (starts at 1 like Initialize()).</summary>
    public byte NeedParty = 1;

    public bool IsPartyLeader;
    public byte InvisibilityState;
    public short Direction;
    public bool IsChicken;
    public byte KnightsRank;
    public byte PersonalRank;

    /// <summary>CUser::RegionPacketAdd — appends [len i16][data] to the region buffer.</summary>
    public void RegionPacketAdd(ReadOnlySpan<byte> buf)
    {
        if (_regionBufferLength + 2 + buf.Length > _regionBuffer.Length)
            return; // the C++ would overrun; drop instead

        var writer = new PacketWriter(_regionBuffer) { Index = _regionBufferLength };
        writer.SetShort(buf.Length);
        writer.SetString(buf);
        _regionBufferLength = writer.Index;
    }

    /// <summary>
    /// CUser::RegionPacketClear — drains the buffer as
    /// [WIZ_CONTINOUS_PACKET][len i16][entries]; returns the packet or null when empty.
    /// </summary>
    public byte[]? RegionPacketClear()
    {
        if (_regionBufferLength <= 0)
            return null;

        var packet = new byte[3 + _regionBufferLength];
        var writer = new PacketWriter(packet);
        writer.SetByte((byte)GameOpcode.WIZ_CONTINOUS_PACKET);
        writer.SetShort(_regionBufferLength);
        writer.SetString(_regionBuffer.AsSpan(0, _regionBufferLength));

        Array.Clear(_regionBuffer, 0, _regionBufferLength);
        _regionBufferLength = 0;
        return packet;
    }

    /// <summary>CUser::GetUserInfo — the per-user blob inside WIZ_USER_INOUT/WIZ_REQ_USERIN.</summary>
    public void GetUserInfo(ref PacketWriter writer)
    {
        if (UserData is not { } user)
            return;

        writer.SetString1(Encoding.Latin1.GetBytes(user.CharId));
        writer.SetByte(user.Nation);
        writer.SetShort(user.Knights);
        writer.SetByte(user.Fame);

        KnightsClan? clan = user.Knights != 0 ? world.Knights.GetValueOrDefault(user.Knights) : null;
        if (clan is not null)
        {
            writer.SetShort(clan.AllianceKnights);
            writer.SetString1(Encoding.Latin1.GetBytes(clan.Name));
            writer.SetByte(clan.Grade);
            writer.SetByte(clan.Ranking);
            writer.SetShort(clan.MarkVersion);
            writer.SetShort(clan.Cape);
        }
        else
        {
            writer.SetShort(0);  // alliance knights
            writer.SetByte(0);   // clan name (empty SetString1)
            writer.SetByte(0);   // grade
            writer.SetByte(0);   // ranking
            writer.SetShort(0);  // mark version
            writer.SetShort(-1); // cape
        }

        writer.SetByte(user.Level);
        writer.SetByte(user.Race);
        writer.SetShort(user.Class);
        writer.SetShort((short)(ushort)(user.CurX * 10));
        writer.SetShort((short)(ushort)(user.CurZ * 10));
        writer.SetShort((short)(user.CurY * 10));
        writer.SetByte(user.Face);
        writer.SetByte(user.HairColor);
        writer.SetByte(ResHpType);
        writer.SetDWord(AbnormalType);
        writer.SetByte(NeedParty);
        writer.SetByte(user.Authority);
        writer.SetByte(IsPartyLeader ? (byte)1 : (byte)0);
        writer.SetByte(InvisibilityState);
        writer.SetShort(Direction);
        writer.SetByte(IsChicken ? (byte)1 : (byte)0);
        writer.SetByte(user.Rank);
        writer.SetByte(KnightsRank);
        writer.SetByte(PersonalRank);

        // Visible equipment: BREAST, LEG, HEAD, GLOVE, FOOT, SHOULDER, RIGHTHAND, LEFTHAND.
        foreach (int slot in (int[])[
            GameConstants.SlotBreast, GameConstants.SlotLeg, GameConstants.SlotHead,
            GameConstants.SlotGlove, GameConstants.SlotFoot, GameConstants.SlotShoulder,
            GameConstants.SlotRightHand, GameConstants.SlotLeftHand])
        {
            writer.SetDWord((uint)user.Items[slot].Num);
            writer.SetShort(user.Items[slot].Duration);
            writer.SetByte(user.Items[slot].Flag);
        }
    }

    /// <summary>CUser::SendUserInfo — the per-user blob inside AG_USER_INFO_ALL.</summary>
    public void SendUserInfo(ref PacketWriter writer)
    {
        if (UserData is not { } user)
            return;

        writer.SetShort(SocketId);
        writer.SetString2(Encoding.Latin1.GetBytes(user.CharId));
        writer.SetByte(user.Zone);
        writer.SetShort((short)ZoneIndex);
        writer.SetByte(user.Nation);
        writer.SetByte(user.Level);
        writer.SetShort(user.Hp);
        writer.SetShort(user.Mp);
        writer.SetShort((short)(TotalHit * AttackAmount / 100));
        writer.SetShort((short)(TotalAc + AcAmount));
        writer.SetFloat(TotalHitRate);
        writer.SetFloat(TotalEvasionRate);
        writer.SetShort(PartyIndex);
        writer.SetByte(user.Authority);
    }

    /// <summary>CUser::MoveProcess.</summary>
    public void MoveProcess(ReadOnlySpan<byte> body)
    {
        if (Warp != 0)
            return;

        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        ushort willX = (ushort)reader.GetShort();
        ushort willZ = (ushort)reader.GetShort();
        short willY = reader.GetShort();
        short speed = reader.GetShort();
        byte echo = reader.GetByte();

        float realX = willX / 10.0f;
        float realZ = willZ / 10.0f;
        float realY = willY / 10.0f;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        if (!map.IsValidPosition(realX, realZ))
            return;

        if ((ResHpType == UserDeadResHpType || user.Hp == 0) && speed != 0)
        {
            logger.LogWarning("MoveProcess: dead user is moving [charId={CharId} socketId={SocketId} hp={Hp}]",
                user.CharId, SocketId, user.Hp);
        }

        if (speed != 0)
        {
            // Promote the buffered next position, remember the new one.
            user.CurX = WillX;
            user.CurZ = WillZ;
            user.CurY = WillY;

            WillX = realX;
            WillZ = realZ;
            WillY = realY;
        }
        else
        {
            user.CurX = WillX = realX;
            user.CurZ = WillZ = realZ;
            user.CurY = WillY = realY;
        }

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MOVE);
        writer.SetShort(SocketId);
        writer.SetShort((short)willX);
        writer.SetShort((short)willZ);
        writer.SetShort(willY);
        writer.SetShort(speed);
        writer.SetByte(echo);

        RegisterRegion();
        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, except: null, direct: false);

        // C3DMap::CheckEvent (trap/teleport tiles) attaches with the event slice.

        var aiBuffer = new byte[32];
        var aiWriter = new PacketWriter(aiBuffer);
        aiWriter.SetByte(AiOpcode.AG_USER_MOVE);
        aiWriter.SetShort(SocketId);
        aiWriter.SetFloat(WillX);
        aiWriter.SetFloat(WillZ);
        aiWriter.SetFloat(WillY);
        aiWriter.SetShort(speed);
        world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
    }

    /// <summary>CUser::Rotate.</summary>
    public void Rotate(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        Direction = reader.GetShort();

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ROTATE);
        writer.SetShort(SocketId);
        writer.SetShort(Direction);

        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, except: null, direct: false);
    }

    /// <summary>CUser::UserInOut — region membership + the in/out broadcast.</summary>
    public void UserInOut(byte type)
    {
        if (UserData is not { } user)
            return;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        if (type == UserOut)
            map.RegionUserRemove(RegionX, RegionZ, SocketId);
        else
            map.RegionUserAdd(RegionX, RegionZ, SocketId);

        var buffer = new byte[1024];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_USER_INOUT);
        writer.SetByte(type);
        writer.SetShort(SocketId);

        if (type == UserOut)
        {
            world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, this);
            SendAiUserInOut(type, user);
            return;
        }

        GetUserInfo(ref writer);
        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, this);

        if (AbnormalType != AbnormalBlinking)
            SendAiUserInOut(type, user);
    }

    private void SendAiUserInOut(byte type, OpenKO.Data.Models.UserData user)
    {
        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_INOUT);
        writer.SetByte(type);
        writer.SetShort(SocketId);
        writer.SetString2(Encoding.Latin1.GetBytes(user.CharId));
        writer.SetFloat(user.CurX);
        writer.SetFloat(user.CurZ);
        world.SendToAiServer?.Invoke(user.Zone, writer.Written.ToArray());
    }

    /// <summary>CUser::RegisterRegion — border crossing bookkeeping + delta broadcasts.</summary>
    public void RegisterRegion()
    {
        if (UserData is not { } user)
            return;

        var regX = (short)(user.CurX / GameZone.ViewDistance);
        var regZ = (short)(user.CurZ / GameZone.ViewDistance);

        if (RegionX == regX && RegionZ == regZ)
            return;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        short oldRegionX = RegionX;
        short oldRegionZ = RegionZ;

        map.RegionUserRemove(RegionX, RegionZ, SocketId);
        RegionX = regX;
        RegionZ = regZ;
        map.RegionUserAdd(RegionX, RegionZ, SocketId);

        if (State == ConnectionState.GameStart)
        {
            // The delete sweep runs against the movement direction, the add sweep with it.
            RemoveRegion(oldRegionX - RegionX, oldRegionZ - RegionZ);
            InsertRegion(RegionX - oldRegionX, RegionZ - oldRegionZ);

            world.RegionNpcInfoForMe(this);
            world.RegionUserInOutForMe(this);
        }
    }

    /// <summary>CUser::RemoveRegion — USER_OUT to the regions left behind.</summary>
    public void RemoveRegion(int delX, int delZ)
    {
        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_USER_INOUT);
        writer.SetByte(UserOut);
        writer.SetShort(SocketId);
        ReadOnlySpan<byte> packet = writer.Written;

        if (delX != 0)
        {
            world.SendUnitRegion(map, packet, RegionX + delX * 2, RegionZ + delZ - 1);
            world.SendUnitRegion(map, packet, RegionX + delX * 2, RegionZ + delZ);
            world.SendUnitRegion(map, packet, RegionX + delX * 2, RegionZ + delZ + 1);
        }

        if (delZ != 0)
        {
            world.SendUnitRegion(map, packet, RegionX + delX, RegionZ + delZ * 2);

            if (delX < 0)
            {
                world.SendUnitRegion(map, packet, RegionX + delX + 1, RegionZ + delZ * 2);
            }
            else if (delX > 0)
            {
                world.SendUnitRegion(map, packet, RegionX + delX - 1, RegionZ + delZ * 2);
            }
            else
            {
                world.SendUnitRegion(map, packet, RegionX + delX - 1, RegionZ + delZ * 2);
                world.SendUnitRegion(map, packet, RegionX + delX + 1, RegionZ + delZ * 2);
            }
        }
    }

    /// <summary>CUser::InsertRegion — USER_IN + user info to the regions entered.</summary>
    public void InsertRegion(int delX, int delZ)
    {
        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[1024];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_USER_INOUT);
        writer.SetByte(UserIn);
        writer.SetShort(SocketId);
        GetUserInfo(ref writer);
        ReadOnlySpan<byte> packet = writer.Written;

        if (delX != 0)
        {
            world.SendUnitRegion(map, packet, RegionX + delX, RegionZ - 1);
            world.SendUnitRegion(map, packet, RegionX + delX, RegionZ);
            world.SendUnitRegion(map, packet, RegionX + delX, RegionZ + 1);
        }

        if (delZ != 0)
        {
            world.SendUnitRegion(map, packet, RegionX, RegionZ + delZ);

            if (delX < 0)
            {
                world.SendUnitRegion(map, packet, RegionX + 1, RegionZ + delZ);
            }
            else if (delX > 0)
            {
                world.SendUnitRegion(map, packet, RegionX - 1, RegionZ + delZ);
            }
            else
            {
                world.SendUnitRegion(map, packet, RegionX - 1, RegionZ + delZ);
                world.SendUnitRegion(map, packet, RegionX + 1, RegionZ + delZ);
            }
        }
    }

    /// <summary>CUser::RequestUserIn — resolves a uid list into full user infos.</summary>
    public void RequestUserIn(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        var buffer = new byte[40960];
        var writer = new PacketWriter(buffer) { Index = 3 };
        int count = 0;

        int requested = reader.GetShort();
        for (int i = 0; i < requested; i++)
        {
            short uid = reader.GetShort();
            if (i > 1000)
                break;

            GameUser? user = uid >= 0 && uid < world.Users.Length ? world.Users[uid] : null;
            if (user is null || user.State != ConnectionState.GameStart)
                continue;

            writer.SetShort(user.SocketId);
            user.GetUserInfo(ref writer);
            count++;
        }

        buffer[0] = (byte)GameOpcode.WIZ_REQ_USERIN;
        buffer[1] = (byte)count;
        buffer[2] = (byte)(count >> 8);

        if (writer.Index < 500)
            Send(buffer.AsSpan(0, writer.Index));
        else
            SendCompressingPacket(buffer.AsSpan(0, writer.Index));
    }

    /// <summary>CUser::RequestNpcIn — resolves a nid list into full NPC infos.</summary>
    public void RequestNpcIn(ReadOnlySpan<byte> body)
    {
        if (!world.PointCheckFlag)
            return;

        var reader = new PacketReader(body);

        var buffer = new byte[20480];
        var writer = new PacketWriter(buffer) { Index = 3 };
        int count = 0;

        int requested = reader.GetShort();
        for (int i = 0; i < requested; i++)
        {
            short nid = reader.GetShort();
            if (nid < 0 || nid > 20000) // NPC_BAND + NPC_BAND
                continue;

            if (i > 1000)
                break;

            GameNpc? npc = world.Npcs.GetValueOrDefault(nid);
            if (npc is null)
                continue;

            writer.SetShort(npc.Nid);
            npc.GetNpcInfo(ref writer);
            count++;
        }

        buffer[0] = (byte)GameOpcode.WIZ_REQ_NPCIN;
        buffer[1] = (byte)count;
        buffer[2] = (byte)(count >> 8);

        if (writer.Index < 500)
            Send(buffer.AsSpan(0, writer.Index));
        else
            SendCompressingPacket(buffer.AsSpan(0, writer.Index));
    }
}
