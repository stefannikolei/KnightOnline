namespace OpenKO.Data.Models;

/// <summary>K_NPC table (guards &amp; NPCs).</summary>
public sealed record Npc
{
    /// <summary>Column [sSid].</summary>
    public required short NpcId { get; init; }

    /// <summary>Column [strName].</summary>
    public required string Name { get; init; }

    /// <summary>Column [sPid].</summary>
    public required short PictureId { get; init; }

    /// <summary>Column [sSize].</summary>
    public required short Size { get; init; }

    /// <summary>Column [iWeapon1].</summary>
    public required int Weapon1 { get; init; }

    /// <summary>Column [iWeapon2].</summary>
    public required int Weapon2 { get; init; }

    /// <summary>Column [byGroup].</summary>
    public required byte Group { get; init; }

    /// <summary>Column [byActType].</summary>
    public required byte ActType { get; init; }

    /// <summary>Column [byType].</summary>
    public required byte Type { get; init; }

    /// <summary>Column [byFamily].</summary>
    public required byte Family { get; init; }

    /// <summary>Column [byRank].</summary>
    public required byte Rank { get; init; }

    /// <summary>Column [byTitle].</summary>
    public required byte Title { get; init; }

    /// <summary>Column [iSellingGroup].</summary>
    public required int SellingGroup { get; init; }

    /// <summary>Column [sLevel].</summary>
    public required short Level { get; init; }

    /// <summary>Column [iExp].</summary>
    public required int Exp { get; init; }

    /// <summary>Column [iLoyalty].</summary>
    public required int Loyalty { get; init; }

    /// <summary>Column [iHpPoint].</summary>
    public required int HitPoints { get; init; }

    /// <summary>Column [sMpPoint].</summary>
    public required short ManaPoints { get; init; }

    /// <summary>Column [sAtk].</summary>
    public required short Attack { get; init; }

    /// <summary>Column [sAc].</summary>
    public required short Armor { get; init; }

    /// <summary>Column [sHitRate].</summary>
    public required short HitRate { get; init; }

    /// <summary>Column [sEvadeRate].</summary>
    public required short EvadeRate { get; init; }

    /// <summary>Column [sDamage].</summary>
    public required short Damage { get; init; }

    /// <summary>Column [sAttackDelay].</summary>
    public required short AttackDelay { get; init; }

    /// <summary>Column [bySpeed1].</summary>
    public required byte WalkSpeed { get; init; }

    /// <summary>Column [bySpeed2].</summary>
    public required byte RunSpeed { get; init; }

    /// <summary>Column [sStandtime].</summary>
    public required short StandTime { get; init; }

    /// <summary>Column [iMagic1].</summary>
    public required int Magic1 { get; init; }

    /// <summary>Column [iMagic2].</summary>
    public required int Magic2 { get; init; }

    /// <summary>Column [iMagic3].</summary>
    public required int Magic3 { get; init; }

    /// <summary>Column [sFireR].</summary>
    public required short FireResist { get; init; }

    /// <summary>Column [sColdR].</summary>
    public required short ColdResist { get; init; }

    /// <summary>Column [sLightningR].</summary>
    public required short LightningResist { get; init; }

    /// <summary>Column [sMagicR].</summary>
    public required short MagicResist { get; init; }

    /// <summary>Column [sDiseaseR].</summary>
    public required short DiseaseResist { get; init; }

    /// <summary>Column [sPoisonR].</summary>
    public required short PoisonResist { get; init; }

    /// <summary>Column [sLightR].</summary>
    public required short LightResist { get; init; }

    /// <summary>Column [sBulk].</summary>
    public required short Bulk { get; init; }

    /// <summary>Column [byAttackRange].</summary>
    public required byte AttackRange { get; init; }

    /// <summary>Column [bySearchRange].</summary>
    public required byte SearchRange { get; init; }

    /// <summary>Column [byTracingRange].</summary>
    public required byte TracingRange { get; init; }

    /// <summary>Column [iMoney].</summary>
    public required int Money { get; init; }

