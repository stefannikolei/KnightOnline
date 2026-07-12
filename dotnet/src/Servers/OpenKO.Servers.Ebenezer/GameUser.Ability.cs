using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser stat/ability layer (User.cpp): item bonuses (SetSlotItemValue),
/// the class-coefficient formulas (SetUserAbility, SetMaxHp/SetMaxMp), the
/// derived detail data and the HP/MP change packets.
/// </summary>
public sealed partial class GameUser
{
    private const int MaxLevel = 80;         // MAX_LEVEL
    private const int MaxType3Repeat = 10;   // MAX_TYPE3_REPEAT
    private const int MaxType4Buff = 9;      // MAX_TYPE4_BUFF
    private const int ClassBerserker = 105;  // BERSERKER
    private const int ClassBlade = 205;      // BLADE

    // Weapon kinds (GameDefine.h, pTable->Kind / 10).
    private const int WeaponDagger = 1;
    private const int WeaponSword = 2;
    private const int WeaponAxe = 3;
    private const int WeaponMace = 4;
    private const int WeaponSpear = 5;
    private const int WeaponBow = 7;
    private const int WeaponLongBow = 8;
    private const int WeaponLauncher = 10;
    private const int WeaponStaff = 11;

    // ---- derived stats (CUser members) ----
    public short BodyAc;
    public short TotalHit;
    public short TotalAc;
    public float TotalHitRate;
    public float TotalEvasionRate;

    public short ItemMaxHp;
    public short ItemMaxMp;
    public int ItemWeight;
    public short ItemHit;
    public short ItemAc;
    public short ItemStr;
    public short ItemSta;
    public short ItemDex;
    public short ItemIntel;
    public short ItemCham;
    public short ItemHitrate = 100;
    public short ItemEvasionrate = 100;

    public byte FireR;
    public byte ColdR;
    public byte LightningR;
    public byte MagicR;
    public byte DiseaseR;
    public byte PoisonR;

    public short DaggerR;
    public short SwordR;
    public short AxeR;
    public short MaceR;
    public short SpearR;
    public short BowR;

    public byte MagicTypeLeftHand;
    public byte MagicTypeRightHand;
    public short MagicAmountLeftHand;
    public short MagicAmountRightHand;

    public short MaxHp;
    public short MaxMp = 1;
    public int MaxExp;
    public int MaxWeight;

    public short ZoneIndex;
    public short RegionX = -1;
    public short RegionZ = -1;
    public float WillX;
    public float WillZ;
    public float WillY;

    public short PartyIndex = -1;
    public byte AbnormalType = 1;
    public int LostExp;

    // ---- type-4 buff amounts (InitType4 defaults) ----
    public byte AttackSpeedAmount = 100;
    public byte SpeedAmount = 100;
    public short AcAmount;
    public byte AttackAmount = 100;
    public short MaxHpAmount;
    public byte HitRateAmount = 100;
    public short AvoidRateAmount = 100;
    public short StrAmount;
    public short StaAmount;
    public short DexAmount;
    public short IntelAmount;
    public short ChaAmount;
    public readonly byte[] Type4Buff = new byte[MaxType4Buff];
    public bool Type4Flag;

    // ---- type-3 duration slots ----
    public readonly double[] HpStartTime = new double[MaxType3Repeat];
    public readonly double[] HpLastTime = new double[MaxType3Repeat];
    public readonly short[] HpAmount = new short[MaxType3Repeat];
    public readonly byte[] HpDuration = new byte[MaxType3Repeat];
    public readonly byte[] HpInterval = new byte[MaxType3Repeat];
    public readonly short[] SourceId = new short[MaxType3Repeat];
    public bool Type3Flag;

    /// <summary>CUser::InitType3.</summary>
    public void InitType3()
    {
        for (int i = 0; i < MaxType3Repeat; i++)
        {
            HpStartTime[i] = 0.0;
            HpLastTime[i] = 0.0;
            HpAmount[i] = 0;
            HpDuration[i] = 0;
            HpInterval[i] = 5;
            SourceId[i] = -1;
        }

        Type3Flag = false;
    }

    /// <summary>CUser::InitType4.</summary>
    public void InitType4()
    {
        AttackSpeedAmount = 100;
        SpeedAmount = 100;
        AcAmount = 0;
        AttackAmount = 100;
        MaxHpAmount = 0;
        HitRateAmount = 100;
        AvoidRateAmount = 100;
        StrAmount = 0;
        StaAmount = 0;
        DexAmount = 0;
        IntelAmount = 0;
        ChaAmount = 0;
        AbnormalType = 1;

        Array.Clear(Type4Buff);
        Type4Flag = false;
    }

