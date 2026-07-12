namespace OpenKO.Data.Models;

/// <summary>
/// COEFFICIENT table (deps/db-models Full/model): per-class weapon and stat
/// coefficients used by CUser::SetUserAbility and validated on character creation.
/// </summary>
public sealed record Coefficient
{
    /// <summary>Column [sClass].</summary>
    public required short ClassId { get; init; }

    public required double ShortSword { get; init; }

    public required double Sword { get; init; }

    public required double Axe { get; init; }

    public required double Club { get; init; }

    public required double Spear { get; init; }

    public required double Pole { get; init; }

    public required double Staff { get; init; }

    public required double Bow { get; init; }

    /// <summary>Column [Hp].</summary>
    public required double HitPoint { get; init; }

    /// <summary>Column [Mp].</summary>
    public required double ManaPoint { get; init; }

    public required double Sp { get; init; }

    /// <summary>Column [Ac].</summary>
    public required double Armor { get; init; }

    /// <summary>Column [Hitrate].</summary>
    public required double HitRate { get; init; }

    /// <summary>Column [Evasionrate].</summary>
    public required double EvasionRate { get; init; }
}

/// <summary>ITEM table (deps/db-models Full/model Item).</summary>
public sealed record Item
{
    /// <summary>Column [Num].</summary>
    public required int ID { get; init; }

    /// <summary>Column [strName].</summary>
    public required string Name { get; init; }

    public required byte Kind { get; init; }

    public required byte Slot { get; init; }

    public required byte Race { get; init; }

    /// <summary>Column [Class].</summary>
    public required byte ClassId { get; init; }

    public required short Damage { get; init; }

    public required short Delay { get; init; }

    public required short Range { get; init; }

    public required short Weight { get; init; }

    /// <summary>Column [Duration].</summary>
    public required short Durability { get; init; }

    public required int BuyPrice { get; init; }

    public required int SellPrice { get; init; }

    /// <summary>Column [Ac].</summary>
    public required short Armor { get; init; }

    public required byte Countable { get; init; }

    /// <summary>Column [Effect1].</summary>
    public required int MagicEffect { get; init; }

    /// <summary>Column [Effect2].</summary>
    public required int SpecialEffect { get; init; }

    /// <summary>Column [ReqLevel].</summary>
    public required byte MinLevel { get; init; }

    /// <summary>Column [ReqLevelMax].</summary>
    public required byte MaxLevel { get; init; }

    /// <summary>Column [ReqRank].</summary>
    public required byte RequiredRank { get; init; }

    /// <summary>Column [ReqTitle].</summary>
    public required byte RequiredTitle { get; init; }

    /// <summary>Column [ReqStr].</summary>
    public required byte RequiredStrength { get; init; }

    /// <summary>Column [ReqSta].</summary>
    public required byte RequiredStamina { get; init; }

    /// <summary>Column [ReqDex].</summary>
    public required byte RequiredDexterity { get; init; }

    /// <summary>Column [ReqIntel].</summary>
    public required byte RequiredIntelligence { get; init; }

    /// <summary>Column [ReqCha].</summary>
    public required byte RequiredCharisma { get; init; }

    public required byte SellingGroup { get; init; }

    /// <summary>Column [ItemType].</summary>
    public required byte Type { get; init; }

    /// <summary>Column [Hitrate].</summary>
    public required short HitRate { get; init; }

    /// <summary>Column [Evasionrate].</summary>
    public required short EvasionRate { get; init; }

    /// <summary>Column [DaggerAc].</summary>
    public required short DaggerArmor { get; init; }

    /// <summary>Column [SwordAc].</summary>
    public required short SwordArmor { get; init; }

    /// <summary>Column [MaceAc].</summary>
    public required short MaceArmor { get; init; }

    /// <summary>Column [AxeAc].</summary>
    public required short AxeArmor { get; init; }

    /// <summary>Column [SpearAc].</summary>
    public required short SpearArmor { get; init; }

