using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// Port of <c>CNpcMagicProcess</c> (Server/AIServer/NpcMagicProcess.cpp). Each NPC
/// owns one processor (m_MagicProcess) with itself as the source (m_pSrcNpc).
/// Only Type3 (direct damage/heal) is actually implemented in the C++; the other
/// ExecuteType* bodies are empty and stay that way here.
/// </summary>
public sealed class NpcMagicProcessor(Npc srcNpc)
{
    // e_MagicState (Define.h).
    public const byte StateNone = 0x01;    // NONE
    public const byte StateCasting = 0x02; // CASTING

    // e_MagicOpcode (Define.h).
    private const byte MagicCasting = 1;   // MAGIC_CASTING
    private const byte MagicEffecting = 3; // MAGIC_EFFECTING
    private const byte MagicFail = 4;      // MAGIC_FAIL

    // MORAL_* (Define.h).
    private const byte MoralSelf = 1;
    private const byte MoralFriendWithMe = 2;
    private const byte MoralFriendExceptMe = 3;
    private const byte MoralParty = 4;
    private const byte MoralNpc = 5;
    private const byte MoralPartyAll = 6;
    private const byte MoralEnemy = 7;
    private const byte MoralAreaEnemy = 10;

    // Attack attributes NONE_R..DARKNESS_R (Define.h).
    private const int NoneR = 0;
    private const int FireR = 1;
    private const int ColdR = 2;
    private const int LightningR = 3;
    private const int MagicR = 4;
    private const int DiseaseR = 5;
    private const int PoisonR = 6;
    private const int LightR = 7;
    private const int DarknessR = 8;

    // Hit results (Define.h).
    private const byte Success = 2; // SUCCESS
    private const byte Fail = 4;    // FAIL

    private const int NpcBand = 10000;     // NPC_BAND
    private const int InvalidBand = 20000; // INVALID_BAND

    // Gate-like NPC types that magic damage ignores (Define.h NPC_*).
    private const byte NpcTypePhoenixGate = 51; // NPC_PHOENIX_GATE
    private const byte NpcTypeSpecialGate = 52; // NPC_SPECIAL_GATE
    private const byte NpcTypeGateLever = 55;   // NPC_GATE_LEVER
    private const byte NpcTypeArtifact = 60;    // NPC_ARTIFACT

    private readonly Npc _srcNpc = srcNpc;

    /// <summary>m_bMagicState.</summary>
    public byte MagicState = StateNone;

    private AiWorld? World => _srcNpc.World;

    private int MyRand(int min, int max) => World?.Rand(min, max) ?? min;

    /// <summary>
    /// CNpcMagicProcess::MagicPacket. <paramref name="buf"/> is the raw payload
    /// [command][magicid u32][sid i16][tid i16][data1..data6 i16] the NPC AI builds.
    /// </summary>
    public void MagicPacket(ReadOnlySpan<byte> buf)
    {
        var reader = new PacketReader(buf);
        byte command = reader.GetByte();

        if (command == MagicFail)
        {
            // The C++ builds an AG_MAGIC_ATTACK_RESULT echo here, but the actual
            // send (Send_Region) is commented out — only the state reset survives.
            MagicState = StateNone;
            return;
        }

        int magicId = (int)reader.GetDWord();
        int sid = reader.GetShort();
        int tid = reader.GetShort();
        int data1 = reader.GetShort();
        int data2 = reader.GetShort();
        int data3 = reader.GetShort();
        int data4 = reader.GetShort();
        int data5 = reader.GetShort();
        int data6 = reader.GetShort();

        Magic? table = IsAvailable(magicId, tid, command);
        if (table is null)
            return;

        if (command == MagicEffecting)
        {
            switch (table.Type1)
            {
                case 1:
                    ExecuteType1(table.ID, tid, data1, data2, data3);
                    break;
                case 2:
                    ExecuteType2(table.ID, tid, data1, data2, data3);
                    break;
                case 3:
                    ExecuteType3(table.ID, tid, data1, data2, data3, table.Moral);
                    break;
                case 4:
                    ExecuteType4(table.ID, tid);
                    break;
                case 5:
                    ExecuteType5(table.ID);
                    break;
                case 6:
                    ExecuteType6(table.ID);
                    break;
                case 7:
                    ExecuteType7(table.ID);
                    break;
                case 8:
                    ExecuteType8(table.ID, tid, sid, data1, data2, data3);
                    break;
                case 9:
                    ExecuteType9(table.ID);
                    break;
                case 10:
                    ExecuteType10(table.ID);
                    break;
            }

            switch (table.Type2)
            {
                case 1:
                    // Quirk kept: the Type2 dispatch passes data4/5/6 for case 1 only.
                    ExecuteType1(table.ID, tid, data4, data5, data6);
                    break;
                case 2:
                    ExecuteType2(table.ID, tid, data1, data2, data3);
                    break;
                case 3:
                    ExecuteType3(table.ID, tid, data1, data2, data3, table.Moral);
                    break;
                case 4:
                    ExecuteType4(table.ID, tid);
                    break;
                case 5:
                    ExecuteType5(table.ID);
                    break;
                case 6:
                    ExecuteType6(table.ID);
                    break;
                case 7:
                    ExecuteType7(table.ID);
                    break;
                case 8:
                    ExecuteType8(table.ID, tid, sid, data1, data2, data3);
                    break;
                case 9:
                    ExecuteType9(table.ID);
                    break;
                case 10:
                    ExecuteType10(table.ID);
                    break;
            }
        }
        else if (command == MagicCasting)
        {
            // Echo as AG_MAGIC_ATTACK_RESULT. Quirk kept: the C++ copies len-1
            // bytes starting at the command byte, so the last byte is dropped.
            var send = new byte[buf.Length];
            send[0] = AiOpcode.AG_MAGIC_ATTACK_RESULT;
            buf[..^1].CopyTo(send.AsSpan(1));
            _srcNpc.SendAll(send);
        }
    }

