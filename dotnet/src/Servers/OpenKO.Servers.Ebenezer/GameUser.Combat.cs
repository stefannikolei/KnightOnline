using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser combat slice (User.cpp): WIZ_ATTACK, the PvP damage formulas
/// (GetDamage/GetMagicDamage/GetACDamage/GetHitRate), exp/level/loyalty/gold
/// consequences, item durability and the death handling.
/// </summary>
public sealed partial class GameUser
{
    // Hit results (GameDefine.h).
    private const byte HitGreatSuccess = 1; // GREAT_SUCCESS
    private const byte HitSuccess = 2;      // SUCCESS
    private const byte HitNormal = 3;       // NORMAL
    private const byte HitFail = 4;         // FAIL

    // e_ItemType (GameDefine.h) — the weapon proc types.
    public const byte ItemTypeFire = 1;
    public const byte ItemTypeCold = 2;
    public const byte ItemTypeLightning = 3;
    public const byte ItemTypePoison = 4;
    public const byte ItemTypeHpDrain = 5;
    public const byte ItemTypeMpDamage = 6;
    public const byte ItemTypeMpDrain = 7;
    public const byte ItemTypeMirrorDamage = 8;

    // e_DurabilityType.
    public const int DurabilityTypeAttack = 1;
    public const int DurabilityTypeDefence = 2;

    private const int ItemGold = 900000000;  // ITEM_GOLD
    private const int MaxItemCount = 9999;   // MAX_ITEM_COUNT
    private const byte CommandAuthority = 0x01; // COMMAND_AUTHORITY (WIZ_AUTHORITY_CHANGE subtype)

    /// <summary>m_sWhoKilledMe.</summary>
    public short WhoKilledMe = -1;