    /// <summary>CUser::SetSlotItemValue — recomputes every item bonus from the 14 equip slots.</summary>
    public void SetSlotItemValue()
    {
        if (UserData is not { } user)
            return;

        ItemMaxHp = ItemMaxMp = 0;
        ItemHit = ItemAc = ItemStr = ItemSta = ItemDex = ItemIntel = ItemCham = 0;
        ItemHitrate = ItemEvasionrate = 100;
        ItemWeight = 0;

        FireR = ColdR = LightningR = MagicR = DiseaseR = PoisonR = 0;
        DaggerR = SwordR = AxeR = MaceR = SpearR = BowR = 0;

        MagicTypeLeftHand = MagicTypeRightHand = 0;
        MagicAmountLeftHand = MagicAmountRightHand = 0;

        for (int i = 0; i < GameConstants.SlotMax; i++)
        {
            if (user.Items[i].Num <= 0)
                continue;

            Item? table = world.ItemTable.GetValueOrDefault(user.Items[i].Num);
            if (table is null)
                continue;

            // Broken items only give half their stats.
            int itemHit = user.Items[i].Duration == 0 ? table.Damage / 2 : table.Damage;
            int itemAc = user.Items[i].Duration == 0 ? table.Armor / 2 : table.Armor;

            if (i == GameConstants.SlotRightHand)
                ItemHit += (short)itemHit;

            if (i == GameConstants.SlotLeftHand
                && user.Class is ClassBerserker or ClassBlade)
                ItemHit += (short)(itemHit * 0.5f);

            ItemMaxHp += table.MaxHpBonus;
            ItemMaxMp += table.MaxMpBonus;
            ItemAc += (short)itemAc;
            ItemStr += table.StrengthBonus;
            ItemSta += table.StaminaBonus;
            ItemDex += table.DexterityBonus;
            ItemIntel += table.IntelligenceBonus;
            ItemCham += table.CharismaBonus;
            ItemHitrate += table.HitRate;
            ItemEvasionrate += table.EvasionRate;

            FireR += (byte)table.FireResist;
            ColdR += (byte)table.ColdResist;
            LightningR += (byte)table.LightningResist;
            MagicR += (byte)table.MagicResist;
            DiseaseR += (byte)table.CurseResist;
            PoisonR += (byte)table.PoisonResist;

            DaggerR += table.DaggerArmor;
            SwordR += table.SwordArmor;
            AxeR += table.AxeArmor;
            MaceR += table.MaceArmor;
            SpearR += table.SpearArmor;
            BowR += table.BowArmor;
        }

        // Weight covers the whole inventory.
        for (int i = 0; i < GameConstants.InventoryTotal; i++)
        {
            if (user.Items[i].Num <= 0)
                continue;

            Item? table = world.ItemTable.GetValueOrDefault(user.Items[i].Num);
            if (table is null)
                continue;

            if (table.Countable == 0)
                ItemWeight += table.Weight;
            else
                ItemWeight += table.Weight * user.Items[i].Count;
        }

        if (ItemHit < 3)
            ItemHit = 3;

        // Elemental weapon procs (left/right hand).
        ApplyHandMagic(user.Items[GameConstants.SlotLeftHand].Num, ref MagicTypeLeftHand, ref MagicAmountLeftHand);
        ApplyHandMagic(user.Items[GameConstants.SlotRightHand].Num, ref MagicTypeRightHand, ref MagicAmountRightHand);
    }

    private void ApplyHandMagic(int itemId, ref byte magicType, ref short magicAmount)
    {
        Item? item = world.ItemTable.GetValueOrDefault(itemId);
        if (item is null)
            return;

        // Later checks override earlier ones, exactly like the C++ if-chain.
        if (item.FireDamage != 0) { magicType = 1; magicAmount = item.FireDamage; }
        if (item.IceDamage != 0) { magicType = 2; magicAmount = item.IceDamage; }
        if (item.LightningDamage != 0) { magicType = 3; magicAmount = item.LightningDamage; }
        if (item.PoisonDamage != 0) { magicType = 4; magicAmount = item.PoisonDamage; }
        if (item.HpDrain != 0) { magicType = 5; magicAmount = item.HpDrain; }
        if (item.MpDamage != 0) { magicType = 6; magicAmount = item.MpDamage; }
        if (item.MpDrain != 0) { magicType = 7; magicAmount = item.MpDrain; }
        if (item.MirrorDamage != 0) { magicType = 8; magicAmount = item.MirrorDamage; }
    }