    /// <summary>Column [BowAc].</summary>
    public required short BowArmor { get; init; }

    public required byte FireDamage { get; init; }

    public required byte IceDamage { get; init; }

    public required byte LightningDamage { get; init; }

    public required byte PoisonDamage { get; init; }

    /// <summary>Column [HPDrain].</summary>
    public required byte HpDrain { get; init; }

    /// <summary>Column [MPDamage].</summary>
    public required byte MpDamage { get; init; }

    /// <summary>Column [MPDrain].</summary>
    public required byte MpDrain { get; init; }

    public required byte MirrorDamage { get; init; }

    /// <summary>Column [Droprate].</summary>
    public required byte DropRate { get; init; }

    /// <summary>Column [StrB].</summary>
    public required short StrengthBonus { get; init; }

    /// <summary>Column [StaB].</summary>
    public required short StaminaBonus { get; init; }

    /// <summary>Column [DexB].</summary>
    public required short DexterityBonus { get; init; }

    /// <summary>Column [IntelB].</summary>
    public required short IntelligenceBonus { get; init; }

    /// <summary>Column [ChaB].</summary>
    public required short CharismaBonus { get; init; }

    /// <summary>Column [MaxHpB].</summary>
    public required short MaxHpBonus { get; init; }

    /// <summary>Column [MaxMpB].</summary>
    public required short MaxMpBonus { get; init; }

    /// <summary>Column [FireR].</summary>
    public required short FireResist { get; init; }

    /// <summary>Column [ColdR].</summary>
    public required short ColdResist { get; init; }

    /// <summary>Column [LightningR].</summary>
    public required short LightningResist { get; init; }

    /// <summary>Column [MagicR].</summary>
    public required short MagicResist { get; init; }

    /// <summary>Column [PoisonR].</summary>
    public required short PoisonResist { get; init; }

    /// <summary>Column [CurseR].</summary>
    public required short CurseResist { get; init; }
}

/// <summary>LEVEL_UP table.</summary>
public sealed record LevelUp
{
    /// <summary>Column [level].</summary>
    public required byte Level { get; init; }

    /// <summary>Column [Exp].</summary>
    public required int RequiredExp { get; init; }
}

/// <summary>HOME table: respawn rectangles per nation.</summary>
public sealed record Home
{
    public required byte Nation { get; init; }

    public required int ElmoZoneX { get; init; }
    public required int ElmoZoneZ { get; init; }
    public required byte ElmoZoneLX { get; init; }
    public required byte ElmoZoneLZ { get; init; }

    public required int KarusZoneX { get; init; }
    public required int KarusZoneZ { get; init; }
    public required byte KarusZoneLX { get; init; }
    public required byte KarusZoneLZ { get; init; }

    public required int FreeZoneX { get; init; }
    public required int FreeZoneZ { get; init; }
    public required byte FreeZoneLX { get; init; }
    public required byte FreeZoneLZ { get; init; }

    public required int BattleZoneX { get; init; }
    public required int BattleZoneZ { get; init; }
    public required byte BattleZoneLX { get; init; }
    public required byte BattleZoneLZ { get; init; }

    public required int BattleZone2X { get; init; }
    public required int BattleZone2Z { get; init; }
    public required byte BattleZone2LX { get; init; }
    public required byte BattleZone2LZ { get; init; }
}

/// <summary>MAGIC_TYPE5 table (cure/resurrection support skills).</summary>
public sealed record MagicType5
{
    /// <summary>Column [iNum].</summary>
    public required int ID { get; init; }

    /// <summary>1 cure disease, 2 cure curse, 3 resurrection, 4 self resurrection, 5 remove bless.</summary>
    public required byte Type { get; init; }

    /// <summary>Column [ExpRecover]: percent of experience loss recovered.</summary>
    public required byte ExpRecover { get; init; }

    /// <summary>Column [NeedStone]: resurrection stones required.</summary>
    public required short NeedStone { get; init; }
}