    /// <summary>CUser::Attack — the WIZ_ATTACK handler.</summary>
    public void Attack(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        byte result = reader.GetByte();
        short tid = reader.GetShort();
        float delaytime = reader.GetShort();
        float distance = reader.GetShort();

        if (AbnormalType == AbnormalBlinking)
            return;

        if (ResHpType == UserDeadResHpType || user.Hp == 0)
        {
            logger.LogError("Attack: dead user cannot attack [charId={CharId} resHpType={Res} hp={Hp}]",
                user.CharId, ResHpType, user.Hp);
            return;
        }

        Item? weapon = world.ItemTable.GetValueOrDefault(user.Items[GameConstants.SlotRightHand].Num);
        if (weapon is null && user.Items[GameConstants.SlotRightHand].Num != 0)
            return;

        // Attack-speed check against the weapon delay (empty hands: 100).
        if (weapon is not null)
        {
            if (delaytime < weapon.Delay)
                return;
        }
        else if (delaytime < 100)
        {
            return;
        }

        GameUser? target = null;

        if (tid < EbenezerWorld.NpcBand)
        {
            if (tid < 0 || tid >= world.Users.Length)
                return;

            target = world.Users[tid];
            if (target is null
                || target.ResHpType == UserDeadResHpType
                || target.AbnormalType == AbnormalBlinking
                || target.UserData is not { } targetData
                || targetData.Nation == user.Nation)
            {
                result = 0x00;
            }
            else
            {
                if (weapon is not null && distance > weapon.Range)
                    return;

                int damage = GetDamage(tid, 0);

                // Snowball fights only hurt via thrown snow.
                if (user.Zone == ZoneSnowBattle && world.BattleOpen == SnowBattle)
                    damage = 0;

                if (damage <= 0)
                {
                    result = 0;
                }
                else
                {
                    target.HpChange(-damage, 0, true);
                    ItemWoreOut(DurabilityTypeAttack, damage);
                    target.ItemWoreOut(DurabilityTypeDefence, damage);

                    if (targetData.Hp == 0)
                    {
                        result = 0x02;
                        target.ResHpType = UserDeadResHpType;

                        if (PartyIndex == -1)
                            LoyaltyChange(tid);
                        else
                            LoyaltyDivide(tid);

                        GoldChange(tid, 0);

                        target.InitType3();
                        target.InitType4();

                        // A dying commander loses the command authority.
                        if (targetData.Fame == FameCommandCaptain)
                        {
                            targetData.Fame = FameChief;

                            var authBuffer = new byte[8];
                            var authWriter = new PacketWriter(authBuffer);
                            authWriter.SetByte((byte)GameOpcode.WIZ_AUTHORITY_CHANGE);
                            authWriter.SetByte(CommandAuthority);
                            authWriter.SetShort(target.SocketId);
                            authWriter.SetByte(targetData.Fame);
                            world.SendRegion(authWriter.Written, targetData.Zone, target.RegionX, target.RegionZ);
                            Send(authWriter.Written);

                            // Announcement(KARUS/ELMORAD_CAPTAIN_DEPRIVE_NOTIFY)
                            // attaches with the chat slice (DB string resources).
                        }

                        target.WhoKilledMe = SocketId;

                        if (targetData.Zone != targetData.Nation && targetData.Zone < 3)
                            target.ExpChange(-target.MaxExp / 100);
                    }

                    SendTargetHP(0, tid, -damage);
                }
            }
        }
        else
        {
            if (!world.PointCheckFlag)
                return;

            GameNpc? npc = world.Npcs.GetValueOrDefault(tid);
            if (npc is not null && npc.NpcState != GameNpc.StateDead && npc.HP > 0)
            {
                if (weapon is not null && distance > weapon.Range)
                    return;

                var aiBuffer = new byte[64];
                var aiWriter = new PacketWriter(aiBuffer);
                aiWriter.SetByte(AiOpcode.AG_ATTACK_REQ);
                aiWriter.SetByte(type);
                aiWriter.SetByte(result);
                aiWriter.SetShort(SocketId);
                aiWriter.SetShort(tid);
                aiWriter.SetShort(TotalHit * AttackAmount / 100);
                aiWriter.SetShort(TotalAc + AcAmount);
                aiWriter.SetFloat(TotalHitRate);
                aiWriter.SetFloat(TotalEvasionRate);
                aiWriter.SetShort(ItemAc);
                aiWriter.SetByte(MagicTypeLeftHand);
                aiWriter.SetByte(MagicTypeRightHand);
                aiWriter.SetShort(MagicAmountLeftHand);
                aiWriter.SetShort(MagicAmountRightHand);
                world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
                return;
            }
        }

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ATTACK);
        writer.SetByte(type);
        writer.SetByte(result);
        writer.SetShort(SocketId);
        writer.SetShort(tid);
        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, except: null, direct: false);

        // The victim gets the dead packet immediately once more (ghost fix).
        if (tid < EbenezerWorld.NpcBand && result == 0x02)
            target?.Send(writer.Written);
    }

    /// <summary>CUser::GetDamage — PvP damage (magicid 0 = normal hit).</summary>
    public short GetDamage(int tid, int magicid)
    {
        short damage = 0;
        short tempHit = 0;
        byte result = HitFail;

        GameUser? target = tid >= 0 && tid < world.Users.Length ? world.Users[tid] : null;
        if (target?.UserData is null || target.ResHpType == UserDeadResHpType)
            return -1;

        int tempAc = target.TotalAc + target.AcAmount;
        var tempHitB = (short)((TotalHit * AttackAmount * 200 / 100) / (tempAc + 240));

        Magic? magic = null;

        if (magicid > 0)
        {
            magic = world.MagicTable.GetValueOrDefault(magicid);
            if (magic is null)
                return -1;

            if (magic.Type1 == 1)
            {
                MagicType1? type1 = world.MagicType1Table.GetValueOrDefault(magicid);
                if (type1 is null)
                    return -1;

                if (type1.Type != 0)
                {
                    result = type1.HitRateMod <= world.Rand(0, 100) ? HitFail : HitSuccess;
                }
                else
                {
                    result = GetHitRate(TotalHitRate / target.TotalEvasionRate * (type1.HitRateMod / 100.0f));
                }

                tempHit = (short)(tempHitB * (type1.DamageMod / 100.0f));
            }
            else if (magic.Type1 == 2)
            {
                MagicType2? type2 = world.MagicType2Table.GetValueOrDefault(magicid);
                if (type2 is null)
                    return -1;

                if (type2.HitType is 1 or 2)
                {
                    result = type2.HitRateMod <= world.Rand(0, 100) ? HitFail : HitSuccess;
                }
                else
                {
                    result = GetHitRate(TotalHitRate / target.TotalEvasionRate * (type2.HitRateMod / 100.0f));
                }

                if (type2.HitType == 1)
                    tempHit = (short)(TotalHit * AttackAmount * (type2.DamageMod / 100.0f) / 100);
                else
                    tempHit = (short)(tempHitB * (type2.DamageMod / 100.0f));
            }
        }
        else
        {
            tempHit = (short)(TotalHit * AttackAmount / 100);
            result = GetHitRate(TotalHitRate / target.TotalEvasionRate);
        }

        switch (result)
        {
            case HitGreatSuccess:
            case HitSuccess:
            case HitNormal:
                if (magicid > 0)
                {
                    int random = world.Rand(0, tempHit);
                    if (magic!.Type1 == 1)
                        damage = (short)((tempHit + 0.3f * random) + 0.99f);
                    else
                        damage = (short)(((tempHit * 0.6f) + 1.0f * random) + 0.99f);
                }
                else
                {
                    int random = world.Rand(0, tempHitB);
                    damage = (short)((0.85f * tempHitB) + 0.3f * random);
                }

                break;

            default:
                damage = 0;
                break;
        }

        damage = GetMagicDamage(damage, tid);
        damage = GetACDamage(damage, tid);
        damage /= 3; // the infamous balancing divisor

        return damage;
    }

    /// <summary>CUser::GetMagicDamage — elemental weapon procs vs target resistances.</summary>
    public short GetMagicDamage(int damage, int tid)
    {
        GameUser? target = tid >= 0 && tid < world.Users.Length ? world.Users[tid] : null;
        if (target?.UserData is null || target.ResHpType == UserDeadResHpType)
            return (short)damage;

        short totalR = 0;
        short tempDamage = 0;

        // RIGHT HAND.
        if (MagicTypeRightHand is > 4 and < 8)
            tempDamage = (short)(damage * MagicAmountRightHand / 100);

        switch (MagicTypeRightHand)
        {
            case ItemTypeFire: totalR = (short)(target.FireR + target.FireRAmount); break;
            case ItemTypeCold: totalR = (short)(target.ColdR + target.ColdRAmount); break;
            case ItemTypeLightning: totalR = (short)(target.LightningR + target.LightningRAmount); break;
            case ItemTypePoison: totalR = (short)(target.PoisonR + target.PoisonRAmount); break;
            case ItemTypeHpDrain: HpChange(tempDamage, 0); break;
            case ItemTypeMpDamage: target.MSpChange(-tempDamage); break;
            case ItemTypeMpDrain: MSpChange(tempDamage); break;
        }

        if (MagicTypeRightHand is > 0 and < 5)
        {
            if (totalR > 200)
                totalR = 200;

            tempDamage = (short)(MagicAmountRightHand - MagicAmountRightHand * totalR / 200);
            damage += tempDamage;
        }

        totalR = 0;
        tempDamage = 0;

        // LEFT HAND.
        if (MagicTypeLeftHand is > 4 and < 8)
            tempDamage = (short)(damage * MagicAmountLeftHand / 100);

        switch (MagicTypeLeftHand)
        {
            case ItemTypeFire: totalR = (short)(target.FireR + target.FireRAmount); break;
            case ItemTypeCold: totalR = (short)(target.ColdR + target.ColdRAmount); break;
            case ItemTypeLightning: totalR = (short)(target.LightningR + target.LightningRAmount); break;
            case ItemTypePoison: totalR = (short)(target.PoisonR + target.PoisonRAmount); break;
            case ItemTypeHpDrain: HpChange(tempDamage, 0); break;
            case ItemTypeMpDamage: target.MSpChange(-tempDamage); break;
            case ItemTypeMpDrain: MSpChange(tempDamage); break;
        }

        if (MagicTypeLeftHand is > 0 and < 5)
        {
            if (totalR > 200)
                totalR = 200;

            tempDamage = (short)(MagicAmountLeftHand - MagicAmountLeftHand * totalR / 200);
            damage += tempDamage;
        }

        tempDamage = 0;

        // Mirror damage reflects onto the attacker.
        if (target.MagicTypeLeftHand == ItemTypeMirrorDamage)
        {
            tempDamage = (short)(damage * target.MagicAmountLeftHand / 100);
            HpChange(-tempDamage);
        }

        return (short)damage;
    }

    /// <summary>CUser::GetACDamage — weapon-kind specific target armor.</summary>
    public short GetACDamage(int damage, int tid)
    {
        GameUser? target = tid >= 0 && tid < world.Users.Length ? world.Users[tid] : null;
        if (target?.UserData is null || target.ResHpType == UserDeadResHpType)
            return (short)damage;

        if (UserData is not { } user)
            return (short)damage;

        foreach (int slot in (int[])[GameConstants.SlotRightHand, GameConstants.SlotLeftHand])
        {
            if (user.Items[slot].Num == 0)
                continue;

            Item? hand = world.ItemTable.GetValueOrDefault(user.Items[slot].Num);
            if (hand is null)
                continue;

            switch (hand.Kind / 10)
            {
                case WeaponDagger: damage -= damage * target.DaggerR / 200; break;
                case WeaponSword: damage -= damage * target.SwordR / 200; break;
                case WeaponAxe: damage -= damage * target.AxeR / 200; break;
                case WeaponMace: damage -= damage * target.MaceR / 200; break;
                case WeaponSpear: damage -= damage * target.SpearR / 200; break;
                case WeaponBow: damage -= damage * target.BowR / 200; break;
            }
        }

        return (short)damage;
    }

    /// <summary>CUser::GetHitRate — the banded 1..10000 hit roll.</summary>
    public byte GetHitRate(float rate) => world.GetHitRate(rate);

    /// <summary>CUser::SendTargetHP — the WIZ_TARGET_HP reply to the attacker.</summary>
    public void SendTargetHP(byte echo, int tid, int damage)
    {
        int hp, maxHp;

        if (tid < 0)
            return;

        if (tid >= EbenezerWorld.NpcBand)
        {
            if (!world.PointCheckFlag)
                return;

            GameNpc? npc = world.Npcs.GetValueOrDefault(tid);
            if (npc is null)
                return;

            hp = npc.HP;
            maxHp = npc.MaxHP;
        }
        else
        {
            GameUser? target = tid < world.Users.Length ? world.Users[tid] : null;
            if (target?.UserData is not { } targetData || target.ResHpType == UserDeadResHpType)
                return;

            hp = targetData.Hp;
            maxHp = target.MaxHp;
        }

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_TARGET_HP);
        writer.SetShort(tid);
        writer.SetByte(echo);
        writer.SetDWord((uint)maxHp);
        writer.SetDWord((uint)hp);
        writer.SetShort(damage);
        Send(writer.Written);
    }

    /// <summary>CUser::ExpChange — grants/penalizes exp, drives level changes.</summary>
    public void ExpChange(int exp)
    {
        if (UserData is not { } user)
            return;

        if (user.Level < 6 && exp < 0)
            return;

        if (user.Zone == ZoneBattle && exp < 0)
            return;

        user.Exp += exp;

        if (user.Exp < 0)
        {
            if (user.Level > 5)
            {
                --user.Level;
                user.Exp += world.LevelUpTable.GetValueOrDefault(user.Level);
                LevelChange(user.Level, levelUp: false);
                return;
            }
        }
        else if (user.Exp >= MaxExp)
        {
            if (user.Level >= MaxLevel)
            {
                user.Exp = MaxExp;
                return;
            }

            user.Exp -= MaxExp;
            ++user.Level;

            LevelChange(user.Level);
            return;
        }

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_EXP_CHANGE);
        writer.SetDWord((uint)user.Exp);
        Send(writer.Written);

        if (exp < 0)
            LostExp = -exp;
    }

    /// <summary>CUser::LevelChange — stat/skill points, refills and the region broadcast.</summary>
    public void LevelChange(short level, bool levelUp = true)
    {
        if (level < 1 || level > MaxLevel)
            return;

        if (UserData is not { } user)
            return;

        if (levelUp)
        {
            if (user.Points + user.Sta + user.Str + user.Dex + user.Intel + user.Cha < 300 + 3 * (level - 1))
                user.Points += 3;

            if (level > 9
                && user.Skills[0] + user.Skills[1] + user.Skills[2] + user.Skills[3] + user.Skills[4]
                 + user.Skills[5] + user.Skills[6] + user.Skills[7] + user.Skills[8] < 2 * (level - 9))
                user.Skills[0] += 2;
        }

        MaxExp = world.LevelUpTable.GetValueOrDefault(level);

        SetSlotItemValue();
        SetUserAbility();

        user.Mp = MaxMp;
        HpChange(MaxHp);

        SendAiUserUpdate();

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_LEVEL_CHANGE);
        writer.SetShort(SocketId);
        writer.SetByte(user.Level);
        writer.SetByte(user.Points);
        writer.SetByte(user.Skills[0]);
        writer.SetDWord((uint)MaxExp);
        writer.SetDWord((uint)user.Exp);
        writer.SetShort(MaxHp);
        writer.SetShort(user.Hp);
        writer.SetShort(MaxMp);
        writer.SetShort(user.Mp);
        writer.SetShort(GetMaxWeightForClient());
        writer.SetShort(GetCurrentWeightForClient());
        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ);

        // WIZ_PARTY/PARTY_LEVELCHANGE attaches with the party slice.
    }

    /// <summary>CUser::Send2AI_UserUpdateInfo.</summary>
    public void SendAiUserUpdate()
    {
        if (UserData is not { } user)
            return;

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_UPDATE);
        writer.SetShort(SocketId);
        writer.SetByte(user.Level);
        writer.SetShort(user.Hp);
        writer.SetShort(user.Mp);
        writer.SetShort(TotalHit * AttackAmount / 100);
        writer.SetShort(TotalAc + AcAmount);
        writer.SetFloat(TotalHitRate);
        writer.SetFloat(TotalEvasionRate);
        writer.SetShort(ItemAc);
        writer.SetByte(MagicTypeLeftHand);
        writer.SetByte(MagicTypeRightHand);
        writer.SetShort(MagicAmountLeftHand);
        writer.SetShort(MagicAmountRightHand);
        world.SendToAiServer?.Invoke(user.Zone, writer.Written.ToArray());
    }

    /// <summary>CUser::LoyaltyChange — national points for a PvP kill.</summary>
    public void LoyaltyChange(int tid)
    {
        if (UserData is not { } user)
            return;

        GameUser? target = tid >= 0 && tid < world.Users.Length ? world.Users[tid] : null;
        if (target?.UserData is not { } targetData)
            return;

        short loyaltySource, loyaltyTarget;

        if (targetData.Nation != user.Nation)
        {
            int levelDifference = targetData.Level - user.Level;

            if (targetData.Loyalty <= 0)
            {
                loyaltySource = 0;
                loyaltyTarget = 0;
            }
            else if (levelDifference > 5)
            {
                loyaltySource = 50;
                loyaltyTarget = -25;
            }
            else if (levelDifference < -5)
            {
                loyaltySource = 10;
                loyaltyTarget = -5;
            }
            else
            {
                loyaltySource = 30;
                loyaltyTarget = -15;
            }
        }
        else
        {
            if (targetData.Loyalty >= 0)
            {
                loyaltySource = -1000;
                loyaltyTarget = -15;
            }
            else
            {
                loyaltySource = 100;
                loyaltyTarget = -15;
            }
        }

        if (user.Zone != user.Nation && user.Zone < 3)
            loyaltySource *= 2;

        user.Loyalty += loyaltySource;
        targetData.Loyalty += loyaltyTarget;

        if (user.Loyalty < 0)
            user.Loyalty = 0;

        if (targetData.Loyalty < 0)
            targetData.Loyalty = 0;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_LOYALTY_CHANGE);
        writer.SetDWord((uint)user.Loyalty);
        Send(writer.Written);

        writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_LOYALTY_CHANGE);
        writer.SetDWord((uint)targetData.Loyalty);
        target.Send(writer.Written);

        // Wednesday battle-event kill counters.
        if (world.BattleOpen != NoBattle && user.Zone == ZoneBattle)
        {
            if (targetData.Nation == Karus)
                ++world.KarusDead;
            else if (targetData.Nation == Elmorad)
                ++world.ElmoradDead;
        }
    }

    /// <summary>CUser::LoyaltyDivide — the party variant (full port with the party slice).</summary>
    public void LoyaltyDivide(int tid)
    {
        _ = tid;

        // The C++ needs the _PARTY_GROUP for the level average; PartyIndex is
        // always -1 until the party slice lands, matching the early return.
        if (PartyIndex < 0)
            return;
    }

    /// <summary>CUser::GoldChange — battle/frontier-zone money transfer on a kill.</summary>
    public void GoldChange(int tid, int gold)
    {
        if (UserData is not { } user)
            return;

        // Money only changes hands in the frontier/battle zones.
        if (user.Zone < 3)
            return;

        if (user.Zone == ZoneSnowBattle)
            return;

        GameUser? target = tid >= 0 && tid < world.Users.Length ? world.Users[tid] : null;
        if (target?.UserData is not { } targetData)
            return;

        if (targetData.Gold <= 0)
            return;

        int sourceGold, targetGold;
        byte sourceType, targetType;

        if (gold == 0)
        {
            if (PartyIndex != -1)
            {
                // The party loot-share path attaches with the party slice.
                return;
            }

            sourceType = GoldChangeGain;
            targetType = GoldChangeLose;

            sourceGold = targetData.Gold * 4 / 10;
            targetGold = targetData.Gold / 2;

            user.Gold += sourceGold;
            targetData.Gold -= targetGold;
        }
        else if (gold > 0)
        {
            sourceType = GoldChangeGain;
            targetType = GoldChangeLose;

            sourceGold = gold;
            targetGold = gold;

            user.Gold += sourceGold;
            targetData.Gold -= targetGold;
        }
        else
        {
            sourceType = GoldChangeLose;
            targetType = GoldChangeGain;

            sourceGold = gold;
            targetGold = gold;

            user.Gold -= sourceGold;
            targetData.Gold += targetGold;
        }

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_GOLD_CHANGE);
        writer.SetByte(sourceType);
        writer.SetDWord((uint)sourceGold);
        writer.SetDWord((uint)user.Gold);
        Send(writer.Written);

        writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_GOLD_CHANGE);
        writer.SetByte(targetType);
        writer.SetDWord((uint)targetGold);
        writer.SetDWord((uint)targetData.Gold);
        target.Send(writer.Written);
    }

    private const byte GoldChangeGain = 1; // GOLD_CHANGE_GAIN
    private const byte GoldChangeLose = 2; // GOLD_CHANGE_LOSE

    /// <summary>CUser::ItemWoreOut — durability loss on attack/defence.</summary>
    public void ItemWoreOut(int type, int damage)
    {
        if (UserData is not { } user)
            return;

        var wearRate = (int)Math.Sqrt(damage / 10.0);
        if (wearRate == 0)
            return;

        if (type == DurabilityTypeAttack)
        {
            // Weapons wear (defence items in the hand slots — shields — do not).
            WearSlot(user, GameConstants.SlotRightHand, wearRate, requireSlotType: slot => slot != 2);
            WearSlot(user, GameConstants.SlotLeftHand, wearRate, requireSlotType: slot => slot != 2);
        }
        else if (type == DurabilityTypeDefence)
        {
            WearSlot(user, GameConstants.SlotHead, wearRate, requireSlotType: null);
            WearSlot(user, GameConstants.SlotBreast, wearRate, requireSlotType: null);
            WearSlot(user, GameConstants.SlotLeg, wearRate, requireSlotType: null);
            WearSlot(user, GameConstants.SlotGlove, wearRate, requireSlotType: null);
            WearSlot(user, GameConstants.SlotFoot, wearRate, requireSlotType: null);
            WearSlot(user, GameConstants.SlotRightHand, wearRate, requireSlotType: slot => slot == 2);
            WearSlot(user, GameConstants.SlotLeftHand, wearRate, requireSlotType: slot => slot == 2);
        }
    }

    private void WearSlot(UserData user, int slot, int wearRate, Func<byte, bool>? requireSlotType)
    {
        if (user.Items[slot].Num == 0 || user.Items[slot].Duration == 0)
            return;

        Item? table = world.ItemTable.GetValueOrDefault(user.Items[slot].Num);
        if (table is null)
            return;

        if (requireSlotType is not null && !requireSlotType(table.Slot))
            return;

        user.Items[slot].Duration -= (short)wearRate;
        ItemDurationChange(slot, table.Durability, user.Items[slot].Duration, wearRate);
    }

    /// <summary>CUser::ItemDurationChange — WIZ_DURATION notifications + break handling.</summary>
    public void ItemDurationChange(int slot, int maxValue, int curValue, int amount)
    {
        if (UserData is not { } user)
            return;

        if (maxValue <= 0)
            return;

        // C++ quirk kept as-is: the upper bound allows slot == SLOT_MAX.
        if (slot < 0 || slot > GameConstants.SlotMax)
            return;

        if (user.Items[slot].Duration <= 0)
        {
            // The item broke: notify, then re-derive the halved stats.
            user.Items[slot].Duration = 0;

            var buffer = new byte[8];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_DURATION);
            writer.SetByte((byte)slot);
            writer.SetShort(0);
            Send(writer.Written);

            SetSlotItemValue();
            SetUserAbility();

            var move = new byte[64];
            var moveWriter = new PacketWriter(move);
            moveWriter.SetByte((byte)GameOpcode.WIZ_ITEM_MOVE);
            moveWriter.SetByte(0x01);
            moveWriter.SetShort(TotalHit);
            moveWriter.SetShort(TotalAc);
            moveWriter.SetShort(GetCurrentWeightForClient());
            moveWriter.SetShort(MaxHp);
            moveWriter.SetShort(MaxMp);
            moveWriter.SetShort(ItemStr + StrAmount);
            moveWriter.SetShort(ItemSta + StaAmount);
            moveWriter.SetShort(ItemDex + DexAmount);
            moveWriter.SetShort(ItemIntel + IntelAmount);
            moveWriter.SetShort(ItemCham + ChaAmount);
            moveWriter.SetShort(FireR);
            moveWriter.SetShort(ColdR);
            moveWriter.SetShort(LightningR);
            moveWriter.SetShort(MagicR);
            moveWriter.SetShort(DiseaseR);
            moveWriter.SetShort(PoisonR);
            Send(moveWriter.Written);
            return;
        }

        var curPercent = (int)(curValue / (double)maxValue * 100);
        var beforePercent = (int)((curValue + amount) / (double)maxValue * 100);

        if (curPercent / 5 != beforePercent / 5)
        {
            var buffer = new byte[8];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_DURATION);
            writer.SetByte((byte)slot);
            writer.SetShort(curValue);
            Send(writer.Written);

            if (curPercent is >= 65 and < 70)
                UserLookChange(slot, user.Items[slot].Num, curValue);

            if (curPercent is >= 25 and < 30)
                UserLookChange(slot, user.Items[slot].Num, curValue);
        }
    }

    /// <summary>CUser::UserLookChange — visible equipment change broadcast.</summary>
    public void UserLookChange(int pos, int itemId, int durability)
    {
        if (UserData is not { } user)
            return;

        if (pos >= GameConstants.SlotMax)
            return;

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_USERLOOK_CHANGE);
        writer.SetShort(SocketId);
        writer.SetByte((byte)pos);
        writer.SetDWord((uint)itemId);
        writer.SetShort(durability);
        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, this);
    }

    /// <summary>CUser::GiveItem — puts an item into the inventory (WIZ_ITEM_COUNT_CHANGE).</summary>
    public bool GiveItem(int itemId, short count)
    {
        if (UserData is not { } user)
            return false;

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
            return false;

        int pos = GetEmptySlot(itemId, table.Countable);
        if (pos == 0xFF)
            return false;

        if (pos >= GameConstants.HaveMax)
            return false;

        ref ItemData slot = ref user.Items[GameConstants.SlotMax + pos];

        if (slot.Num != 0)
        {
            if (table.Countable != 1)
                return false;

            if (slot.Num != itemId)
                return false;
        }

        slot.Num = itemId;

        if (table.Countable != 0)
        {
            slot.Count += count;
            if (slot.Count > MaxItemCount)
                slot.Count = MaxItemCount;
        }
        else
        {
            slot.Count = 1;
        }

        slot.Duration = table.Durability;

        SendItemWeight();

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_COUNT_CHANGE);
        writer.SetShort(1);
        writer.SetByte(1);
        writer.SetByte((byte)pos);
        writer.SetDWord((uint)itemId);
        writer.SetDWord((uint)user.Items[GameConstants.SlotMax + pos].Count);
        Send(writer.Written);
        return true;
    }

    /// <summary>CUser::GetEmptySlot.</summary>
    public int GetEmptySlot(int itemId, int countable)
    {
        if (UserData is not { } user)
            return 0xFF;

        int pos = 0xFF;

        if (countable == -1)
        {
            Item? table = world.ItemTable.GetValueOrDefault(itemId);
            if (table is null)
                return pos;

            countable = table.Countable;
        }

        if (itemId == ItemGold)
            return pos;

        for (int i = 0; i < GameConstants.HaveMax; i++)
        {
            if (user.Items[GameConstants.SlotMax + i].Num != 0)
                continue;

            pos = i;
            break;
        }

        if (countable != 0)
        {
            for (int i = 0; i < GameConstants.HaveMax; i++)
            {
                if (user.Items[GameConstants.SlotMax + i].Num == itemId)
                    return i;
            }
        }

        return pos;
    }

    /// <summary>CUser::SendItemWeight.</summary>
    public void SendItemWeight()
    {
        SetSlotItemValue();

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_WEIGHT_CHANGE);
        writer.SetShort(GetCurrentWeightForClient());
        Send(writer.Written);
    }

    /// <summary>CUser::EventMoneyItemGet — a no-op upstream (fully commented out in the C++).</summary>
    public void EventMoneyItemGet(int itemId, int count)
    {
        _ = itemId;
        _ = count;
    }
}
