using System.Numerics;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// Port of <c>CMagicProcess</c> (Server/AIServer/MagicProcess.cpp) — user-cast
/// magic resolved on the AI side. Each AiUser owns one processor (m_MagicProcess)
/// fed by AG_MAGIC_ATTACK_REQ. All broadcasts go to the user's zone game server
/// (the C++ m_pSrcUser-&gt;SendAll and m_pMain-&gt;Send(..., m_curZone) both resolve
/// to that same socket).
/// </summary>
public sealed class UserMagicProcessor(AiUser srcUser)
{
    // e_MagicState (Define.h).
    public const byte StateNone = 0x01; // NONE

    // e_MagicOpcode (Define.h).
    private const byte MagicEffecting = 3; // MAGIC_EFFECTING
    private const byte MagicFail = 4;      // MAGIC_FAIL

    // Attack results / types (Define.h).
    private const byte AttackSuccess = 1;         // ATTACK_SUCCESS
    private const byte AttackTargetDead = 2;      // ATTACK_TARGET_DEAD
    private const byte MagicAttackTargetDead = 4; // MAGIC_ATTACK_TARGET_DEAD
    private const byte MagicAttack = 2;           // MAGIC_ATTACK (attack type)

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

    private const int UserBand = 0;        // USER_BAND
    private const int NpcBand = 10000;     // NPC_BAND
    private const int InvalidBand = 20000; // INVALID_BAND

    private const byte NpcTypePhoenixGate = 51; // NPC_PHOENIX_GATE
    private const byte NpcTypeSpecialGate = 52; // NPC_SPECIAL_GATE
    private const byte NpcTypeGateLever = 55;   // NPC_GATE_LEVER
    private const byte NpcTypeArtifact = 60;    // NPC_ARTIFACT

    // Weather (shared/packets.h e_WeatherType / Define.h ATTRIBUTE_*).
    private const int WeatherFine = 0x01;
    private const int WeatherRain = 0x02;
    private const int WeatherSnow = 0x03;
    private const short AttributeFire = 1;
    private const short AttributeIce = 2;
    private const short AttributeLightning = 3;

    private const int DefenceSlot = 0x02; // DEFENCE (item wear)

    private readonly AiUser _srcUser = srcUser;

    /// <summary>m_bMagicState.</summary>
    public byte MagicState = StateNone;

    /// <summary>AIServerApp::_weatherType (set from AG_TIME_WEATHER); 0 = no buff.</summary>
    public Func<int>? GetWeatherType;

    private AiWorld? World => _srcUser.World;

    private AiZone? Zone
        => World is { } w && _srcUser.ZoneIndex >= 0 && _srcUser.ZoneIndex < w.Zones.Count
            ? w.Zones[_srcUser.ZoneIndex]
            : null;

    private int MyRand(int min, int max) => World?.Rand(min, max) ?? min;

    private double TimeGet() => World?.Clock() ?? 0.0;