    /// <summary>CUser::SetUserAbility — total hit/ac/rates from the class coefficients.</summary>
    public void SetUserAbility()
    {
        if (UserData is not { } user)
            return;

        Coefficient? coefficient = world.CoefficientTable.GetValueOrDefault(user.Class);
        if (coefficient is null)
            return;

        double hitCoefficient = 0.0;
        bool haveBow = false;
        Item? weapon = null;

        if (user.Items[GameConstants.SlotRightHand].Num != 0)
        {
            weapon = world.ItemTable.GetValueOrDefault(user.Items[GameConstants.SlotRightHand].Num);
            if (weapon is not null)
            {
                switch (weapon.Kind / 10)
                {
                    case WeaponDagger: hitCoefficient = coefficient.ShortSword; break;
                    case WeaponSword: hitCoefficient = coefficient.Sword; break;
                    case WeaponAxe: hitCoefficient = coefficient.Axe; break;
                    case WeaponMace: hitCoefficient = coefficient.Club; break;
                    case WeaponSpear: hitCoefficient = coefficient.Spear; break;
                    case WeaponBow:
                    case WeaponLongBow:
                    case WeaponLauncher:
                        hitCoefficient = coefficient.Bow;
                        haveBow = true;
                        break;
                    case WeaponStaff: hitCoefficient = coefficient.Staff; break;
                }
            }
        }

        if (user.Items[GameConstants.SlotLeftHand].Num != 0 && hitCoefficient == 0.0)
        {
            Item? leftWeapon = world.ItemTable.GetValueOrDefault(user.Items[GameConstants.SlotLeftHand].Num);
            if (leftWeapon is not null)
            {
                switch (leftWeapon.Kind / 10)
                {
                    case WeaponBow:
                    case WeaponLongBow:
                    case WeaponLauncher:
                        hitCoefficient = coefficient.Bow;
                        haveBow = true;
                        weapon = leftWeapon;
                        break;
                }
            }
        }

        int tempStr = user.Str + StrAmount + ItemStr;
        int tempDex = user.Dex + DexAmount + ItemDex;

        BodyAc = user.Level;
        MaxWeight = (user.Str + ItemStr) * 50;

        if (haveBow)
        {
            TotalHit = (short)((0.005 * weapon!.Damage * (tempDex + 40))
                + (hitCoefficient * weapon.Damage * user.Level * tempDex) + 3);
        }
        else
        {
            TotalHit = (short)((0.005f * ItemHit * (tempStr + 40))
                + (hitCoefficient * ItemHit * user.Level * tempStr) + 3);
        }

        TotalAc = (short)(coefficient.Armor * (BodyAc + ItemAc));
        TotalHitRate = (float)((1 + coefficient.HitRate * user.Level * tempDex) * ItemHitrate / 100)
            * (HitRateAmount / 100);
        TotalEvasionRate = (float)((1 + coefficient.EvasionRate * user.Level * tempDex) * ItemEvasionrate / 100)
            * (AvoidRateAmount / 100);

        SetMaxHp();
        SetMaxMp();
    }

    /// <summary>CUser::SetMaxHp (0 default, 1 refill, 2 snow-battle cap).</summary>
    public void SetMaxHp(int flag = 0)
    {
        if (UserData is not { } user)
            return;

        Coefficient? coefficient = world.CoefficientTable.GetValueOrDefault(user.Class);
        if (coefficient is null)
            return;

        int tempSta = user.Sta + ItemSta + StaAmount;

        if (user.Zone == ZoneSnowBattle && flag == 0)
        {
            MaxHp = 100;
        }
        else
        {
            MaxHp = (short)((coefficient.HitPoint * user.Level * user.Level * tempSta)
                + (0.1 * user.Level * tempSta) + (tempSta / 5) + MaxHpAmount + ItemMaxHp);

            if (flag == 1)
                user.Hp = (short)(MaxHp + 20); // slight overshoot so HpChange corrects it
            else if (flag == 2)
                MaxHp = 100;
        }

        if (MaxHp < user.Hp)
        {
            user.Hp = MaxHp;
            HpChange(user.Hp);
        }

        if (user.Hp < 5)
            user.Hp = 5;
    }

