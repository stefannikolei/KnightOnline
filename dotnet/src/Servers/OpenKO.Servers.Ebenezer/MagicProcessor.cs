using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Port of the Ebenezer-side <c>CMagicProcess</c> (Server/Ebenezer/MagicProcess.cpp):
/// the WIZ_MAGIC_PROCESS packet flow, the moral/skill/cost gate (IsAvailable) and
/// the type 1-5 executors. Each <see cref="GameUser"/> owns one instance
/// (<c>m_pSrcUser</c> = the user); the AI link owns one with a null source for
/// NPC-cast magic. Type 8 (warp/summon) and resurrection attach with the
/// respawn slice.
/// </summary>
public sealed class MagicProcessor(EbenezerWorld world, GameUser? srcUser, ILogger logger)
{
    // e_MagicState (GameDefine.h).
    public const byte StateNone = 0;    // MAGIC_STATE_NONE
    public const byte StateCasting = 2; // MAGIC_STATE_CASTING

    // WIZ_MAGIC_PROCESS subcommands (shared/packets.h).
    public const byte MagicCasting = 1;   // MAGIC_CASTING
    public const byte MagicFlying = 2;    // MAGIC_FLYING
    public const byte MagicEffecting = 3; // MAGIC_EFFECTING
    public const byte MagicFail = 4;      // MAGIC_FAIL
    public const byte MagicType3End = 5;  // MAGIC_TYPE3_END / MAGIC_DURATION_EXPIRED
    public const byte MagicType4End = 5;  // MAGIC_TYPE4_END
    public const byte MagicCancel = 6;    // MAGIC_CANCEL

    // Moral codes (MagicProcess.cpp).
    private const byte MoralSelf = 1;
    private const byte MoralFriendWithMe = 2;
    private const byte MoralFriendExceptMe = 3;
    private const byte MoralParty = 4;
    private const byte MoralNpc = 5;
    private const byte MoralPartyAll = 6;
    private const byte MoralEnemy = 7;
    private const byte MoralAreaEnemy = 10;
    private const byte MoralAreaFriend = 11;
    private const byte MoralSelfArea = 13;
    private const byte MoralClan = 14;
    private const byte MoralClanAll = 15;
    private const byte MoralCorpseFriend = 25;

    // MAGIC_TYPE5 types.
    private const byte RemoveType3 = 1;
    private const byte RemoveType4 = 2;
    private const byte Resurrection = 3;
    private const byte RemoveBless = 5;

    // Attribute resistances (AIServer Define.h NONE_R..).
    private const byte AttributeFire = 1;      // FIRE_R / ATTRIBUTE_FIRE
    private const byte AttributeCold = 2;      // COLD_R / ATTRIBUTE_ICE
    private const byte AttributeLightning = 3; // LIGHTNING_R / ATTRIBUTE_LIGHTNING
    private const byte AttributeMagic = 4;     // MAGIC_R
    private const byte AttributeDisease = 5;   // DISEASE_R
    private const byte AttributePoison = 6;    // POISON_R

    private const int SnowEventSkill = 490043; // SNOW_EVENT_SKILL
    private const int SnowEventMoney = 2000;   // SNOW_EVENT_MONEY
    private const int ClanSummonTime = 180;    // CLAN_SUMMON_TIME

    private const byte UserDead = 3;          // USER_DEAD
    private const byte AbnormalBlinking = 3;  // ABNORMAL_BLINKING
    private const byte AbnormalNormal = 1;    // ABNORMAL_NORMAL
    private const byte AbnormalGiant = 2;     // ABNORMAL_GIANT
    private const byte AbnormalDwarf = 3;     // ABNORMAL_DWARF

    private const int ZoneBattle = 101;      // ZONE_BATTLE
    private const int ZoneSnowBattle = 111;  // ZONE_SNOW_BATTLE
    private const byte SnowBattle = 2;       // SNOW_BATTLE
    private const int WeaponStaff = 11;      // WEAPON_STAFF (Kind / 10)

    /// <summary>m_bMagicState.</summary>
    public byte MagicState = StateNone;

    private GameUser? GetUser(int id)
        => id >= 0 && id < world.Users.Length ? world.Users[id] : null;

    private static bool IsValidUserId(int id) => id is >= 0 and < EbenezerWorld.MaxUser;