/// <summary>MAGIC_TYPE8 table (warp, resurrection and summon spells).</summary>
public sealed record MagicType8
{
    /// <summary>Column [iNum].</summary>
    public required int ID { get; init; }

    public required byte Target { get; init; }

    public required short Radius { get; init; }

    /// <summary>1 gate warp, 11 resurrect, 12 summon in zone, 13 summon across zones, 20 random teleport.</summary>
    public required byte WarpType { get; init; }

    /// <summary>Column [ExpRecover].</summary>
    public required short ExpRecover { get; init; }
}

/// <summary>SERVER_RESOURCE table: sprintf-style message templates.</summary>
public sealed record ServerResource
{
    /// <summary>Column [nResourceID].</summary>
    public required int ResourceId { get; init; }

    /// <summary>Column [strResource].</summary>
    public required string Resource { get; init; }
}

/// <summary>START_POSITION table: per-zone WIZ_HOME spawn boxes.</summary>
public sealed record StartPosition
{
    /// <summary>Column [ZoneID].</summary>
    public required short ZoneId { get; init; }

    /// <summary>Column [sKarusX].</summary>
    public required short KarusX { get; init; }

    /// <summary>Column [sKarusZ].</summary>
    public required short KarusZ { get; init; }

    /// <summary>Column [sElmoradX].</summary>
    public required short ElmoX { get; init; }

    /// <summary>Column [sElmoradZ].</summary>
    public required short ElmoZ { get; init; }

    /// <summary>Column [bRangeX].</summary>
    public required byte RangeX { get; init; }

    /// <summary>Column [bRangeZ].</summary>
    public required byte RangeZ { get; init; }
}

/// <summary>KNIGHTS table row (Ebenezer startup cache).</summary>
public sealed record KnightsRow
{
    /// <summary>Column [IDNum].</summary>
    public required short Id { get; init; }

    /// <summary>Column [Flag]: 1 clan, 2 knights.</summary>
    public required byte Flag { get; init; }

    /// <summary>Column [Nation].</summary>
    public required byte Nation { get; init; }

    /// <summary>Column [Ranking].</summary>
    public required byte Ranking { get; init; }

    /// <summary>Column [IDName].</summary>
    public required string Name { get; init; }

    /// <summary>Column [Members].</summary>
    public required short Members { get; init; }

    /// <summary>Column [Chief].</summary>
    public required string Chief { get; init; }

    /// <summary>Column [ViceChief_1].</summary>
    public required string ViceChief1 { get; init; }

    /// <summary>Column [ViceChief_2].</summary>
    public required string ViceChief2 { get; init; }

    /// <summary>Column [ViceChief_3].</summary>
    public required string ViceChief3 { get; init; }

    /// <summary>Column [Gold].</summary>
    public required long Gold { get; init; }

    /// <summary>Column [Domination].</summary>
    public required short Domination { get; init; }

    /// <summary>Column [Points].</summary>
    public required int Points { get; init; }

    /// <summary>Column [sMarkVersion].</summary>
    public required short MarkVersion { get; init; }

    /// <summary>Column [sAllianceKnights].</summary>
    public required short AllianceKnights { get; init; }

    /// <summary>Column [sCape].</summary>
    public required short Cape { get; init; }
}

/// <summary>KNIGHTS_USER table row.</summary>
public sealed record KnightsUserRow
{
    /// <summary>Column [sIDNum].</summary>
    public required short KnightsId { get; init; }

    /// <summary>Column [strUserID].</summary>
    public required string UserId { get; init; }
}

/// <summary>EVENT_TRIGGER table row (NPC-type/trap → quest event).</summary>
public sealed record EventTriggerRow
{
    /// <summary>Column [bNpcType].</summary>
    public required byte NpcType { get; init; }

    /// <summary>Column [sNpcID].</summary>
    public required short NpcId { get; init; }

    /// <summary>Column [nTriggerNum].</summary>
    public required int TriggerNumber { get; init; }
}