    /// <summary>CUser::SetMaxMp.</summary>
    public void SetMaxMp()
    {
        if (UserData is not { } user)
            return;

        Coefficient? coefficient = world.CoefficientTable.GetValueOrDefault(user.Class);
        if (coefficient is null)
            return;

        int tempIntel = user.Intel + ItemIntel + IntelAmount + 30;
        int tempSta = user.Sta + ItemSta + StaAmount;

        if (coefficient.ManaPoint != 0)
        {
            MaxMp = (short)((coefficient.ManaPoint * user.Level * user.Level * tempIntel)
                + (0.1f * user.Level * 2 * tempIntel) + (tempIntel / 5));
            MaxMp += ItemMaxMp;
            MaxMp += 20;
        }
        else if (coefficient.Sp != 0)
        {
            MaxMp = (short)((coefficient.Sp * user.Level * user.Level * tempSta)
                + (0.1f * user.Level * tempSta) + (tempSta / 5));
            MaxMp += ItemMaxMp;
        }

        if (MaxMp < user.Mp)
        {
            user.Mp = MaxMp;
            MSpChange(user.Mp);
        }
    }

    /// <summary>CUser::SetDetailData — everything the DB does not carry.</summary>
    public void SetDetailData()
    {
        if (UserData is not { } user)
            return;

        SetSlotItemValue();
        SetUserAbility();

        if (user.Level > MaxLevel)
        {
            logger.LogError("SetDetailData: user exceeds max level [accountId={Account} charId={CharId} level={Level}]",
                user.AccountId, user.CharId, user.Level);
            Close?.Invoke();
            return;
        }

        MaxExp = world.LevelUpTable.GetValueOrDefault(user.Level);
        MaxWeight = (user.Str + ItemStr) * 50;

        int zoneIndex = world.GetZoneIndex(user.Zone);
        ZoneIndex = (short)zoneIndex;

        // A zone this server does not host.
        if (zoneIndex == -1)
            Close?.Invoke();

        WillX = user.CurX;
        WillZ = user.CurZ;
        WillY = user.CurY;

        RegionX = (short)(user.CurX / 48); // VIEW_DISTANCE
        RegionZ = (short)(user.CurZ / 48);
    }

    /// <summary>CUser::HpChange — clamps, notifies the client/AI server/party.</summary>
    public void HpChange(int amount, int type = 0, bool attack = false)
    {
        if (UserData is not { } user)
            return;

        user.Hp = (short)(user.Hp + amount);
        if (user.Hp < 0)
            user.Hp = 0;
        else if (user.Hp > MaxHp)
            user.Hp = MaxHp;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_HP_CHANGE);
        writer.SetShort(MaxHp);
        writer.SetShort(user.Hp);
        Send(writer.Written);

        if (type == 0)
        {
            var aiBuffer = new byte[8];
            var aiWriter = new PacketWriter(aiBuffer);
            aiWriter.SetByte(AiOpcode.AG_USER_SET_HP);
            aiWriter.SetShort(SocketId);
            aiWriter.SetDWord((uint)user.Hp);
            world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
        }

        // Party broadcast attaches with the stage-4 party slice (PartyIndex is
        // still always -1 here).

        // Death by direct hits sends no dead packet from here.
        if (user.Hp == 0 && !attack)
            Dead();
    }

    /// <summary>CUser::MSpChange.</summary>
    public void MSpChange(int amount)
    {
        if (UserData is not { } user)
            return;

        user.Mp = (short)(user.Mp + amount);
        if (user.Mp < 0)
            user.Mp = 0;
        else if (user.Mp > MaxMp)
            user.Mp = MaxMp;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MSP_CHANGE);
        writer.SetShort(MaxMp);
        writer.SetShort(user.Mp);
        Send(writer.Written);

        // Party broadcast attaches with the stage-4 party slice.
    }

    /// <summary>CUser::Dead — ported with the stage-4 combat slice.</summary>
    public void Dead()
    {
        logger.LogDebug("Dead: not yet ported [charId={CharId}]", UserData?.CharId);
    }

    /// <summary>CUser::GetCurrentWeightForClient.</summary>
    public short GetCurrentWeightForClient() => (short)Math.Min(ItemWeight, short.MaxValue);

    /// <summary>CUser::GetMaxWeightForClient.</summary>
    public short GetMaxWeightForClient() => (short)Math.Min(MaxWeight, short.MaxValue);
}