    /// <summary>Column [sItem]: K_MONSTER_ITEM.sIndex drop table reference.</summary>
    public required short Item { get; init; }

    /// <summary>Column [byDirectAttack].</summary>
    public required byte DirectAttack { get; init; }

    /// <summary>Column [byMagicAttack].</summary>
    public required byte MagicAttack { get; init; }

    /// <summary>Column [byMoneyType].</summary>
    public required byte MoneyType { get; init; }
}

/// <summary>K_MONSTER table. Identical column layout to <see cref="Npc"/> (K_NPC).</summary>
public sealed record Monster
{
    /// <summary>Column [sSid].</summary>
    public required short MonsterId { get; init; }

    /// <summary>Column [strName].</summary>
    public required string Name { get; init; }

    /// <summary>Column [sPid].</summary>
    public required short PictureId { get; init; }

    /// <summary>Column [sSize].</summary>
    public required short Size { get; init; }

    /// <summary>Column [iWeapon1].</summary>
    public required int Weapon1 { get; init; }

    /// <summary>Column [iWeapon2].</summary>
    public required int Weapon2 { get; init; }

    /// <summary>Column [byGroup].</summary>
    public required byte Group { get; init; }

    /// <summary>Column [byActType].</summary>
    public required byte ActType { get; init; }

    /// <summary>Column [byType].</summary>
    public required byte Type { get; init; }

    /// <summary>Column [byFamily].</summary>
    public required byte Family { get; init; }

    /// <summary>Column [byRank].</summary>
    public required byte Rank { get; init; }

    /// <summary>Column [byTitle].</summary>
    public required byte Title { get; init; }

    /// <summary>Column [iSellingGroup].</summary>
    public required int SellingGroup { get; init; }

    /// <summary>Column [sLevel].</summary>
    public required short Level { get; init; }

    /// <summary>Column [iExp].</summary>
    public required int Exp { get; init; }

    /// <summary>Column [iLoyalty].</summary>
    public required int Loyalty { get; init; }

    /// <summary>Column [iHpPoint].</summary>
    public required int HitPoints { get; init; }

    /// <summary>Column [sMpPoint].</summary>
    public required short ManaPoints { get; init; }

    /// <summary>Column [sAtk].</summary>
    public required short Attack { get; init; }

    /// <summary>Column [sAc].</summary>
    public required short Armor { get; init; }

    /// <summary>Column [sHitRate].</summary>
    public required short HitRate { get; init; }

    /// <summary>Column [sEvadeRate].</summary>
    public required short EvadeRate { get; init; }

    /// <summary>Column [sDamage].</summary>
    public required short Damage { get; init; }

    /// <summary>Column [sAttackDelay].</summary>
    public required short AttackDelay { get; init; }

    /// <summary>Column [bySpeed1].</summary>
    public required byte WalkSpeed { get; init; }

    /// <summary>Column [bySpeed2].</summary>
    public required byte RunSpeed { get; init; }

    /// <summary>Column [sStandtime].</summary>
    public required short StandTime { get; init; }

    /// <summary>Column [iMagic1].</summary>
    public required int Magic1 { get; init; }

    /// <summary>Column [iMagic2].</summary>
    public required int Magic2 { get; init; }

    /// <summary>Column [iMagic3].</summary>
    public required int Magic3 { get; init; }

    /// <summary>Column [sFireR].</summary>
    public required short FireResist { get; init; }

    /// <summary>Column [sColdR].</summary>
    public required short ColdResist { get; init; }

    /// <summary>Column [sLightningR].</summary>
    public required short LightningResist { get; init; }

    /// <summary>Column [sMagicR].</summary>
    public required short MagicResist { get; init; }

    /// <summary>Column [sDiseaseR].</summary>
    public required short DiseaseResist { get; init; }

    /// <summary>Column [sPoisonR].</summary>
    public required short PoisonResist { get; init; }

    /// <summary>Column [sLightR].</summary>
    public required short LightResist { get; init; }

    /// <summary>Column [sBulk].</summary>
    public required short Bulk { get; init; }

    /// <summary>Column [byAttackRange].</summary>
    public required byte AttackRange { get; init; }