    /// <summary>CNpcMagicProcess::IsAvailable — moral/validity checks, fail packet on reject.</summary>
    private Magic? IsAvailable(int magicId, int tid, byte type)
    {
        AiUser? user = null;
        Npc? npc = null;
        int moral;

        Magic? table = World?.MagicTable.GetValueOrDefault(magicId);
        if (table is null)
            return FailReturn(magicId, tid, type);

        // Compare morals between source and target character.
        if (tid >= 0 && tid < AiConstants.MaxUser)
        {
            user = World?.GetUser(tid);
            if (user is null || user.Live == AiUser.UserDead)
                return FailReturn(magicId, tid, type);

            moral = user.Nation;
        }
        // Compare morals between source and target NPC.
        else if (tid >= NpcBand)
        {
            npc = World?.Npcs.GetValueOrDefault(tid - NpcBand);
            if (npc is null || npc.State == NpcState.Dead)
                return FailReturn(magicId, tid, type);

            moral = npc.Group;
        }
        // Area spells (tid == -1): monsters have no nation, so AREA_ENEMY flips groups.
        else if (tid == -1)
        {
            if (table.Moral == MoralAreaEnemy)
                moral = _srcNpc.Group == 0 ? 2 : 1;
            else
                moral = _srcNpc.Group;
        }
        else
        {
            moral = _srcNpc.Group;
        }

        switch (table.Moral)
        {
            case MoralSelf:
                if (tid != _srcNpc.Nid + NpcBand)
                    return FailReturn(magicId, tid, type);
                break;

            case MoralFriendWithMe:
                if (_srcNpc.Group != moral)
                    return FailReturn(magicId, tid, type);
                break;

            case MoralFriendExceptMe:
                if (_srcNpc.Group != moral)
                    return FailReturn(magicId, tid, type);
                if (tid == _srcNpc.Nid + NpcBand)
                    return FailReturn(magicId, tid, type);
                break;

            case MoralParty:
            case MoralPartyAll:
                break;

            case MoralNpc:
                if (npc is null || npc.Group != moral)
                    return FailReturn(magicId, tid, type);
                break;

            case MoralEnemy:
                if (_srcNpc.Group == moral)
                    return FailReturn(magicId, tid, type);
                break;
        }

        // NPCs have no MP pool to charge — the MP check is commented out in the C++.
        return table;
    }