    /// <summary>CMagicProcess::MagicPacket — the central WIZ_MAGIC_PROCESS flow.</summary>
    public void MagicPacket(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte command = reader.GetByte();
        var magicId = (int)reader.GetDWord();
        short sid = reader.GetShort();
        short tid = reader.GetShort();
        short data1 = reader.GetShort();
        short data2 = reader.GetShort();
        short data3 = reader.GetShort();
        short data4 = reader.GetShort();
        short data5 = reader.GetShort();
        short data6 = reader.GetShort();

        // Snowball fights: only the snowball skill goes through.
        if (srcUser?.UserData is { } snowCheck
            && snowCheck.Zone == ZoneSnowBattle
            && world.BattleOpen == SnowBattle
            && magicId != SnowEventSkill)
            return;

        if (command == MagicCancel)
        {
            Type3Cancel(magicId, sid);
            Type4Cancel(magicId, sid);
            return;
        }

        Magic? magic = world.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        GameNpc? sourceNpc = null;
        GameUser? sourceUser = null;

        if (sid >= EbenezerWorld.NpcBand)
        {
            sourceNpc = world.Npcs.GetValueOrDefault(sid);
            if (sourceNpc is null || sourceNpc.NpcState == GameNpc.StateDead)
                return;
        }
        else if (IsValidUserId(sid))
        {
            sourceUser = GetUser(sid);
            if (sourceUser is null)
                return;

            if (sourceUser.ResHpType == UserDead || sourceUser.UserData?.Hp == 0)
            {
                logger.LogError("MagicPacket: user is dead [userId={UserId}]", sid);
                return;
            }
        }

        GameUser? targetUser = GetUser(tid);
        if (targetUser is not null)
        {
            // Type 4 repeat check.
            if (magic.Type1 == 4 && magic.Moral < 5)
            {
                MagicType4? type4 = world.MagicType4Table.GetValueOrDefault(magicId);
                if (type4 is null)
                    return;

                if (targetUser.Type4Buff[type4.BuffType - 1] > 0)
                {
                    SendNoEffectFail(magicId, sid, tid);
                    return;
                }
            }
            // Type 3 repeat check.
            else if (magic.Type1 == 3 && magic.Moral < 5)
            {
                MagicType3? type3 = world.MagicType3Table.GetValueOrDefault(magicId);
                if (type3 is null)
                    return;

                if (type3.TimeDamage > 0)
                {
                    for (int i = 0; i < targetUser.HpAmount.Length; i++)
                    {
                        if (targetUser.HpAmount[i] > 0)
                        {
                            SendNoEffectFail(magicId, sid, tid);
                            return;
                        }
                    }
                }
            }
        }

        // Battle-zone clan/party summon throttle.
        if (sourceUser is not null
            && srcUser?.UserData is { Zone: ZoneBattle }
            && targetUser is not null
            && magic.Type1 == 8
            && (magic.Moral < 5 || magic.Moral == MoralClan)
            && world.Clock() - targetUser.LastRegeneTime < ClanSummonTime)
        {
            SendNoEffectFail(magicId, sid, tid);
            return;
        }

        // Client indicates that magic failed. Just send back the packet.
        if (command == MagicFail)
        {
            SendEcho(command, magicId, sid, tid, data1, data2, data3, data4, data5, data6, sourceNpc);
            return;
        }

        // When the arrow starts flying...
        if (command == MagicFlying)
        {
            if (magic.Type1 == 2)
            {
                MagicType2? type2 = world.MagicType2Table.GetValueOrDefault(magicId);
                if (type2 is null)
                    return;

                if (IsValidUserId(sid) && srcUser?.UserData is { } srcData)
                {
                    if (magic.FlyingEffect > 0)
                    {
                        if (magic.ManaCost > srcData.Mp)
                        {
                            command = MagicFail;
                            SendEcho(command, magicId, sid, tid, data1, data2, data3, data4, data5, data6, sourceNpc);
                            return;
                        }

                        srcUser.MSpChange(-magic.ManaCost);
                    }

                    if (srcUser.ItemCountChange(magic.UseItem, 1, type2.NeedArrow) < 2)
                    {
                        command = MagicFail;
                        SendEcho(command, magicId, sid, tid, data1, data2, data3, data4, data5, data6, sourceNpc);
                        return;
                    }
                }
            }

            SendEcho(command, magicId, sid, tid, data1, data2, data3, data4, data5, data6, sourceNpc);
            return;
        }

        Magic? table = IsAvailable(magicId, tid, sid, command, data1, data2, data3);
        if (table is null)
            return;

        if (command == MagicEffecting)
        {
            int initialResult = 1;

            // Users attacking NPCs (or area spells): forward to the AI server.
            if (IsValidUserId(sid) && srcUser?.UserData is { } srcData
                && (tid >= EbenezerWorld.NpcBand
                    || (tid == -1 && magic.Moral is MoralAreaEnemy or MoralSelfArea)))
            {
                int totalMagicDamage = 0;

                var aiBuffer = new byte[64];
                var aiWriter = new PacketWriter(aiBuffer);
                aiWriter.SetByte(AiOpcode.AG_MAGIC_ATTACK_REQ);
                aiWriter.SetShort(sid);
                aiWriter.SetByte(command);
                aiWriter.SetShort(tid);
                aiWriter.SetDWord((uint)magicId);
                aiWriter.SetShort(data1);
                aiWriter.SetShort(data2);
                aiWriter.SetShort(data3);
                aiWriter.SetShort(data4);
                aiWriter.SetShort(data5);
                aiWriter.SetShort(data6);
                aiWriter.SetShort(srcData.Cha + srcUser.ItemCham);

                Item? rightHand = world.ItemTable.GetValueOrDefault(srcData.Items[GameConstants.SlotRightHand].Num);
                if (srcData.Items[GameConstants.SlotRightHand].Num != 0
                    && rightHand is not null
                    && srcData.Items[GameConstants.SlotLeftHand].Num == 0
                    && rightHand.Kind / 10 == WeaponStaff)
                {
                    if (magic.Type1 == 3)
                    {
                        totalMagicDamage += (int)((rightHand.Damage * 0.8f) + (rightHand.Damage * srcData.Level) / 60);

                        MagicType3? type3 = world.MagicType3Table.GetValueOrDefault(magicId);
                        if (type3 is null)
                            return;

                        if (srcUser.MagicTypeRightHand == type3.Attribute)
                            totalMagicDamage += (int)((rightHand.Damage * 0.8f) + (rightHand.Damage * srcData.Level) / 30);

                        if (type3.Attribute == AttributeMagic)
                            totalMagicDamage = 0;
                    }

                    aiWriter.SetShort(totalMagicDamage);
                }
                else
                {
                    aiWriter.SetShort(0);
                }

                world.SendToAiServer?.Invoke(srcData.Zone, aiWriter.Written.ToArray());
            }

            // Make sure a single player target exists.
            if (tid != -1 && targetUser is null)
                return;

            initialResult = ExecuteType(table.Type1, table.ID, sid, tid, data1, data2, data3);

            if (initialResult != 0)
                ExecuteType(table.Type2, table.ID, sid, tid, data1, data2, data3);
        }
        else if (command == MagicCasting)
        {
            SendEcho(command, magicId, sid, tid, data1, data2, data3, data4, data5, data6, sourceNpc);
        }
    }

    private int ExecuteType(byte type, int magicId, int sid, int tid, int data1, int data2, int data3)
    {
        switch (type)
        {
            case 1:
                return ExecuteType1(magicId, sid, tid, data1, data2, data3);
            case 2:
                return ExecuteType2(magicId, sid, tid, data1, data2, data3);
            case 3:
                ExecuteType3(magicId, sid, tid, data1, data2, data3);
                break;
            case 4:
                ExecuteType4(magicId, sid, tid, data1, data2, data3);
                break;
            case 5:
                ExecuteType5(magicId, sid, tid, data1, data2, data3);
                break;
            case 8:
                ExecuteType8(magicId, sid, tid, data1, data2, data3);
                break;
        }

        return 1;
    }

    /// <summary>The MAGIC_FAIL packet with the SKILLMAGIC_FAIL_NOEFFECT (-103) marker.</summary>
    private void SendNoEffectFail(int magicId, short sid, short tid)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(MagicFail);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(-103);
        writer.SetShort(0);
        writer.SetShort(0);

        if (srcUser?.UserData is not { } srcData)
        {
            MagicState = StateNone;
            return;
        }

        if (MagicState == StateCasting)
            world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
        else
            srcUser.Send(writer.Written);