    /// <summary>
    /// CMagicProcess::MagicPacket. <paramref name="buf"/> is the AG_MAGIC_ATTACK_REQ
    /// payload after the user id: [command][tid i16][magicid u32][data1..data6 i16]
    /// [totalDex i16][righthandDamage i16].
    /// </summary>
    public void MagicPacket(ReadOnlySpan<byte> buf)
    {
        int result = 1;

        var reader = new PacketReader(buf);
        byte command = reader.GetByte();
        int tid = reader.GetShort();
        int magicId = (int)reader.GetDWord();
        int data1 = reader.GetShort();
        int data2 = reader.GetShort();
        int data3 = reader.GetShort();
        int data4 = reader.GetShort();
        int data5 = reader.GetShort();
        int data6 = reader.GetShort();
        int totalDex = reader.GetShort();
        int righthandDamage = reader.GetShort();

        Magic? table = IsAvailable(magicId, tid, command);
        if (table is null)
            return;

        if (command != MagicEffecting)
            return;

        switch (table.Type1)
        {
            case 1:
                result = ExecuteType1(table.ID, tid, data1, data2, data3, 1);
                break;
            case 2:
                result = ExecuteType2(table.ID, tid, data1, data2, data3);
                break;
            case 3:
                ExecuteType3(table.ID, tid, data1, data2, data3, table.Moral, totalDex, righthandDamage);
                break;
            case 4:
                ExecuteType4(table.ID, _srcUser.Uid, tid, data1, data2, data3, table.Moral);
                break;
            case 5:
                ExecuteType5(table.ID);
                break;
            case 6:
                ExecuteType6(table.ID);
                break;
            case 7:
                ExecuteType7(table.ID, tid, data1, data2, data3, table.Moral);
                break;
            case 8:
                ExecuteType8(table.ID);
                break;
            case 9:
                ExecuteType9(table.ID);
                break;
            case 10:
                ExecuteType10(table.ID);
                break;
        }

        if (result == 0)
            return;

        switch (table.Type2)
        {
            case 1:
                // Quirk kept: the Type2 dispatch passes data4/5/6 for case 1 only.
                ExecuteType1(table.ID, tid, data4, data5, data6, 2);
                break;
            case 2:
                ExecuteType2(table.ID, tid, data1, data2, data3);
                break;
            case 3:
                ExecuteType3(table.ID, tid, data1, data2, data3, table.Moral, totalDex, righthandDamage);
                break;
            case 4:
                ExecuteType4(table.ID, _srcUser.Uid, tid, data1, data2, data3, table.Moral);
                break;
            case 5:
                ExecuteType5(table.ID);
                break;
            case 6:
                ExecuteType6(table.ID);
                break;
            case 7:
                ExecuteType7(table.ID, tid, data1, data2, data3, table.Moral);
                break;
            case 8:
                ExecuteType8(table.ID);
                break;
            case 9:
                ExecuteType9(table.ID);
                break;
            case 10:
                ExecuteType10(table.ID);
                break;
        }
    }

    /// <summary>
    /// CMagicProcess::IsAvailable — only the table lookup survives in the C++;
    /// the fail packet is commented out and no moral checks happen for users.
    /// </summary>
    private Magic? IsAvailable(int magicId, int tid, byte type)
    {
        Magic? table = World?.MagicTable.GetValueOrDefault(magicId);
        if (table is not null)
            return table;

        MagicState = StateNone;
        return null;
    }

    /// <summary>CMagicProcess::ExecuteType1 — weapon-based attack skill.</summary>
    private byte ExecuteType1(int magicId, int tid, int data1, int data2, int data3, byte sequence)
    {
        byte result = 1;

        Magic? magic = World?.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return 0;

        short damage = _srcUser.GetDamage(tid, magicId);

        Npc? npc = World?.Npcs.GetValueOrDefault(tid - NpcBand);
        if (npc is null || npc.State == NpcState.Dead || npc.HP == 0)
        {
            result = 0;
        }
        else if (!npc.SetDamage(magicId, damage, _srcUser.UserId, _srcUser.Uid + UserBand))
        {
            npc.SendExpToUserList();
            npc.SendDead();
            _srcUser.SendAttackSuccess(tid, AttackTargetDead, damage, npc.HP);
        }
        else
        {
            _srcUser.SendAttackSuccess(tid, AttackSuccess, damage, npc.HP);
        }

        // packet_send:
        if (magic.Type2 is 0 or 1)
        {
            var buffer = new byte[32];
            var writer = new PacketWriter(buffer);
            writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
            writer.SetByte(MagicEffecting);
            writer.SetDWord((uint)magicId);
            writer.SetShort(_srcUser.Uid);
            writer.SetShort(tid);
            writer.SetShort(data1);
            writer.SetShort(result);
            writer.SetShort(data3);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetShort(damage == 0 ? -104 : 0);
            _srcUser.SendAll(writer.Written);
        }

        return result;
    }