    /// <summary>The fail_return block: AG_MAGIC_ATTACK_RESULT with MAGIC_FAIL.</summary>
    private Magic? FailReturn(int magicId, int tid, byte type)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicFail);
        writer.SetDWord((uint)magicId);
        writer.SetShort(_srcNpc.Nid + NpcBand);
        writer.SetShort(tid);
        writer.SetShort(type == MagicCasting ? -100 : 0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);

        _srcNpc.SendAll(writer.Written);
        MagicState = StateNone;
        return null;
    }

    // Applied to an attack skill using a weapon. Empty in the C++.
    private void ExecuteType1(int magicId, int tid, int data1, int data2, int data3)
    {
    }

    private void ExecuteType2(int magicId, int tid, int data1, int data2, int data3)
    {
    }

    /// <summary>
    /// CNpcMagicProcess::ExecuteType3 — magical attack / healing. Only the heal path
    /// (positive damage on an NPC target) is live; the user-damage path is commented
    /// out in the C++ because area attacks on users are handled by the game server.
    /// </summary>
    private void ExecuteType3(int magicId, int tid, int data1, int data2, int data3, int moral)
    {
        int result = 1;

        Magic? magic = World?.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        // Area attacks (tid == -1) skip straight to the broadcast; monsters' area
        // attacks against users are resolved by the game server.
        if (tid != -1)
        {
            Npc? npc = World?.Npcs.GetValueOrDefault(tid - NpcBand);
            if (npc is null || npc.State == NpcState.Dead || npc.HP == 0)
            {
                result = 0;
            }
            else
            {
                MagicType3? type = World?.MagicType3Table.GetValueOrDefault(magicId);
                if (type is null)
                    return; // no broadcast, like the C++

                const int dexpoint = 0;
                int damage = GetMagicDamage(tid, type.FirstDamage, type.Attribute, dexpoint);

                // Non-durational spells; the durational branch is empty in the C++.
                if (type.Duration == 0 && type.DirectType == 1 && damage > 0)
                    result = npc.SetHMagicDamage(damage) ? 1 : 0;
                // The negative-damage (attack on NPC) path is commented out in the C++.
            }
        }

        // packet_send:
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(_srcNpc.Nid + NpcBand);
        writer.SetShort(tid);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);
        writer.SetShort(moral);
        writer.SetShort(0);
        writer.SetShort(0);
        _srcNpc.SendAll(writer.Written);
    }

    private void ExecuteType4(int magicId, int tid)
    {
    }

    private void ExecuteType5(int magicId)
    {
    }

    private void ExecuteType6(int magicId)
    {
    }

    private void ExecuteType7(int magicId)
    {
    }

    // Warp, resurrection, and summon spells. Empty in the C++.
    private void ExecuteType8(int magicId, int tid, int sid, int data1, int data2, int data3)
    {
    }

    private void ExecuteType9(int magicId)
    {
    }

    private void ExecuteType10(int magicId)
    {
    }

    /// <summary>CNpcMagicProcess::GetMagicDamage — only valid for NPC-band targets.</summary>
    public short GetMagicDamage(int tid, int totalHit, int attribute, int dexpoint)
    {
        short damage = 0;
        int totalR = 0;
        bool sign = true; // false → negative result

        // Quirk kept: the C++ uses '> INVALID_BAND', not '>='.
        if (tid < NpcBand || tid > InvalidBand)
            return 0;

        Npc? npc = World?.Npcs.GetValueOrDefault(tid - NpcBand);
        if (npc is null || npc.State == NpcState.Dead || npc.HP == 0)
            return 0;

        if (npc.NpcType is NpcTypeArtifact or NpcTypePhoenixGate or NpcTypeGateLever or NpcTypeSpecialGate)
            return 0;

        // The hit-rate roll is commented out in the C++ — always SUCCESS.
        byte result = Success;

        if (result != Fail)
        {
            switch (attribute)
            {
                case NoneR:
                    totalR = 0;
                    break;
                case FireR:
                    totalR = npc.FireResist;
                    break;
                case ColdR:
                    totalR = npc.ColdResist;
                    break;
                case LightningR:
                    totalR = npc.LightningResist;
                    break;
                case MagicR:
                    totalR = npc.MagicResist;
                    break;
                case DiseaseR:
                    totalR = npc.DiseaseResist;
                    break;
                case PoisonR:
                    totalR = npc.PoisonResist;
                    break;
                case LightR:
                case DarknessR:
                    // LATER !!! (unimplemented in the C++)
                    break;
            }

            totalHit = (totalHit * (dexpoint + 20)) / 170;

            if (totalHit < 0)
            {
                totalHit = Math.Abs(totalHit);
                sign = false;
            }

            damage = (short)(totalHit - (0.7f * totalHit * totalR / 200));
            // myrand(0, damage): with damage < 0 the C++ modulo is UB — guard to 0.
            int random = damage > 0 ? MyRand(0, damage) : 0;
            damage = (short)((0.7f * (totalHit - (0.9f * totalHit * totalR / 200))) + 0.2f * random);
        }
        else
        {
            damage = 0;
        }

        if (!sign && damage != 0)
            damage = (short)-damage;

        return damage;
    }
}