    /// <summary>Column [bySearchRange].</summary>
    public required byte SearchRange { get; init; }

    /// <summary>Column [byTracingRange].</summary>
    public required byte TracingRange { get; init; }

    /// <summary>Column [iMoney].</summary>
    public required int Money { get; init; }

    /// <summary>Column [sItem]: K_MONSTER_ITEM.sIndex drop table reference.</summary>
    public required short Item { get; init; }

    /// <summary>Column [byDirectAttack].</summary>
    public required byte DirectAttack { get; init; }

    /// <summary>Column [byMagicAttack].</summary>
    public required byte MagicAttack { get; init; }

    /// <summary>Column [byMoneyType].</summary>
    public required byte MoneyType { get; init; }
}

/// <summary>K_MONSTER_ITEM table (monster loot table).</summary>
public sealed record MonsterItem
{
    /// <summary>Column [sIndex]: monster identifier (K_MONSTER.sSid).</summary>
    public required short MonsterId { get; init; }

    /// <summary>Columns [iItem01]..[iItem05].</summary>
    public required int[] ItemId { get; init; }

    /// <summary>Columns [sPersent01]..[sPersent05].</summary>
    public required short[] DropChance { get; init; }
}

/// <summary>K_NPCPOS table (NPC spawn positions).</summary>
public sealed record NpcPos
{
    /// <summary>Column [ZoneID].</summary>
    public required short ZoneId { get; init; }

    /// <summary>Column [NpcID]: NPC identifier (K_NPC.sSid / K_MONSTER.sSid).</summary>
    public required int NpcId { get; init; }

    public required byte ActType { get; init; }

    public required byte RegenType { get; init; }

    public required byte DungeonFamily { get; init; }

    public required byte SpecialType { get; init; }

    public required byte TrapNumber { get; init; }

    public required int LeftX { get; init; }

    public required int TopZ { get; init; }

    public required int RightX { get; init; }

    public required int BottomZ { get; init; }

    public required int LimitMinZ { get; init; }

    public required int LimitMinX { get; init; }

    public required int LimitMaxX { get; init; }

    public required int LimitMaxZ { get; init; }

    /// <summary>Column [NumNPC].</summary>
    public required byte NumNpc { get; init; }

    /// <summary>Column [RegTime].</summary>
    public required short RespawnTime { get; init; }

    /// <summary>Column [byDirection].</summary>
    public required int Direction { get; init; }

    /// <summary>Column [DotCnt]: number of points contained within <see cref="Path"/>.</summary>
    public required byte PathPointCount { get; init; }

    /// <summary>Column [path]: zero-padded 4-digit x/z coordinate pairs.</summary>
    public required string Path { get; init; }
}

/// <summary>MAGIC table (magic and ability configuration).</summary>
public sealed record Magic
{
    /// <summary>Column [MagicNum].</summary>
    public required int ID { get; init; }

    public required byte BeforeAction { get; init; }

    public required byte TargetAction { get; init; }

    public required byte SelfEffect { get; init; }

    public required byte FlyingEffect { get; init; }

    public required short TargetEffect { get; init; }

    public required byte Moral { get; init; }

    public required short SkillLevel { get; init; }

    public required short Skill { get; init; }

    /// <summary>Column [Msp].</summary>
    public required short ManaCost { get; init; }

    /// <summary>Column [HP].</summary>
    public required short HpCost { get; init; }

    public required byte ItemGroup { get; init; }

    public required int UseItem { get; init; }

    public required byte CastTime { get; init; }

    /// <summary>Column [ReCastTime].</summary>
    public required byte RecastTime { get; init; }

    public required byte SuccessRate { get; init; }

    public required byte Type1 { get; init; }

    public required byte Type2 { get; init; }

    public required short Range { get; init; }

    public required byte Etc { get; init; }

    public required int Event { get; init; }
}

/// <summary>MAGIC_TYPE1 table (melee abilities).</summary>
public sealed record MagicType1
{
    /// <summary>Column [iNum].</summary>
    public required int ID { get; init; }

    public required byte Type { get; init; }