    /// <summary>CMagicProcess::ExecuteType2 — arrow-based attack skill.</summary>
    private byte ExecuteType2(int magicId, int tid, int data1, int data2, int data3)
    {
        byte result = 1;
        short damage = _srcUser.GetDamage(tid, magicId);

        if (damage > 0)
        {
            Npc? npc = World?.Npcs.GetValueOrDefault(tid - NpcBand);
            if (npc is null || npc.State == NpcState.Dead || npc.HP == 0)
            {
                result = 0;
            }
            else if (!npc.SetDamage(magicId, damage, _srcUser.UserId, _srcUser.Uid + UserBand))
            {
                // Target died: the C++ sends the result packet first, then the kill flow.
                SendType2Result(magicId, tid, data1, data3, result, damage);
                npc.SendExpToUserList();
                npc.SendDead();
                _srcUser.SendAttackSuccess(tid, MagicAttackTargetDead, damage, npc.HP);
                return result;
            }
            else
            {
                _srcUser.SendAttackSuccess(tid, AttackSuccess, damage, npc.HP);
            }
        }

        // packet_send:
        SendType2Result(magicId, tid, data1, data3, result, damage);
        return result;
    }

    private void SendType2Result(int magicId, int tid, int data1, int data3, byte result, short damage)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(_srcUser.Uid);
        writer.SetShort(tid);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(damage == 0 ? -104 : 0);
        _srcUser.SendAll(writer.Written);
    }

    /// <summary>CMagicProcess::ExecuteType3 — magical attack, healing, mana restore.</summary>
    private void ExecuteType3(int magicId, int tid, int data1, int data2, int data3,
        int moral, int dexpoint, int righthandDamage)
    {
        int result = 1;

        Magic? magic = World?.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        // Area attack.
        if (tid == -1)
        {
            AreaAttack(3, magicId, moral, data1, data2, data3, dexpoint, righthandDamage);
            return;
        }

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

            int damage;
            if (type.FirstDamage < 0 && type.DirectType == 1 && magicId < 400000)
                damage = GetMagicDamage(tid, type.FirstDamage, type.Attribute, dexpoint, righthandDamage);
            else
                damage = type.FirstDamage;

            if (type.Duration == 0)
            {
                // Non-durational spells.
                if (type.DirectType == 1)
                {
                    if (damage > 0)
                    {
                        result = npc.SetHMagicDamage(damage) ? 1 : 0;
                    }
                    else
                    {
                        damage = Math.Abs(damage);
                        int attackType = type.Attribute == 3 ? 3 : magicId; // stun magic

                        if (!npc.SetDamage(attackType, damage, _srcUser.UserId, _srcUser.Uid + UserBand))
                        {
                            npc.SendExpToUserList();
                            npc.SendDead();
                            _srcUser.SendAttackSuccess(tid, MagicAttackTargetDead, (short)damage, npc.HP, MagicAttack);
                        }
                        else
                        {
                            _srcUser.SendAttackSuccess(tid, AttackSuccess, (short)damage, npc.HP, MagicAttack);
                        }
                    }
                }
                else if (type.DirectType is 2 or 3)
                {
                    npc.MSpChange(type.DirectType, type.FirstDamage);
                }
                else if (type.DirectType == 4)
                {
                    npc.ItemWoreOut(DefenceSlot, type.FirstDamage);
                }
            }
            else
            {
                // Durational spells (HP only).
                if (damage < 0)
                {
                    damage = Math.Abs(damage);
                    int attackType = type.Attribute == 3 ? 3 : magicId;

                    if (!npc.SetDamage(attackType, damage, _srcUser.UserId, _srcUser.Uid + UserBand))
                    {
                        npc.SendExpToUserList();
                        npc.SendDead();
                        _srcUser.SendAttackSuccess(tid, MagicAttackTargetDead, (short)damage, npc.HP);
                    }
                    else
                    {
                        _srcUser.SendAttackSuccess(tid, AttackSuccess, (short)damage, npc.HP);
                    }
                }

                damage = GetMagicDamage(tid, type.TimeDamage, type.Attribute, dexpoint, righthandDamage);

                for (int i = 0; i < AiConstants.MaxMagicType3; i++)
                {
                    if (npc.MagicType3[i].AttackUserId == -1 && npc.MagicType3[i].Duration == 0)
                    {
                        npc.MagicType3[i].AttackUserId = (short)_srcUser.Uid;
                        npc.MagicType3[i].StartTime = TimeGet();
                        npc.MagicType3[i].Duration = type.Duration;
                        npc.MagicType3[i].Interval = 2;
                        npc.MagicType3[i].HpAmount = (short)(damage / (type.Duration / 2));
                        break;
                    }
                }
            }
        }

        // packet_send:
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(_srcUser.Uid);
        writer.SetShort(tid);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);
        writer.SetShort(moral);
        writer.SetShort(0);
        writer.SetShort(0);
        _srcUser.SendAll(writer.Written);
    }

    /// <summary>CMagicProcess::ExecuteType4 — buffs (only the speed buff is live).</summary>
    private void ExecuteType4(int magicId, int sid, int tid, int data1, int data2, int data3, int moral)
    {
        byte result = 1;

        // Area buff.
        if (tid == -1)
        {
            if (AreaAttack(4, magicId, moral, data1, data2, data3, 0, 0) == 0)
                SendType4Fail(magicId, sid, tid);

            return;
        }

        Npc? npc = World?.Npcs.GetValueOrDefault(tid - NpcBand);
        if (npc is null || npc.State == NpcState.Dead || npc.HP == 0)
        {
            SendType4Fail(magicId, sid, tid);
            return;
        }

        MagicType4? type = World?.MagicType4Table.GetValueOrDefault(magicId);
        if (type is null)
            return;

        switch (type.BuffType)
        {
            case 1: // max HP
            case 2: // armor
            case 4: // attack power
            case 5: // attack speed
                break;

            case 6: // move speed
                npc.MagicType4[type.BuffType - 1].Amount = type.Speed;
                npc.MagicType4[type.BuffType - 1].DurationTime = type.Duration;
                npc.MagicType4[type.BuffType - 1].StartTime = TimeGet();
                npc.Speed1 = npc.OldSpeed1 * (type.Speed / 100.0f);
                npc.Speed2 = npc.OldSpeed2 * (type.Speed / 100.0f);
                break;

            case 7: // stats
            case 8: // resistances
            case 9: // hit/avoid rates
                break;

            default:
                SendType4Fail(magicId, sid, tid);
                return;
        }

        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        _srcUser.SendAll(writer.Written);
    }

    private void SendType4Fail(int magicId, int sid, int tid)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicFail);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        _srcUser.SendAll(writer.Written);
    }

    private void ExecuteType5(int magicId)
    {
    }

    private void ExecuteType6(int magicId)
    {
    }

    /// <summary>CMagicProcess::ExecuteType7 — binding/sleep skills.</summary>
    private void ExecuteType7(int magicId, int tid, int data1, int data2, int data3, int moral)
    {
        int result = 1;

        Magic? magic = World?.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        // AoE skills (AoE sleep unimplemented upstream).
        if (tid == -1)
        {
            AreaAttack(7, magicId, moral, data1, data2, data3, 0, 0);
            return;
        }

        Npc? npc = World?.Npcs.GetValueOrDefault(tid - NpcBand);
        if (npc is null || npc.State == NpcState.Dead || npc.HP == 0)
        {
            result = 0;
        }
        else
        {
            MagicType7? type = World?.MagicType7Table.GetValueOrDefault(magicId);
            if (type is null)
                return;

            short damage = type.Damage;
            if (damage > 0)
            {
                // Attacking (e.g. binding).
                if (!npc.SetDamage(magicId, damage, _srcUser.UserId, _srcUser.Uid + UserBand))
                {
                    npc.SendExpToUserList();
                    npc.SendDead();
                    _srcUser.SendAttackSuccess(tid, MagicAttackTargetDead, damage, npc.HP);
                }
                else
                {
                    _srcUser.SendAttackSuccess(tid, AttackSuccess, damage, npc.HP);
                }
            }
            else
            {
                // Sleeping (upstream note: works, but the duration is infinite).
                npc.State = NpcState.Sleeping;
                npc.Delay = type.Duration;
            }
        }

        // packet_send:
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(_srcUser.Uid);
        writer.SetShort(tid);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);
        writer.SetShort(moral);
        writer.SetShort(0);
        writer.SetShort(0);
        _srcUser.SendAll(writer.Written);
    }

    private void ExecuteType8(int magicId)
    {
    }

    private void ExecuteType9(int magicId)
    {
    }

    private void ExecuteType10(int magicId)
    {
    }

    /// <summary>CMagicProcess::GetMagicDamage — like the NPC version plus the right-hand bonus.</summary>
    public short GetMagicDamage(int tid, int totalHit, int attribute, int dexpoint, int righthandDamage)
    {
        short damage = 0;
        int totalR = 0;
        bool sign = true;

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
            damage = (short)(damage + righthandDamage);
        }
        else
        {
            damage = 0;
        }

        if (!sign && damage != 0)
            damage = (short)-damage;

        return damage;
    }

    /// <summary>CMagicProcess::AreaAttack — fans the effect out over the 3×3 region block.</summary>
    public short AreaAttack(int magicType, int magicId, int moral, int data1, int data2, int data3,
        int dexpoint, int righthandDamage)
    {
        int radius = 0;

        if (magicType == 3)
        {
            MagicType3? type3 = World?.MagicType3Table.GetValueOrDefault(magicId);
            if (type3 is null)
                return 0;

            radius = type3.Radius;
        }
        else if (magicType == 4)
        {
            MagicType4? type4 = World?.MagicType4Table.GetValueOrDefault(magicId);
            if (type4 is null)
                return 0;

            radius = type4.Radius;
        }
        else if (magicType == 7)
        {
            MagicType7? type7 = World?.MagicType7Table.GetValueOrDefault(magicId);
            if (type7 is null)
                return 0;

            radius = type7.Radius;
        }

        if (radius <= 0)
            return 0;

        int regionX = data1 / AiConstants.ViewDistance;
        int regionZ = data3 / AiConstants.ViewDistance;

        AiZone? zone = Zone;
        if (zone is null)
            return 0;

        int maxXx = zone.RegionsX;
        int maxZz = zone.RegionsZ;

        int minX = regionX - 1;
        if (minX < 0)
            minX = 0;

        int minZ = regionZ - 1;
        if (minZ < 0)
            minZ = 0;

        int maxX = regionX + 1;
        if (maxX >= maxXx)
            maxX = maxXx - 1;

        // Quirk kept: the C++ clamps min_z against max_zz here instead of max_z,
        // leaving max_z unclamped at the map's upper edge.
        int maxZ = regionZ + 1;
        if (minZ >= maxZz)
            minZ = maxZz - 1;

        int searchX = maxX - minX + 1;
        int searchZ = maxZ - minZ + 1;

        for (int i = 0; i < searchX; i++)
        {
            for (int j = 0; j < searchZ; j++)
                AreaAttackDamage(magicType, minX + i, minZ + j, magicId, moral, data1, data2, data3, dexpoint, righthandDamage);
        }

        return 1;
    }

    /// <summary>CMagicProcess::AreaAttackDamage — applies the area effect within one region.</summary>
    public void AreaAttackDamage(int magicType, int rx, int rz, int magicId, int moral,
        int data1, int data2, int data3, int dexpoint, int righthandDamage)
    {
        AiZone? zone = Zone;
        if (zone is null)
            return;

        if (rx < 0 || rz < 0 || rx > zone.RegionsX - 1 || rz > zone.RegionsZ - 1)
            return;

        Magic? magic = World?.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        MagicType3? type3 = null;
        MagicType4? type4 = null;
        MagicType7? type7 = null;
        int targetDamage = 0, attribute = 0;
        float radius = 0;

        if (magicType == 3)
        {
            type3 = World?.MagicType3Table.GetValueOrDefault(magicId);
            if (type3 is null)
                return;

            targetDamage = type3.FirstDamage;
            attribute = type3.Attribute;
            radius = type3.Radius;
        }
        else if (magicType == 4)
        {
            type4 = World?.MagicType4Table.GetValueOrDefault(magicId);
            if (type4 is null)
                return;

            radius = type4.Radius;
        }
        else if (magicType == 7)
        {
            type7 = World?.MagicType7Table.GetValueOrDefault(magicId);
            if (type7 is null)
                return;

            targetDamage = type7.Damage;
            radius = type7.Radius;
        }

        if (radius <= 0)
            return;

        var start = new Vector3(data1, 0, data3);
        int result = 1;

        // Snapshot like the C++ (ordered for deterministic packet order).
        int[] npcIds = [.. zone.Regions[rx, rz].Npcs.Order()];

        foreach (int bandId in npcIds)
        {
            if (bandId < NpcBand)
                continue;

            Npc? npc = World?.Npcs.GetValueOrDefault(bandId - NpcBand);
            if (npc is null || npc.State == NpcState.Dead)
                continue;

            if (_srcUser.Nation == npc.Group)
                continue;

            var end = new Vector3(npc.CurX, npc.CurY, npc.CurZ);
            float distance = Npc.GetDistance(start, end);
            if (distance > radius)
                continue;

            if (magicType == 3)
            {
                int damage = GetMagicDamage(npc.Nid + NpcBand, targetDamage, attribute, dexpoint, righthandDamage);

                // Note: '>= 0' here, unlike the '>' in ExecuteType3 — kept from the C++.
                if (damage >= 0)
                {
                    result = npc.SetHMagicDamage(damage) ? 1 : 0;
                }
                else
                {
                    damage = Math.Abs(damage);
                    int attackType = type3!.Attribute == 3 ? 3 : magicId; // stun magic

                    if (!npc.SetDamage(attackType, damage, _srcUser.UserId, _srcUser.Uid + UserBand))
                    {
                        npc.SendExpToUserList();
                        npc.SendDead();
                        _srcUser.SendAttackSuccess(npc.Nid + NpcBand, MagicAttackTargetDead, (short)damage, npc.HP);
                    }
                    else
                    {
                        _srcUser.SendAttackSuccess(npc.Nid + NpcBand, AttackSuccess, (short)damage, npc.HP);
                    }
                }

                SendAreaResult(magicId, npc.Nid + NpcBand, data1, data3, result, moral);
            }
            else if (magicType == 4)
            {
                result = 1;

                switch (type4!.BuffType)
                {
                    case 1:
                    case 2:
                    case 4:
                    case 5:
                        break;

                    case 6: // move speed
                        npc.MagicType4[type4.BuffType - 1].Amount = type4.Speed;
                        npc.MagicType4[type4.BuffType - 1].DurationTime = type4.Duration;
                        npc.MagicType4[type4.BuffType - 1].StartTime = TimeGet();
                        npc.Speed1 = npc.OldSpeed1 * (type4.Speed / 100.0f);
                        npc.Speed2 = npc.OldSpeed2 * (type4.Speed / 100.0f);
                        break;

                    case 7:
                    case 8:
                    case 9:
                        break;

                    default:
                        result = 0;
                        break;
                }

                SendAreaResult(magicId, npc.Nid + NpcBand, data1, data3, result, 0);
            }
            else if (magicType == 7)
            {
                int damage = targetDamage;
                if (!npc.SetDamage(magicId, damage, _srcUser.UserId, _srcUser.Uid + UserBand))
                {
                    npc.SendExpToUserList();
                    npc.SendDead();
                    _srcUser.SendAttackSuccess(npc.Nid + NpcBand, MagicAttackTargetDead, (short)damage, npc.HP);
                }
                else
                {
                    _srcUser.SendAttackSuccess(npc.Nid + NpcBand, AttackSuccess, (short)damage, npc.HP);
                }

                SendAreaResult(magicId, npc.Nid + NpcBand, data1, data3, result, moral);
            }
        }

        _ = type7;
    }

    private void SendAreaResult(int magicId, int tid, int data1, int data3, int result, int moral)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(_srcUser.Uid);
        writer.SetShort(tid);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);
        writer.SetShort(moral);
        writer.SetShort(0);
        writer.SetShort(0);
        _srcUser.SendAll(writer.Written);
    }

    /// <summary>CMagicProcess::GetWeatherDamage — +10% when the weather matches the attribute.</summary>
    public short GetWeatherDamage(short damage, short attribute)
    {
        bool weatherBuff = (GetWeatherType?.Invoke() ?? 0) switch
        {
            WeatherFine => attribute == AttributeFire,
            WeatherRain => attribute == AttributeLightning,
            WeatherSnow => attribute == AttributeIce,
            _ => false,
        };

        if (weatherBuff)
            damage = (short)((damage * 110) / 100);

        return damage;
    }
}
