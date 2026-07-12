using System.Text;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Port of the Ebenezer-side <c>CNpc</c> (Server/Ebenezer/Npc.h) — a data mirror
/// of the AIServer's NPCs, filled from the AG_* packets (stage 4.5 AISocket).
/// </summary>
public sealed class GameNpc
{
    public short Nid;
    public short Sid;
    public short CurZone;
    public short ZoneIndex;
    public float CurX;
    public float CurY;
    public float CurZ;
    public short Pid;
    public short Size;
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
    public byte NpcState;
    public byte GateOpen;
    public short HitRate;
    public byte ObjectType;
    public byte Direction;
    public short Event;
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
}