    /// <summary>Column [HitRate].</summary>
    public required short HitRateMod { get; init; }

    /// <summary>Column [Hit].</summary>
    public required short DamageMod { get; init; }

    public required short AddDamage { get; init; }

    public required byte Delay { get; init; }

    public required byte ComboType { get; init; }

    public required byte ComboCount { get; init; }

    public required short ComboDamage { get; init; }

    public required short Range { get; init; }
}

/// <summary>MAGIC_TYPE2 table (bow abilities).</summary>
public sealed record MagicType2
{
    /// <summary>Column [iNum].</summary>
    public required int ID { get; init; }

    public required byte HitType { get; init; }

    /// <summary>Column [HitRate].</summary>
    public required short HitRateMod { get; init; }

    /// <summary>Column [AddDamage].</summary>
    public required short DamageMod { get; init; }

    /// <summary>Column [AddRange].</summary>
    public required short RangeMod { get; init; }

    public required byte NeedArrow { get; init; }

    public required short AddDamagePlus { get; init; }
}

/// <summary>MAGIC_TYPE3 table (area of effect / damage over time effects).</summary>
public sealed record MagicType3
{
    /// <summary>Column [iNum].</summary>
    public required int ID { get; init; }

    public required byte Radius { get; init; }

    public required short Angle { get; init; }

    public required byte DirectType { get; init; }

    public required short FirstDamage { get; init; }

    public required short EndDamage { get; init; }

    public required short TimeDamage { get; init; }

    public required byte Duration { get; init; }

    public required byte Attribute { get; init; }
}

/// <summary>MAGIC_TYPE4 table (stat modification skills).</summary>
public sealed record MagicType4
{
    /// <summary>Column [iNum].</summary>
    public required int ID { get; init; }

    public required byte BuffType { get; init; }

    public required byte Radius { get; init; }

    public required short Duration { get; init; }

    public required byte AttackSpeed { get; init; }

    public required byte Speed { get; init; }

    /// <summary>Column [AC].</summary>
    public required short Armor { get; init; }

    /// <summary>Column [ACPct].</summary>
    public required short ArmorPercent { get; init; }

    /// <summary>Column [Attack].</summary>
    public required byte AttackPower { get; init; }

    /// <summary>Column [MagicAttack].</summary>
    public required byte MagicPower { get; init; }

    /// <summary>Column [MaxHP].</summary>
    public required short MaxHp { get; init; }

    /// <summary>Column [MaxHpPct].</summary>
    public required short MaxHpPercent { get; init; }

    /// <summary>Column [MaxMP].</summary>
    public required short MaxMp { get; init; }

    /// <summary>Column [MaxMpPct].</summary>
    public required short MaxMpPercent { get; init; }

    public required byte HitRate { get; init; }

    public required short AvoidRate { get; init; }

    /// <summary>Column [Str].</summary>
    public required short Strength { get; init; }

    /// <summary>Column [Sta].</summary>
    public required short Stamina { get; init; }

    /// <summary>Column [Dex].</summary>
    public required short Dexterity { get; init; }

    /// <summary>Column [Intel].</summary>
    public required short Intelligence { get; init; }

    /// <summary>Column [Cha].</summary>
    public required short Charisma { get; init; }

    /// <summary>Column [FireR].</summary>
    public required byte FireResist { get; init; }

    /// <summary>Column [ColdR].</summary>
    public required byte ColdResist { get; init; }

    /// <summary>Column [LightningR].</summary>
    public required byte LightningResist { get; init; }

    /// <summary>Column [MagicR].</summary>
    public required byte MagicResist { get; init; }

    /// <summary>Column [DiseaseR].</summary>
    public required byte DiseaseResist { get; init; }

    /// <summary>Column [PoisonR].</summary>
    public required byte PoisonResist { get; init; }

    /// <summary>Column [ExpPct].</summary>
    public required byte ExpPercent { get; init; }
}

/// <summary>MAGIC_TYPE7 table (targeting modifications).</summary>
public sealed record MagicType7
{
    /// <summary>Column [nIndex].</summary>
    public required int ID { get; init; }