        MagicState = StateNone;
    }

    /// <summary>The return_echo tail of MagicPacket.</summary>
    private void SendEcho(byte command, int magicId, short sid, short tid,
        short data1, short data2, short data3, short data4, short data5, short data6, GameNpc? sourceNpc)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(command);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        writer.SetShort(data1);
        writer.SetShort(data2);
        writer.SetShort(data3);
        writer.SetShort(data4);
        writer.SetShort(data5);
        writer.SetShort(data6);

        if (IsValidUserId(sid) && srcUser?.UserData is { } srcData)
        {
            world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
        }
        else if (sid >= EbenezerWorld.NpcBand && sourceNpc is not null)
        {
            world.SendRegion(writer.Written, sourceNpc.CurZone, sourceNpc.RegionX, sourceNpc.RegionZ, except: null, direct: false);
        }
    }

    /// <summary>CMagicProcess::IsAvailable — moral, skill and cost gate.</summary>
    public Magic? IsAvailable(int magicId, int tid, int sid, byte type, int data1, int data2, int data3)
    {
        _ = data2;
        _ = data3;

        GameUser? user = null;
        GameNpc? npc = null;
        GameNpc? sourceNpc = null;
        bool sourceIsNpc = false;
        int moral;

        Magic? table = world.MagicTable.GetValueOrDefault(magicId);
        if (table is null)
            return FailReturn(magicId, sid, tid, type, sourceIsNpc, null);

        // Source validity.
        if (IsValidUserId(sid))
        {
            if (srcUser is null)
                return FailReturn(magicId, sid, tid, type, sourceIsNpc, null);

            if (srcUser.AbnormalType == AbnormalBlinking)
                return FailReturn(magicId, sid, tid, type, sourceIsNpc, null);
        }
        else if (sid >= EbenezerWorld.NpcBand)
        {
            sourceIsNpc = true;
            sourceNpc = world.Npcs.GetValueOrDefault(sid);
            if (sourceNpc is null || sourceNpc.NpcState == GameNpc.StateDead)
                return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
        }
        else
        {
            return FailReturn(magicId, sid, tid, type, sourceIsNpc, null);
        }

        // Target existence.
        if (IsValidUserId(tid))
        {
            user = GetUser(tid);

            if (table.Type1 != 5)
            {
                if (user is null || user.ResHpType == UserDead || user.AbnormalType == AbnormalBlinking)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
            }
            else
            {
                MagicType5? type5 = world.MagicType5Table.GetValueOrDefault(magicId);
                if (type5 is null)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                if (user is null)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                if (user.AbnormalType == AbnormalBlinking)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                if (user.ResHpType == UserDead && type5.NeedStone == 0 && type5.ExpRecover == 0)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
            }

            moral = user!.UserData?.Nation ?? 0;
        }
        else if (tid >= EbenezerWorld.NpcBand)
        {
            if (!world.PointCheckFlag)
                return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

            npc = world.Npcs.GetValueOrDefault(tid);
            if (npc is null || npc.NpcState == GameNpc.StateDead)
                return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

            moral = npc.Group;
        }
        else if (tid == -1)
        {
            if (table.Moral == MoralAreaEnemy)
            {
                if (!sourceIsNpc)
                    moral = srcUser!.UserData?.Nation == 1 ? 2 : 1;
                else
                    moral = 1;
            }
            else
            {
                moral = !sourceIsNpc ? srcUser!.UserData?.Nation ?? 0 : 1;
            }
        }
        else
        {
            moral = srcUser?.UserData?.Nation ?? 0;
        }

        // Moral comparison.
        switch (table.Moral)
        {
            case MoralSelf:
                if (sourceIsNpc)
                {
                    if (tid != sourceNpc!.Nid)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }
                else if (tid != srcUser!.SocketId)
                {
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }

                break;

            case MoralFriendWithMe:
                if (sourceIsNpc)
                {
                    if (sourceNpc!.Group != moral)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }
                else if (srcUser!.UserData?.Nation != moral)
                {
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }

                break;

            case MoralFriendExceptMe:
            case MoralCorpseFriend:
                if (sourceIsNpc)
                {
                    if (sourceNpc!.Group != moral || tid == sourceNpc.Nid)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }
                else
                {
                    if (srcUser!.UserData?.Nation != moral || tid == srcUser.SocketId)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                    if (table.Moral == MoralCorpseFriend && user!.ResHpType != UserDead)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }

                break;

            case MoralParty:
                if ((srcUser!.PartyIndex == -1 && sid != tid)
                    || srcUser.UserData?.Nation != moral
                    || (user is not null && user.PartyIndex != srcUser.PartyIndex))
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                break;

            case MoralNpc:
                if (npc is null || npc.Group != moral)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                break;

            case MoralPartyAll:
                break;

            case MoralEnemy:
                if (sourceIsNpc)
                {
                    if (sourceNpc!.Group == moral)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }
                else if (srcUser!.UserData?.Nation == moral)
                {
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
                }

                break;

            case MoralAreaFriend:
                if (srcUser!.UserData?.Nation != moral)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                break;

            case MoralClan:
                if ((srcUser!.UserData?.Knights == -1 && sid != tid)
                    || srcUser.UserData?.Nation != moral
                    || (user is not null && user.UserData?.Knights != srcUser.UserData?.Knights))
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                break;
        }

        // If the user cast the spell (and not the NPC)...
        if (!sourceIsNpc && srcUser?.UserData is { } srcData)
        {
            int modulator = table.Skill % 10; // hacking prevention
            if (modulator != 0)
            {
                if (table.Skill / 10 != srcData.Class)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                if (table.SkillLevel > srcData.Skills[modulator])
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
            }
            else if (table.SkillLevel > srcData.Level)
            {
                return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);
            }

            // MP/SP/item/HP deduction.
            if (type == MagicEffecting)
            {
                // Do not reduce MP/SP for arrow skills with a flying stage or combos.
                if (table.Type1 == 2 && table.FlyingEffect != 0)
                {
                    MagicState = StateNone;
                    return table;
                }

                if (table.Type1 == 1 && data1 > 1)
                {
                    MagicState = StateNone;
                    return table;
                }

                if (table.ManaCost > srcData.Mp)
                    return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                // Spells cast off an item.
                if (table.Type1 is 3 or 4 && IsValidUserId(sid) && table.UseItem != 0)
                {
                    Item? item = world.ItemTable.GetValueOrDefault(table.UseItem);
                    if (item is null)
                        return null;

                    if ((item.Race != 0 && srcData.Race != item.Race)
                        || (item.ClassId != 0 && !srcUser.JobGroupCheck(item.ClassId))
                        || (item.MinLevel != 0 && srcData.Level < item.MinLevel)
                        || srcUser.ItemCountChange(table.UseItem, 1, 1) < 2)
                    {
                        return FailReturn(magicId, sid, tid, MagicCasting, sourceIsNpc, sourceNpc);
                    }
                }

                if (table.Type1 == 5 && IsValidUserId(tid) && table.UseItem != 0)
                {
                    MagicType5? type5 = world.MagicType5Table.GetValueOrDefault(magicId);
                    if (type5 is null)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                    GameUser? target = GetUser(tid);
                    if (target is null)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                    // No resurrections for low level users.
                    if (type5.Type == Resurrection && target.UserData?.Level <= 5)
                        return FailReturn(magicId, sid, tid, MagicCasting, sourceIsNpc, sourceNpc);

                    GameUser consumer = type5.Type == Resurrection ? target : srcUser;
                    if (consumer.ItemCountChange(table.UseItem, 1, type5.NeedStone) < 2)
                        return FailReturn(magicId, sid, tid, MagicCasting, sourceIsNpc, sourceNpc);
                }

                // Actual deduction of skill or magic points.
                if (table.Type1 != 4 || tid == -1)
                    srcUser.MSpChange(-table.ManaCost);

                if (table.HpCost > 0 && table.ManaCost == 0)
                {
                    if (table.HpCost > srcData.Hp)
                        return FailReturn(magicId, sid, tid, type, sourceIsNpc, sourceNpc);

                    srcUser.HpChange(-table.HpCost);
                }

                MagicState = StateNone;
            }
        }

        return table;
    }

    /// <summary>The fail_return tail of IsAvailable — the MAGIC_FAIL packet.</summary>
    private Magic? FailReturn(int magicId, int sid, int tid, byte type, bool sourceIsNpc, GameNpc? sourceNpc)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(MagicFail);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(0);
        writer.SetShort(type == MagicCasting ? (short)-100 : (short)0);
        writer.SetShort(0);
        writer.SetShort(0);

        if (MagicState == StateCasting)
        {
            if (!sourceIsNpc)
            {
                if (srcUser?.UserData is { } srcData)
                    world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
            }
            else if (sourceNpc is not null)
            {
                world.SendRegion(writer.Written, sourceNpc.CurZone, sourceNpc.RegionX, sourceNpc.RegionZ, except: null, direct: false);
            }
        }
        else if (!sourceIsNpc)
        {
            srcUser?.Send(writer.Written);
        }

        MagicState = StateNone;
        return null;
    }

    /// <summary>CMagicProcess::ExecuteType1 — weapon attack skills.</summary>
    public byte ExecuteType1(int magicId, int sid, int tid, int data1, int data2, int data3)
    {
        Magic? magic = world.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return 0;

        MagicType1? type1 = world.MagicType1Table.GetValueOrDefault(magicId);
        if (type1 is null)
            return 0;

        if (srcUser is null)
            return 0;

        byte result = 1;
        int damage = srcUser.GetDamage(tid, magicId);

        GameUser? target = GetUser(tid);
        if (target?.UserData is not { } targetData || target.ResHpType == UserDead)
        {
            result = 0;
        }
        else
        {
            target.HpChange(-damage);

            if (targetData.Hp == 0)
            {
                target.ResHpType = UserDead;

                if (srcUser.PartyIndex == -1)
                    srcUser.LoyaltyChange(tid);
                else
                    srcUser.LoyaltyDivide(tid);

                srcUser.GoldChange(tid, 0);

                target.InitType3();
                target.InitType4();

                if (targetData.Zone != targetData.Nation && targetData.Zone < 3)
                    target.ExpChange(-target.MaxExp / 100);

                target.WhoKilledMe = (short)sid;
            }

            srcUser.SendTargetHP(0, tid, -damage);
        }

        if (magic.Type2 is 0 or 1 && srcUser.UserData is { } srcData)
        {
            var buffer = new byte[32];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
            writer.SetByte(MagicEffecting);
            writer.SetDWord((uint)magicId);
            writer.SetShort(sid);
            writer.SetShort(tid);
            writer.SetShort(data1);
            writer.SetShort(data2);
            writer.SetShort(data3);
            writer.SetShort(damage == 0 ? (short)-104 : (short)0);
            world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
        }

        return result;
    }

    /// <summary>CMagicProcess::ExecuteType2 — arrow skills with range verification.</summary>
    public byte ExecuteType2(int magicId, int sid, int tid, int data1, int data2, int data3)
    {
        _ = data2;

        Magic? magic = world.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return 0;

        if (srcUser?.UserData is not { } srcData)
            return 0;

        Item? weapon = srcData.Items[GameConstants.SlotLeftHand].Num != 0
            ? world.ItemTable.GetValueOrDefault(srcData.Items[GameConstants.SlotLeftHand].Num)
            : world.ItemTable.GetValueOrDefault(srcData.Items[GameConstants.SlotRightHand].Num);

        if (weapon is null)
            return 0;

        MagicType2? type2 = world.MagicType2Table.GetValueOrDefault(magicId);
        if (type2 is null)
            return 0;

        byte result = 1;
        int damage = 0;

        GameUser? target = GetUser(tid);
        if (target?.UserData is not { } targetData || target.ResHpType == UserDead)
        {
            result = 0;
        }
        else
        {
            var totalRange = (int)Math.Pow(type2.RangeMod * weapon.Range / 100, 2);

            float dx = srcData.CurX - targetData.CurX;
            float dz = srcData.CurZ - targetData.CurZ;
            if (dx * dx + dz * dz > totalRange)
            {
                result = 0;
            }
            else
            {
                damage = srcUser.GetDamage(tid, magicId);
                target.HpChange(-damage);

                if (targetData.Hp == 0)
                {
                    target.ResHpType = UserDead;

                    if (srcUser.PartyIndex == -1)
                        srcUser.LoyaltyChange(tid);
                    else
                        srcUser.LoyaltyDivide(tid);

                    srcUser.GoldChange(tid, 0);

                    target.InitType3();
                    target.InitType4();

                    if (targetData.Zone != targetData.Nation && targetData.Zone < 3)
                        target.ExpChange(-target.MaxExp / 100);

                    target.WhoKilledMe = (short)sid;
                }

                srcUser.SendTargetHP(0, tid, -damage);
            }
        }

        if (magic.Type2 is 0 or 2)
        {
            var buffer = new byte[32];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
            writer.SetByte(MagicEffecting);
            writer.SetDWord((uint)magicId);
            writer.SetShort(sid);
            writer.SetShort(tid);
            writer.SetShort(data1);
            writer.SetShort(result);
            writer.SetShort(data3);
            writer.SetShort(damage == 0 ? (short)-104 : (short)0);
            world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
        }

        return result;
    }

    /// <summary>CMagicProcess::ExecuteType3 — magic damage/heals, DoTs and area spells.</summary>
    public void ExecuteType3(int magicId, int sid, int tid, int data1, int data2, int data3)
    {
        _ = data2;

        Magic? magic = world.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        MagicType3? type3 = world.MagicType3Table.GetValueOrDefault(magicId);
        if (type3 is null)
            return;

        bool sourceIsNpc = false;
        GameNpc? sourceNpc = null;

        if (sid >= EbenezerWorld.NpcBand)
        {
            sourceIsNpc = true;
            sourceNpc = world.Npcs.GetValueOrDefault(sid);
            if (sourceNpc is null || sourceNpc.NpcState == GameNpc.StateDead)
                return;
        }

        var castedMembers = new List<int>();

        if (tid == -1)
        {
            for (int i = 0; i < world.Users.Length; i++)
            {
                GameUser? candidate = world.Users[i];
                if (candidate is null
                    || candidate.ResHpType == UserDead
                    || candidate.AbnormalType == AbnormalBlinking)
                    continue;

                if (UserRegionCheck(sid, i, magicId, type3.Radius, (short)data1, (short)data3))
                    castedMembers.Add(i);
            }

            if (castedMembers.Count == 0)
            {
                SendAreaFail(magicId, sid, tid, sourceIsNpc, sourceNpc);
                return;
            }
        }
        else
        {
            if (GetUser(tid) is null)
                return;

            castedMembers.Add(tid);
        }

        foreach (int userId in castedMembers)
        {
            GameUser? target = GetUser(userId);
            if (target?.UserData is not { } targetData || target.ResHpType == UserDead)
                continue;

            int damage;
            if (type3.FirstDamage < 0 && type3.DirectType == 1 && magicId < 400000)
                damage = GetMagicDamage(sid, userId, type3.FirstDamage, type3.Attribute);
            else
                damage = type3.FirstDamage;

            // Snowball fights fix the damage at -10.
            if (srcUser?.UserData is { Zone: ZoneSnowBattle } && world.BattleOpen == SnowBattle)
                damage = -10;

            if (type3.Duration == 0)
            {
                if (type3.DirectType == 1)
                {
                    target.HpChange(damage);

                    if (targetData.Hp == 0)
                        HandleType3Death(target, targetData, sid, userId, sourceIsNpc, snowKill: true);

                    if (!sourceIsNpc)
                        srcUser?.SendTargetHP(0, userId, damage);
                }
                else if (type3.DirectType is 2 or 3)
                {
                    target.MSpChange(damage);
                }
                else if (type3.DirectType == 4)
                {
                    target.ItemWoreOut(GameUser.DurabilityTypeDefence, -damage);
                }
            }
            else
            {
                if (damage != 0)
                {
                    target.HpChange(damage);

                    if (targetData.Hp == 0)
                        HandleType3Death(target, targetData, sid, userId, sourceIsNpc, snowKill: false);

                    if (!sourceIsNpc)
                        srcUser?.SendTargetHP(0, userId, damage);
                }

                if (target.ResHpType != UserDead)
                {
                    int durationDamage = type3.TimeDamage < 0
                        ? GetMagicDamage(sid, userId, type3.TimeDamage, type3.Attribute)
                        : type3.TimeDamage;

                    for (int k = 0; k < target.HpInterval.Length; k++)
                    {
                        if (target.HpInterval[k] == 5)
                        {
                            target.HpStartTime[k] = target.HpLastTime[k] = world.Clock();
                            target.HpDuration[k] = type3.Duration;
                            target.HpInterval[k] = 2;
                            target.HpAmount[k] = (short)(durationDamage / (target.HpDuration[k] / target.HpInterval[k]));
                            target.SourceId[k] = (short)sid;
                            break;
                        }
                    }

                    target.Type3Flag = true;
                }

                if (target.PartyIndex != -1 && type3.TimeDamage < 0)
                    world.SendPartyStatusChange(target.PartyIndex, (short)userId, 1, 0x01);
            }

            if (magic.Type2 is 0 or 3)
            {
                var buffer = new byte[32];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
                writer.SetByte(MagicEffecting);
                writer.SetDWord((uint)magicId);
                writer.SetShort(sid);
                writer.SetShort(userId);
                writer.SetShort(data1);
                writer.SetShort(1);
                writer.SetShort(data3);

                if (!sourceIsNpc)
                {
                    if (srcUser?.UserData is { } srcData)
                        world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
                }
                else
                {
                    world.SendRegion(writer.Written, sourceNpc!.CurZone, sourceNpc.RegionX, sourceNpc.RegionZ, except: null, direct: false);
                }
            }

            // Heal magic notifies the AI server (aggro).
            if (type3.DirectType == 1 && damage > 0 && !sourceIsNpc && sid != tid
                && srcUser?.UserData is { } healSrc)
            {
                var buffer = new byte[8];
                var writer = new PacketWriter(buffer);
                writer.SetByte(AiOpcode.AG_HEAL_MAGIC);
                writer.SetShort(sid);
                world.SendToAiServer?.Invoke(healSrc.Zone, writer.Written.ToArray());
            }
        }
    }

    private void HandleType3Death(GameUser target, UserData targetData, int sid, int userId, bool sourceIsNpc, bool snowKill)
    {
        target.ResHpType = UserDead;

        // Killed by a monster/NPC.
        if (sourceIsNpc)
        {
            if (targetData.Zone != targetData.Nation && targetData.Zone < 3)
                target.ExpChange(-target.MaxExp / 100);
            else
                target.ExpChange(-target.MaxExp / 20);
        }
        else if (srcUser is not null)
        {
            // Snowball kills pay out instead of shifting loyalty.
            if (snowKill
                && srcUser.UserData is { Zone: ZoneSnowBattle }
                && world.BattleOpen == SnowBattle)
            {
                srcUser.GoldGain(SnowEventMoney);

                if (targetData.Nation == 1)
                    ++world.KarusDead;
                else if (targetData.Nation == 2)
                    ++world.ElmoradDead;
            }
            else
            {
                if (srcUser.PartyIndex == -1)
                    srcUser.LoyaltyChange(userId);
                else
                    srcUser.LoyaltyDivide(userId);

                srcUser.GoldChange(userId, 0);
            }
        }

        target.InitType3();
        target.InitType4();

        if (IsValidUserId(sid))
        {
            if (targetData.Zone != targetData.Nation && targetData.Zone < 3)
                target.ExpChange(-target.MaxExp / 100);

            target.WhoKilledMe = (short)sid;
        }
    }

    private void SendAreaFail(int magicId, int sid, int tid, bool sourceIsNpc, GameNpc? sourceNpc)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(MagicFail);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        for (int i = 0; i < 6; i++)
            writer.SetShort(0);

        if (!sourceIsNpc)
        {
            if (srcUser?.UserData is { } srcData)
                world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
        }
        else if (sourceNpc is not null)
        {
            world.SendRegion(writer.Written, sourceNpc.CurZone, sourceNpc.RegionX, sourceNpc.RegionZ, except: null, direct: false);
        }
    }

    /// <summary>CMagicProcess::ExecuteType4 — durational stat buffs.</summary>
    public void ExecuteType4(int magicId, int sid, int tid, int data1, int data2, int data3)
    {
        _ = data2;

        Magic? magic = world.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        MagicType4? type4 = world.MagicType4Table.GetValueOrDefault(magicId);
        if (type4 is null)
            return;

        var castedMembers = new List<int>();

        if (tid == -1)
        {
            for (int i = 0; i < world.Users.Length; i++)
            {
                GameUser? candidate = world.Users[i];
                if (candidate is null
                    || candidate.ResHpType == UserDead
                    || candidate.AbnormalType == AbnormalBlinking)
                    continue;

                if (UserRegionCheck(sid, i, magicId, type4.Radius, (short)data1, (short)data3))
                    castedMembers.Add(i);
            }

            if (castedMembers.Count == 0)
            {
                if (IsValidUserId(sid))
                    SendAreaFail(magicId, sid, tid, sourceIsNpc: false, sourceNpc: null);

                return;
            }
        }
        else
        {
            if (GetUser(tid) is null)
                return;

            castedMembers.Add(tid);
        }

        foreach (int userId in castedMembers)
        {
            GameUser? target = GetUser(userId);
            if (target?.UserData is not { } targetData || target.ResHpType == UserDead)
                continue;

            bool failed = false;

            // Friendly buff already active?
            if (target.Type4Buff[type4.BuffType - 1] == 2 && tid == -1)
            {
                failed = true;
            }
            else
            {
                switch (type4.BuffType)
                {
                    case 1:
                        target.MaxHpAmount = type4.MaxHp;
                        break;
                    case 2:
                        target.AcAmount = type4.Armor;
                        break;
                    case 3:
                        // Bezoar / rice cake transformations.
                        if (magicId == 490034)
                            target.StateChange([3, AbnormalGiant]);
                        else if (magicId == 490035)
                            target.StateChange([3, AbnormalDwarf]);

                        break;
                    case 4:
                        target.AttackAmount = type4.AttackPower;
                        break;
                    case 5:
                        target.AttackSpeedAmount = type4.AttackSpeed;
                        break;
                    case 6:
                        target.SpeedAmount = type4.Speed;
                        break;
                    case 7:
                        target.StrAmount = type4.Strength;
                        target.StaAmount = type4.Stamina;
                        target.DexAmount = type4.Dexterity;
                        target.IntelAmount = type4.Intelligence;
                        target.ChaAmount = type4.Charisma;
                        break;
                    case 8:
                        target.FireRAmount = type4.FireResist;
                        target.ColdRAmount = type4.ColdResist;
                        target.LightningRAmount = type4.LightningResist;
                        target.MagicRAmount = type4.MagicResist;
                        target.DiseaseRAmount = type4.DiseaseResist;
                        target.PoisonRAmount = type4.PoisonResist;
                        break;
                    case 9:
                        target.HitRateAmount = type4.HitRate;
                        target.AvoidRateAmount = type4.AvoidRate;
                        break;
                    default:
                        failed = true;
                        break;
                }
            }

            if (!failed)
            {
                target.DurationType4[type4.BuffType] = type4.Duration;
                target.StartTimeType4[type4.BuffType] = world.Clock();

                // Single-target harpy drain: pay the mana per target.
                if (tid != -1 && magic.Type1 == 4 && IsValidUserId(sid))
                    srcUser?.MSpChange(-magic.ManaCost);

                if (IsValidUserId(sid) && srcUser?.UserData is { } srcData2)
                {
                    target.Type4Buff[type4.BuffType - 1] =
                        srcData2.Nation == targetData.Nation ? (byte)2 : (byte)1;
                }
                else
                {
                    target.Type4Buff[type4.BuffType - 1] = 1;
                }

                target.Type4Flag = true;

                target.SetSlotItemValue();
                target.SetUserAbility();

                // C++ quirk kept as-is: the packet carries tid, which is -1 for
                // area buffs.
                if (target.PartyIndex != -1 && target.Type4Buff[type4.BuffType - 1] == 1)
                    world.SendPartyStatusChange(target.PartyIndex, (short)tid, 2, 0x01);

                target.SendAiUserUpdate();

                if (magic.Type2 is 0 or 4)
                    SendType4Effect(magicId, sid, userId, data1, 1, data3, target);
            }
            else
            {
                if (magic.Type2 == 4)
                    SendType4Effect(magicId, sid, userId, data1, 0, data3, target);

                if (IsValidUserId(sid) && srcUser is not null)
                {
                    var buffer = new byte[32];
                    var writer = new PacketWriter(buffer);
                    writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
                    writer.SetByte(MagicFail);
                    writer.SetDWord((uint)magicId);
                    writer.SetShort(sid);
                    writer.SetShort(userId);
                    for (int i = 0; i < 6; i++)
                        writer.SetShort(0);

                    srcUser.Send(writer.Written);
                }
            }
        }
    }

    private void SendType4Effect(int magicId, int sid, int userId, int data1, int result, int data3, GameUser target)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(userId);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);

        if (IsValidUserId(sid) && srcUser?.UserData is { } srcData)
        {
            world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
        }
        else if (target.UserData is { } targetData)
        {
            world.SendRegion(writer.Written, targetData.Zone, target.RegionX, target.RegionZ, except: null, direct: false);
        }
    }

    /// <summary>CMagicProcess::ExecuteType5 — cures/dispels (resurrection defers to the respawn slice).</summary>
    public void ExecuteType5(int magicId, int sid, int tid, int data1, int data2, int data3)
    {
        _ = data2;

        Magic? magic = world.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return;

        MagicType5? type5 = world.MagicType5Table.GetValueOrDefault(magicId);
        if (type5 is null)
            return;

        GameUser? target = GetUser(tid);
        if (target?.UserData is not { } targetData)
            return;

        if (target.ResHpType == UserDead)
        {
            if (type5.Type != Resurrection)
                return;
        }
        else if (type5.Type == Resurrection)
        {
            return;
        }

        switch (type5.Type)
        {
            case RemoveType3:
                for (int i = 0; i < target.HpAmount.Length; i++)
                {
                    if (target.HpAmount[i] < 0)
                    {
                        target.HpStartTime[i] = 0.0;
                        target.HpLastTime[i] = 0.0;
                        target.HpAmount[i] = 0;
                        target.HpDuration[i] = 0;
                        target.HpInterval[i] = 5;
                        target.SourceId[i] = -1;

                        var buffer = new byte[8];
                        var writer = new PacketWriter(buffer);
                        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
                        writer.SetByte(MagicType3End);
                        writer.SetByte(200); // remove all curses
                        target.Send(writer.Written);
                    }
                }

                if (SumType3Durations(target) == 0)
                    target.Type3Flag = false;

                if (target.PartyIndex != -1 && !HasNegativeDot(target))
                    world.SendPartyStatusChange(target.PartyIndex, (short)tid, 1, 0x00);

                break;

            case RemoveType4:
                for (byte buffType = 1; buffType <= 9; buffType++)
                {
                    // Buff slot 3 (transformations) has no removable amount.
                    if (buffType == 3)
                        continue;

                    if (target.Type4Buff[buffType - 1] != 1)
                        continue;

                    ClearType4Buff(target, buffType);
                    SendType4BuffRemove(tid, buffType);
                }

                target.SetSlotItemValue();
                target.SetUserAbility();
                target.SendAiUserUpdate();

                if (SumType4Buffs(target) == 0)
                    target.Type4Flag = false;

                if (target.PartyIndex != -1 && !HasHostileType4(target))
                    world.SendPartyStatusChange(target.PartyIndex, (short)tid, 2, 0x00);

                break;

            case Resurrection:
                target.Regene([1], magicId);
                break;

            case RemoveBless:
                if (target.Type4Buff[0] == 2)
                {
                    ClearType4Buff(target, 1);
                    target.Type4Buff[0] = 0;

                    SendType4BuffRemove(tid, 1);

                    target.SetSlotItemValue();
                    target.SetUserAbility();
                    target.SendAiUserUpdate();

                    if (SumType4Buffs(target) == 0)
                        target.Type4Flag = false;

                    if (target.PartyIndex != -1 && !HasHostileType4(target))
                        world.SendPartyStatusChange(target.PartyIndex, (short)tid, 2, 0x00);
                }

                break;
        }

        if (magic.Type2 is 0 or 5)
        {
            var buffer = new byte[32];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
            writer.SetByte(MagicEffecting);
            writer.SetDWord((uint)magicId);
            writer.SetShort(sid);
            writer.SetShort(tid);
            writer.SetShort(data1);
            writer.SetShort(1);
            writer.SetShort(data3);

            if (IsValidUserId(sid) && srcUser?.UserData is { } srcData)
            {
                world.SendRegion(writer.Written, srcData.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
            }
            else
            {
                world.SendRegion(writer.Written, targetData.Zone, target.RegionX, target.RegionZ, except: null, direct: false);
            }
        }
    }

    /// <summary>True while any curse (negative DoT) slot remains.</summary>
    private static bool HasNegativeDot(GameUser user)
    {
        foreach (short amount in user.HpAmount)
        {
            if (amount < 0)
                return true;
        }

        return false;
    }

    /// <summary>True while any hostile (state 1) type-4 buff remains.</summary>
    private static bool HasHostileType4(GameUser user)
    {
        foreach (byte buff in user.Type4Buff)
        {
            if (buff == 1)
                return true;
        }

        return false;
    }

    private static int SumType3Durations(GameUser user)
    {
        int sum = 0;
        foreach (byte duration in user.HpDuration)
            sum += duration;

        return sum;
    }

    private static int SumType4Buffs(GameUser user)
    {
        int sum = 0;
        foreach (byte buff in user.Type4Buff)
            sum += buff;

        return sum;
    }

    /// <summary>Reset a type-4 buff's amounts (the per-buff blocks of ExecuteType5/Type4Cancel).</summary>
    private static void ClearType4Buff(GameUser target, byte buffType)
    {
        target.DurationType4[buffType] = 0;
        target.StartTimeType4[buffType] = 0.0;
        target.Type4Buff[buffType - 1] = 0;

        switch (buffType)
        {
            case 1: target.MaxHpAmount = 0; break;
            case 2: target.AcAmount = 0; break;
            case 4: target.AttackAmount = 100; break;
            case 5: target.AttackSpeedAmount = 100; break;
            case 6: target.SpeedAmount = 100; break;
            case 7:
                target.StrAmount = 0;
                target.StaAmount = 0;
                target.DexAmount = 0;
                target.IntelAmount = 0;
                target.ChaAmount = 0;
                break;
            case 8:
                target.FireRAmount = 0;
                target.ColdRAmount = 0;
                target.LightningRAmount = 0;
                target.MagicRAmount = 0;
                target.DiseaseRAmount = 0;
                target.PoisonRAmount = 0;
                break;
            case 9:
                target.HitRateAmount = 100;
                target.AvoidRateAmount = 100;
                break;
        }
    }

    /// <summary>CMagicProcess::ExecuteType8 — warp, resurrection and summon spells.</summary>
    public void ExecuteType8(int magicId, int sid, int tid, int data1, int data2, int data3)
    {
        _ = data2;

        MagicType8? type8 = world.MagicType8Table.GetValueOrDefault(magicId);
        if (type8 is null)
            return;

        var castedMembers = new List<int>();

        if (tid == -1)
        {
            // Unlike types 3/4, the C++ scan skips neither dead nor blinking users here.
            for (int i = 0; i < world.Users.Length; i++)
            {
                if (world.Users[i] is null)
                    continue;

                if (UserRegionCheck(sid, i, magicId, type8.Radius, (short)data1, (short)data3))
                    castedMembers.Add(i);
            }

            if (castedMembers.Count == 0)
                return;
        }
        else
        {
            if (GetUser(tid) is null)
                return;

            castedMembers.Add(tid);
        }

        foreach (int userId in castedMembers)
        {
            int result = 1;

            float x = world.Rand(0, 400) / 100.0f;
            float z = world.Rand(0, 400) / 100.0f;

            if (x < 2.5f)
                x += 1.5f;

            if (z < 2.5f)
                z += 1.5f;

            GameUser? target = GetUser(userId);
            if (target?.UserData is not { } targetData)
                continue;

            GameZone? targetMap = world.GetZoneByIndex(target.ZoneIndex);
            if (targetMap is null)
                continue;

            Home? home = world.HomeTable.GetValueOrDefault(targetData.Nation);
            if (home is null)
                return;

            // Warp/summon needs a living target; resurrection (11) a dead one.
            if (type8.WarpType != 11)
            {
                if (target.ResHpType == UserDead)
                    result = 0;
            }
            else if (target.ResHpType != UserDead)
            {
                result = 0;
            }

            if (result != 0 && target.Warp != 0)
                result = 0;

            if (result != 0)
            {
                var warpBuffer = new byte[8];

                switch (type8.WarpType)
                {
                    // Send the target to its resurrection point.
                    case 1:
                    {
                        SendType8Effect(magicId, sid, userId, data1, result, data3, target, targetData);

                        ObjectEvent? bindEvent = targetMap.GetObjectEvent(targetData.Bind);
                        var writer = new PacketWriter(warpBuffer);

                        if (bindEvent is not null)
                        {
                            // C++ quirk kept as-is: the small offset is added
                            // AFTER the decimeter scaling.
                            writer.SetShort((short)(ushort)((bindEvent.PosX * 10) + x));
                            writer.SetShort((short)(ushort)((bindEvent.PosZ * 10) + z));
                        }
                        else if (targetData.Nation != targetData.Zone && targetData.Zone < 3)
                        {
                            // C++ quirk kept as-is: these coordinates are raw
                            // meters — Warp divides them by 10 again.
                            if (targetData.Nation == 1)
                            {
                                writer.SetShort((short)(852 + x));
                                writer.SetShort((short)(164 + z));
                            }
                            else
                            {
                                writer.SetShort((short)(177 + x));
                                writer.SetShort((short)(923 + z));
                            }
                        }
                        else if (targetData.Zone == ZoneBattle)
                        {
                            writer.SetShort((short)(ushort)((home.BattleZoneX * 10) + x));
                            writer.SetShort((short)(ushort)((home.BattleZoneZ * 10) + z));
                        }
                        else if (targetData.Zone == 201) // ZONE_FRONTIER
                        {
                            writer.SetShort((short)(ushort)((home.FreeZoneX * 10) + x));
                            writer.SetShort((short)(ushort)((home.FreeZoneZ * 10) + z));
                        }
                        else
                        {
                            writer.SetShort((short)(ushort)((targetMap.InitX * 10) + x));
                            writer.SetShort((short)(ushort)((targetMap.InitZ * 10) + z));
                        }

                        target.WarpProcess(warpBuffer.AsSpan(0, writer.Index));
                        break;
                    }

                    // Teleport points (2/3/5) are unimplemented upstream.
                    case 2:
                    case 3:
                    case 5:
                        break;

                    // Resurrect a dead player.
                    case 11:
                    {
                        SendType8Effect(magicId, sid, userId, data1, result, data3, target, targetData);

                        target.ResHpType = 1; // USER_STANDING
                        target.HpChange(target.MaxHp);
                        target.ExpChange(type8.ExpRecover / 100); // integer division, like the C++

                        var aiBuffer = new byte[8];
                        var aiWriter = new PacketWriter(aiBuffer);
                        aiWriter.SetByte(AiOpcode.AG_USER_REGENE);
                        aiWriter.SetShort(userId);
                        aiWriter.SetShort(targetData.Zone);
                        world.SendToAiServer?.Invoke(targetData.Zone, aiWriter.Written.ToArray());
                        break;
                    }

                    // Summon a target within the zone.
                    case 12:
                    {
                        if (srcUser?.UserData is not { } srcData || srcData.Zone != targetData.Zone)
                        {
                            result = 0;
                            break;
                        }

                        SendType8Effect(magicId, sid, userId, data1, result, data3, target, targetData);

                        var writer = new PacketWriter(warpBuffer);
                        writer.SetShort((short)(ushort)(srcData.CurX * 10));
                        writer.SetShort((short)(ushort)(srcData.CurZ * 10));
                        target.WarpProcess(warpBuffer.AsSpan(0, writer.Index));
                        break;
                    }

                    // Summon a target across zones (needs CUser::ZoneChange).
                    case 13:
                        logger.LogDebug("MagicProcessor: cross-zone summon deferred to the zone-change slice [magicId={MagicId}]",
                            magicId);
                        result = 0;
                        break;

                    // Randomly teleport the target (within 20 meters).
                    case 20:
                    {
                        SendType8Effect(magicId, sid, userId, data1, result, data3, target, targetData);

                        float warpX = targetData.CurX;
                        float warpZ = targetData.CurZ;

                        float tempX = world.Rand(0, 20);
                        float tempZ = world.Rand(0, 20);

                        warpX = tempX > 10 ? warpX + (tempX - 10) : warpX - tempX;
                        warpZ = tempZ > 10 ? warpZ + (tempZ - 10) : warpZ - tempZ;

                        warpX = Math.Clamp(warpX, 0f, 4096f);
                        warpZ = Math.Clamp(warpZ, 0f, 4096f);

                        // C++ quirk kept as-is: raw meters, Warp divides by 10.
                        var writer = new PacketWriter(warpBuffer);
                        writer.SetShort((short)(ushort)warpX);
                        writer.SetShort((short)(ushort)warpZ);
                        target.WarpProcess(warpBuffer.AsSpan(0, writer.Index));
                        break;
                    }

                    case 21:
                        break; // monster summon, unimplemented upstream

                    default:
                        result = 0;
                        break;
                }
            }

            // The packet_send tail always echoes from the SOURCE user's region.
            if (srcUser?.UserData is { } echoSrc)
            {
                var buffer = new byte[32];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
                writer.SetByte(MagicEffecting);
                writer.SetDWord((uint)magicId);
                writer.SetShort(sid);
                writer.SetShort(userId);
                writer.SetShort(data1);
                writer.SetShort(result);
                writer.SetShort(data3);
                world.SendRegion(writer.Written, echoSrc.Zone, srcUser.RegionX, srcUser.RegionZ, except: null, direct: false);
            }
        }
    }

    private void SendType8Effect(int magicId, int sid, int userId, int data1, int result, int data3,
        GameUser target, UserData targetData)
    {
        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(MagicEffecting);
        writer.SetDWord((uint)magicId);
        writer.SetShort(sid);
        writer.SetShort(userId);
        writer.SetShort(data1);
        writer.SetShort(result);
        writer.SetShort(data3);
        world.SendRegion(writer.Written, targetData.Zone, target.RegionX, target.RegionZ, except: null, direct: false);
    }

    /// <summary>CMagicProcess::GetMagicDamage — resist/staff-adjusted magic damage.</summary>
    public short GetMagicDamage(int sid, int tid, int totalHit, int attribute)
    {
        GameUser? target = GetUser(tid);
        if (target?.UserData is null || target.ResHpType == UserDead)
            return -1;

        short righthandDamage = 0;
        short attributeDamage = 0;
        byte result;

        if (sid >= EbenezerWorld.NpcBand)
        {
            GameNpc? npc = world.Npcs.GetValueOrDefault(sid);
            if (npc is null || npc.NpcState == GameNpc.StateDead)
                return 0;

            result = world.GetHitRate(npc.HitRate / target.TotalEvasionRate);
        }
        else
        {
            totalHit = totalHit * (srcUser?.UserData?.Cha ?? 0) / 170;
            result = 2; // SUCCESS
        }

        short damage = 0;

        if (result != 4) // FAIL
        {
            int totalR = attribute switch
            {
                AttributeFire => target.FireR + target.FireRAmount,
                AttributeCold => target.ColdR + target.ColdRAmount,
                AttributeLightning => target.LightningR + target.LightningRAmount,
                AttributeMagic => target.MagicR + target.MagicRAmount,
                AttributeDisease => target.DiseaseR + target.DiseaseRAmount,
                AttributePoison => target.PoisonR + target.PoisonRAmount,
                _ => 0,
            };

            if (IsValidUserId(sid) && srcUser?.UserData is { } srcData
                && srcData.Items[GameConstants.SlotRightHand].Num != 0)
            {
                Item? rightHand = world.ItemTable.GetValueOrDefault(srcData.Items[GameConstants.SlotRightHand].Num);
                if (rightHand is not null
                    && srcData.Items[GameConstants.SlotLeftHand].Num == 0
                    && rightHand.Kind / 10 == WeaponStaff)
                {
                    righthandDamage = rightHand.Damage;

                    if (srcUser.MagicTypeRightHand == attribute)
                        attributeDamage = rightHand.Damage;
                }
            }

            damage = (short)(totalHit - (0.7 * totalHit * totalR / 200));
            int random = world.Rand(0, damage);
            damage = (short)((0.7 * (totalHit - (0.9 * totalHit * totalR / 200))) + 0.2 * random);

            if (sid >= EbenezerWorld.NpcBand)
            {
                damage = (short)(damage - (3 * righthandDamage) - (3 * attributeDamage));
            }
            else if (attribute != AttributeMagic)
            {
                damage = (short)(damage
                    - ((righthandDamage * 0.8f) + (righthandDamage * (srcUser?.UserData?.Level ?? 0)) / 60)
                    - ((attributeDamage * 0.8f) + (attributeDamage * (srcUser?.UserData?.Level ?? 0)) / 30));
            }
        }

        damage /= 3; // the balancing divisor again

        return damage;
    }

    /// <summary>CMagicProcess::UserRegionCheck — area spell membership test.</summary>
    public bool UserRegionCheck(int sid, int tid, int magicId, int radius, short mouseX, short mouseZ)
    {
        GameUser? target = GetUser(tid);
        if (target?.UserData is not { } targetData)
            return false;

        GameNpc? sourceNpc = null;
        bool sourceIsNpc = false;

        if (sid >= EbenezerWorld.NpcBand)
        {
            sourceNpc = world.Npcs.GetValueOrDefault(sid);
            if (sourceNpc is null || sourceNpc.NpcState == GameNpc.StateDead)
                return false;

            sourceIsNpc = true;
        }

        Magic? magic = world.MagicTable.GetValueOrDefault(magicId);
        if (magic is null)
            return false;

        bool inMoral = false;

        switch (magic.Moral)
        {
            case MoralPartyAll:
                if (target.PartyIndex == -1)
                    return sid == tid;

                if (target.PartyIndex == srcUser?.PartyIndex)
                {
                    if (magic.Type1 == 8
                        && targetData.Zone == ZoneBattle
                        && world.Clock() - target.LastRegeneTime < ClanSummonTime)
                        return false;

                    inMoral = true;
                }

                break;

            case MoralSelfArea:
            case MoralAreaEnemy:
                if (!sourceIsNpc)
                    inMoral = targetData.Nation != srcUser?.UserData?.Nation;
                else
                    inMoral = targetData.Nation != sourceNpc!.Group;

                break;

            case MoralAreaFriend:
                inMoral = targetData.Nation == srcUser?.UserData?.Nation;
                break;

            case MoralClanAll:
                if (targetData.Knights == -1)
                    return sid == tid;

                if (targetData.Knights == srcUser?.UserData?.Knights)
                {
                    if (magic.Type1 == 8
                        && targetData.Zone == ZoneBattle
                        && world.Clock() - target.LastRegeneTime < ClanSummonTime)
                        return false;

                    inMoral = true;
                }

                break;
        }

        if (!inMoral)
            return false;

        if (!sourceIsNpc)
        {
            if (srcUser?.UserData is not { } srcData)
                return false;

            if (targetData.Zone != srcData.Zone
                || target.RegionX != srcUser.RegionX
                || target.RegionZ != srcUser.RegionZ)
                return false;

            if (radius != 0)
            {
                float dx = targetData.CurX - mouseX;
                float dz = targetData.CurZ - mouseZ;
                if (dx * dx + dz * dz > radius * (float)radius)
                    return false;
            }

            return true;
        }

        if (targetData.Zone != sourceNpc!.CurZone
            || target.RegionX != sourceNpc.RegionX
            || target.RegionZ != sourceNpc.RegionZ)
            return false;

        if (radius != 0)
        {
            float dx = targetData.CurX - sourceNpc.CurX;
            float dz = targetData.CurZ - sourceNpc.CurZ;
            if (dx * dx + dz * dz > radius * (float)radius)
                return false;
        }

        return true;
    }

    /// <summary>CMagicProcess::Type4Cancel — remove one buff on client request.</summary>
    public void Type4Cancel(int magicId, int tid)
    {
        GameUser? target = GetUser(tid);
        if (target is null)
            return;

        MagicType4? type4 = world.MagicType4Table.GetValueOrDefault(magicId);
        if (type4 is null)
            return;

        bool removed = false;
        byte buffType = type4.BuffType;

        switch (buffType)
        {
            case 1: removed = target.MaxHpAmount > 0; break;
            case 2: removed = target.AcAmount > 0; break;
            case 3:
                target.DurationType4[3] = 0;
                target.StartTimeType4[3] = 0.0;
                target.StateChange([3, AbnormalNormal]);
                removed = true;
                break;
            case 4: removed = target.AttackAmount > 100; break;
            case 5: removed = target.AttackSpeedAmount > 100; break;
            case 6: removed = target.SpeedAmount > 100; break;
            case 7:
                removed = target.StrAmount + target.StaAmount + target.DexAmount
                    + target.IntelAmount + target.ChaAmount > 0;
                break;
            case 8:
                removed = target.FireRAmount + target.ColdRAmount + target.LightningRAmount
                    + target.MagicRAmount + target.DiseaseRAmount + target.PoisonRAmount > 0;
                break;
            case 9: removed = target.HitRateAmount + target.AvoidRateAmount > 200; break;
        }

        if (removed)
        {
            if (buffType != 3)
                ClearType4Buff(target, buffType);
            else
                target.Type4Buff[2] = 0;

            target.SetSlotItemValue();
            target.SetUserAbility();
            target.SendAiUserUpdate();

            var buffer = new byte[8];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
            writer.SetByte(MagicType4End);
            writer.SetByte(buffType);
            target.Send(writer.Written);
        }

        if (SumType4Buffs(target) == 0)
            target.Type4Flag = false;

        if (target.PartyIndex != -1 && !target.Type4Flag)
            world.SendPartyStatusChange(target.PartyIndex, (short)tid, 2, 0x00);
    }

    /// <summary>CMagicProcess::Type3Cancel — remove one positive DoT/HoT slot.</summary>
    public void Type3Cancel(int magicId, int tid)
    {
        GameUser? target = GetUser(tid);
        if (target is null)
            return;

        MagicType3? type3 = world.MagicType3Table.GetValueOrDefault(magicId);
        if (type3 is null)
            return;

        for (int i = 0; i < target.HpAmount.Length; i++)
        {
            if (target.HpAmount[i] > 0)
            {
                target.HpStartTime[i] = 0.0;
                target.HpLastTime[i] = 0.0;
                target.HpAmount[i] = 0;
                target.HpDuration[i] = 0;
                target.HpInterval[i] = 5;
                target.SourceId[i] = -1;
                break;
            }
        }

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(MagicType3End);
        writer.SetByte(100);
        target.Send(writer.Written);

        if (SumType3Durations(target) == 0)
            target.Type3Flag = false;

        if (target.PartyIndex != -1 && !target.Type3Flag)
            world.SendPartyStatusChange(target.PartyIndex, (short)tid, 1, 0x00);
    }

    /// <summary>CMagicProcess::SendType4BuffRemove.</summary>
    public void SendType4BuffRemove(int tid, byte buff)
    {
        GameUser? target = GetUser(tid);
        if (target is null)
            return;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(MagicType4End);
        writer.SetByte(buff);
        target.Send(writer.Written);
    }

    /// <summary>CMagicProcess::GetWeatherDamage — +10% for the matching element.</summary>
    public short GetWeatherDamage(short damage, short attribute)
    {
        bool weatherBuff = world.Weather switch
        {
            1 => attribute == AttributeFire,      // WEATHER_FINE
            2 => attribute == AttributeLightning, // WEATHER_RAIN
            3 => attribute == AttributeCold,      // WEATHER_SNOW
            _ => false,
        };

        if (weatherBuff)
            damage = (short)(damage * 110 / 100);

        return damage;
    }
}
