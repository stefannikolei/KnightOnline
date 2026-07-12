using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Port of the Ebenezer-side <c>CNpc</c> (Server/Ebenezer/Npc.h) — a data mirror
/// of the AIServer's NPCs, filled from the AG_* packets (stage 4.5 AISocket).
/// Field defaults follow CNpc::Initialize().
/// </summary>
public sealed class GameNpc
{
    public const byte NpcIn = 0x01;   // NPC_IN
    public const byte NpcOut = 0x02;  // NPC_OUT

    public const byte StateDead = 0;  // NPC_DEAD
    public const byte StateLive = 1;  // NPC_LIVE

    public const byte SpecialObject = 1; // SPECIAL_OBJECT

    // e_NpcType gate variants (shared/globals.h).
    public const byte TypeGate = 50;        // NPC_GATE
    public const byte TypePhoenixGate = 51; // NPC_PHOENIX_GATE
    public const byte TypeSpecialGate = 52; // NPC_SPECIAL_GATE

    public short Nid = -1;
    public short Sid;
    public short CurZone = -1;
    public short ZoneIndex = -1;
    public float CurX;
    public float CurY;
    public float CurZ;
    public short Pid;
    public short Size = 100;
    public int Weapon1;
    public int Weapon2;
    public string Name = string.Empty;
    public int MaxHP;
    public int HP;
    public byte State;
    public byte Group;
    public byte Level;
    public byte NpcType;
    public int SellingGroup;
    public short RegionX;
    public short RegionZ;
    public byte NpcState = StateLive;
    public byte GateOpen = 1;
    public short HitRate;
    public byte ObjectType;
    public byte Direction;
    public short Event = -1;
    public byte TrapNumber;

    /// <summary>CNpc::GetNpcInfo — the per-NPC blob inside WIZ_REQ_NPCIN/NPC_INFO.</summary>
    public void GetNpcInfo(ref PacketWriter writer)
    {
        writer.SetShort(Pid);
        writer.SetByte(NpcType);
        writer.SetDWord((uint)SellingGroup);
        writer.SetShort(Size);
        writer.SetDWord((uint)Weapon1);
        writer.SetDWord((uint)Weapon2);
        writer.SetString1(Encoding.Latin1.GetBytes(Name));
        writer.SetByte(Group);
        writer.SetByte(Level);
        writer.SetShort((short)(ushort)(CurX * 10));
        writer.SetShort((short)(ushort)(CurZ * 10));
        writer.SetShort((short)(CurY * 10));
        writer.SetDWord(GateOpen);
        writer.SetByte(ObjectType);
        writer.SetShort(0); // client: sIDK0
        writer.SetShort(0); // client: sIDK1
        writer.SetByte(Direction);
    }

    /// <summary>
    /// CNpc::MoveResult — apply an AIServer movement update and buffer the
    /// WIZ_NPC_MOVE broadcast. The C++ casts the floats to int16 *before*
    /// multiplying by 10 ((uint16_t)m_fCurX * 10), so the client only ever sees
    /// whole-meter NPC positions here.
    /// </summary>
    public void MoveResult(EbenezerWorld world, float x, float y, float z, float speed)
    {
        CurX = x;
        CurZ = z;
        CurY = y;

        RegisterRegion(world);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NPC_MOVE);
        writer.SetShort(Nid);
        writer.SetShort((short)((ushort)CurX * 10));
        writer.SetShort((short)((ushort)CurZ * 10));
        writer.SetShort((short)((short)CurY * 10));
        writer.SetShort((short)((short)speed * 10));

        world.SendRegion(writer.Written, CurZone, RegionX, RegionZ, except: null, direct: false);
    }

    /// <summary>CNpc::NpcInOut — region membership + the WIZ_NPC_INOUT broadcast (direct).</summary>
    public void NpcInOut(EbenezerWorld world, byte type, float fx = 0f, float fz = 0f, float fy = 0f)
    {
        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        if (type == NpcOut)
        {
            map.RegionNpcRemove(RegionX, RegionZ, Nid);
        }
        else
        {
            map.RegionNpcAdd(RegionX, RegionZ, Nid);

            CurX = fx;
            CurZ = fz;
            CurY = fy;
        }

        var buffer = new byte[1024];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NPC_INOUT);
        writer.SetByte(type);
        writer.SetShort(Nid);

        if (type == NpcOut)
        {
            world.SendRegion(writer.Written, CurZone, RegionX, RegionZ);
            return;
        }

        GetNpcInfo(ref writer);
        world.SendRegion(writer.Written, CurZone, RegionX, RegionZ);
    }

    /// <summary>CNpc::RegisterRegion — border crossing bookkeeping + delta broadcasts.</summary>
    public void RegisterRegion(EbenezerWorld world)
    {
        var regX = (short)(CurX / GameZone.ViewDistance);
        var regZ = (short)(CurZ / GameZone.ViewDistance);

        if (RegionX == regX && RegionZ == regZ)
            return;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        short oldRegionX = RegionX;
        short oldRegionZ = RegionZ;

        map.RegionNpcRemove(RegionX, RegionZ, Nid);
        RegionX = regX;
        RegionZ = regZ;
        map.RegionNpcAdd(RegionX, RegionZ, Nid);

        // The delete sweep runs against the movement direction, the add sweep with it.
        RemoveRegion(world, oldRegionX - RegionX, oldRegionZ - RegionZ);
        InsertRegion(world, RegionX - oldRegionX, RegionZ - oldRegionZ);
    }

    /// <summary>CNpc::RemoveRegion — NPC_OUT to the regions left behind (direct).</summary>
    public void RemoveRegion(EbenezerWorld world, int delX, int delZ)
    {
        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NPC_INOUT);
        writer.SetByte(NpcOut);
        writer.SetShort(Nid);
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

    /// <summary>CNpc::InsertRegion — NPC_IN + NPC info to the regions entered (direct).</summary>
    public void InsertRegion(EbenezerWorld world, int delX, int delZ)
    {
        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        var buffer = new byte[1024];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_NPC_INOUT);
        writer.SetByte(NpcIn);
        writer.SetShort(Nid);
        GetNpcInfo(ref writer);
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
}