    /// <summary>Column [byValidGroup].</summary>
    public required byte ValidGroup { get; init; }

    /// <summary>Column [byNatoinChange].</summary>
    public required byte NationChange { get; init; }

    /// <summary>Column [shMonsterNum].</summary>
    public required short MonsterNumber { get; init; }

    /// <summary>Column [byTargetChange].</summary>
    public required byte TargetChange { get; init; }

    /// <summary>Column [byStateChange].</summary>
    public required byte StateChange { get; init; }

    /// <summary>Column [byRadius].</summary>
    public required byte Radius { get; init; }

    /// <summary>Column [shHitrate].</summary>
    public required short HitRate { get; init; }

    /// <summary>Column [shDuration].</summary>
    public required short Duration { get; init; }

    /// <summary>Column [shDamage].</summary>
    public required short Damage { get; init; }

    /// <summary>Column [byVisoin].</summary>
    public required byte Vision { get; init; }

    /// <summary>Column [nNeedItem].</summary>
    public required int NeedItem { get; init; }
}

/// <summary>MAKE_ITEM_GROUP table.</summary>
public sealed record MakeItemGroup
{
    /// <summary>Column [iItemGroupNum].</summary>
    public required int ItemGroupNumber { get; init; }

    /// <summary>Columns [iItem_1]..[iItem_30].</summary>
    public required int[] Item { get; init; }
}

/// <summary>MAKE_WEAPON table.</summary>
public sealed record MakeWeapon
{
    /// <summary>Column [byLevel].</summary>
    public required byte Level { get; init; }

    /// <summary>Columns [sClass_1]..[sClass_12].</summary>
    public required short[] Class { get; init; }
}

/// <summary>MAKE_DEFENSIVE table.</summary>
public sealed record MakeDefensive
{
    /// <summary>Column [byLevel].</summary>
    public required byte Level { get; init; }

    /// <summary>Column [sClass_1].</summary>
    public required short Class1 { get; init; }

    /// <summary>Column [sClass_2].</summary>
    public required short Class2 { get; init; }

    /// <summary>Column [sClass_3].</summary>
    public required short Class3 { get; init; }

    /// <summary>Column [sClass_4].</summary>
    public required short Class4 { get; init; }

    /// <summary>Column [sClass_5].</summary>
    public required short Class5 { get; init; }

    /// <summary>Column [sClass_6].</summary>
    public required short Class6 { get; init; }

    /// <summary>Column [sClass_7].</summary>
    public required short Class7 { get; init; }
}

/// <summary>MAKE_ITEM_GRADECODE table.</summary>
public sealed record MakeItemGradeCode
{
    /// <summary>Column [byItemIndex].</summary>
    public required byte ItemIndex { get; init; }

    /// <summary>Columns [byGrade_1]..[byGrade_9].</summary>
    public required short[] Grade { get; init; }
}

/// <summary>MAKE_ITEM_LARECODE table (make item rarity codes).</summary>
public sealed record MakeItemRareCode
{
    /// <summary>Column [byLevelGrade].</summary>
    public required byte LevelGrade { get; init; }

    /// <summary>Column [sUpgradeItem].</summary>
    public required short UpgradeItem { get; init; }

    /// <summary>Column [sLareItem].</summary>
    public required short RareItem { get; init; }

    /// <summary>Column [sMagicItem].</summary>
    public required short MagicItem { get; init; }

    /// <summary>Column [sGereralItem].</summary>
    public required short GeneralItem { get; init; }
}

/// <summary>ZONE_INFO table (zone/map information).</summary>
public sealed record ZoneInfo
{
    /// <summary>Column [ServerNo].</summary>
    public required byte ServerId { get; init; }

    /// <summary>Column [ZoneNo].</summary>
    public required short ZoneId { get; init; }

    /// <summary>Column [strZoneName].</summary>
    public required string Name { get; init; }

    public required int InitX { get; init; }

    public required int InitZ { get; init; }

    public required int InitY { get; init; }

    public required byte Type { get; init; }

    public required byte RoomEvent { get; init; }
}
