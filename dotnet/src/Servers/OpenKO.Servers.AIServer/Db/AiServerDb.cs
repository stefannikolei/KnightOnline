using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OpenKO.Data;
using OpenKO.Data.Models;

namespace OpenKO.Servers.AIServer.Db;

/// <summary>
/// Loads the GAME-DB tables the AIServer reads at startup (the C++
/// recordset_loader::STLMap/Vector loaders in AIServerApp), one SELECT per
/// table in the model's ordered-column order. Each loader returns null and
/// logs on SqlException, matching the C++ Load_ForbidEmpty error handling.
/// </summary>
public sealed class AiServerDb(SqlConnectionFactory connectionFactory, ILogger<AiServerDb> logger)
{
    private const string NpcColumns =
        "sSid, strName, sPid, sSize, iWeapon1, iWeapon2, byGroup, byActType, byType, byFamily, " +
        "byRank, byTitle, iSellingGroup, sLevel, iExp, iLoyalty, iHpPoint, sMpPoint, sAtk, sAc, " +
        "sHitRate, sEvadeRate, sDamage, sAttackDelay, bySpeed1, bySpeed2, sStandtime, iMagic1, iMagic2, iMagic3, " +
        "sFireR, sColdR, sLightningR, sMagicR, sDiseaseR, sPoisonR, sLightR, sBulk, byAttackRange, bySearchRange, " +
        "byTracingRange, iMoney, sItem, byDirectAttack, byMagicAttack, byMoneyType";

    public Task<List<Npc>?> LoadNpcTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync("K_NPC", NpcColumns, ReadNpc, cancellationToken);

    // The C++ (AIServerApp::GetMonsterTableData) loads K_MONSTER through the
    // Monster->Npc binder adapter INTO the same Npc-shaped map type as K_NPC
    // (NpcTableMap), because both tables share an identical column layout.
    // Here the raw Monster records are returned; converting them into the
    // shared Npc shape is left to the app layer.
    public Task<List<Monster>?> LoadMonsterTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync("K_MONSTER", NpcColumns, ReadMonster, cancellationToken);

    // The C++ (AIServerApp::GetNpcItemTable) loads these rows as a flat vector
    // and flattens them into an int matrix (MonsterId, ItemId/DropChance pairs).
    public Task<List<MonsterItem>?> LoadMonsterItemTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "K_MONSTER_ITEM",
            "sIndex, iItem01, sPersent01, iItem02, sPersent02, iItem03, sPersent03, iItem04, sPersent04, iItem05, sPersent05",
            static reader => new MonsterItem
            {
                MonsterId = reader.GetInt16(0),
                ItemId = [reader.GetInt32(1), reader.GetInt32(3), reader.GetInt32(5), reader.GetInt32(7), reader.GetInt32(9)],
                DropChance = [reader.GetInt16(2), reader.GetInt16(4), reader.GetInt16(6), reader.GetInt16(8), reader.GetInt16(10)],
            },
            cancellationToken);

    // The C++ loads K_NPCPOS as a flat vector (AIServerApp::LoadNpcPosTable)
    // and expands each row into NumNPC CNpc instances, looking the definition
    // up in the monster map (ActType < 100) or NPC map (ActType >= 100).
    public Task<List<NpcPos>?> LoadNpcPosTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "K_NPCPOS",
            "ZoneID, NpcID, ActType, RegenType, DungeonFamily, SpecialType, TrapNumber, LeftX, TopZ, RightX, " +
            "BottomZ, LimitMinZ, LimitMinX, LimitMaxX, LimitMaxZ, NumNPC, RegTime, byDirection, DotCnt, path",
            static reader => new NpcPos
            {
                ZoneId = reader.GetInt16(0),
                NpcId = reader.GetInt32(1),
                ActType = reader.GetByte(2),
                RegenType = reader.GetByte(3),
                DungeonFamily = reader.GetByte(4),
                SpecialType = reader.GetByte(5),
                TrapNumber = reader.GetByte(6),
                LeftX = reader.GetInt32(7),
                TopZ = reader.GetInt32(8),
                RightX = reader.GetInt32(9),
                BottomZ = reader.GetInt32(10),
                LimitMinZ = reader.GetInt32(11),
                LimitMinX = reader.GetInt32(12),
                LimitMaxX = reader.GetInt32(13),
                LimitMaxZ = reader.GetInt32(14),
                NumNpc = reader.GetByte(15),
                RespawnTime = reader.GetInt16(16),
                Direction = reader.GetInt32(17),
                PathPointCount = reader.GetByte(18),
                Path = GetString(reader, 19),
            },
            cancellationToken);

    public Task<List<Magic>?> LoadMagicTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAGIC",
            "MagicNum, BeforeAction, TargetAction, SelfEffect, FlyingEffect, TargetEffect, Moral, SkillLevel, Skill, Msp, " +
            "HP, ItemGroup, UseItem, CastTime, ReCastTime, SuccessRate, Type1, Type2, Range, Etc, Event",
            static reader => new Magic
            {
                ID = reader.GetInt32(0),
                BeforeAction = reader.GetByte(1),
                TargetAction = reader.GetByte(2),
                SelfEffect = reader.GetByte(3),
                FlyingEffect = reader.GetByte(4),
                TargetEffect = reader.GetInt16(5),
                Moral = reader.GetByte(6),
                SkillLevel = reader.GetInt16(7),
                Skill = reader.GetInt16(8),
                ManaCost = reader.GetInt16(9),
                HpCost = reader.GetInt16(10),
                ItemGroup = reader.GetByte(11),
                UseItem = reader.GetInt32(12),
                CastTime = reader.GetByte(13),
                RecastTime = reader.GetByte(14),
                SuccessRate = reader.GetByte(15),
                Type1 = reader.GetByte(16),
                Type2 = reader.GetByte(17),
                Range = reader.GetInt16(18),
                Etc = reader.GetByte(19),
                Event = reader.GetInt32(20),
            },
            cancellationToken);

    public Task<List<MagicType1>?> LoadMagicType1TableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAGIC_TYPE1",
            "iNum, Type, HitRate, Hit, AddDamage, Delay, ComboType, ComboCount, ComboDamage, Range",
            static reader => new MagicType1
            {
                ID = reader.GetInt32(0),
                Type = reader.GetByte(1),
                HitRateMod = reader.GetInt16(2),
                DamageMod = reader.GetInt16(3),
                AddDamage = reader.GetInt16(4),
                Delay = reader.GetByte(5),
                ComboType = reader.GetByte(6),
                ComboCount = reader.GetByte(7),
                ComboDamage = reader.GetInt16(8),
                Range = reader.GetInt16(9),
            },
            cancellationToken);

    public Task<List<MagicType2>?> LoadMagicType2TableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAGIC_TYPE2",
            "iNum, HitType, HitRate, AddDamage, AddRange, NeedArrow, AddDamagePlus",
            static reader => new MagicType2
            {
                ID = reader.GetInt32(0),
                HitType = reader.GetByte(1),
                HitRateMod = reader.GetInt16(2),
                DamageMod = reader.GetInt16(3),
                RangeMod = reader.GetInt16(4),
                NeedArrow = reader.GetByte(5),
                AddDamagePlus = reader.GetInt16(6),
            },
            cancellationToken);

    public Task<List<MagicType3>?> LoadMagicType3TableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAGIC_TYPE3",
            "iNum, Radius, Angle, DirectType, FirstDamage, EndDamage, TimeDamage, Duration, Attribute",
            static reader => new MagicType3
            {
                ID = reader.GetInt32(0),
                Radius = reader.GetByte(1),
                Angle = reader.GetInt16(2),
                DirectType = reader.GetByte(3),
                FirstDamage = reader.GetInt16(4),
                EndDamage = reader.GetInt16(5),
                TimeDamage = reader.GetInt16(6),
                Duration = reader.GetByte(7),
                Attribute = reader.GetByte(8),
            },
            cancellationToken);

    public Task<List<MagicType4>?> LoadMagicType4TableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAGIC_TYPE4",
            "iNum, BuffType, Radius, Duration, AttackSpeed, Speed, AC, ACPct, Attack, MagicAttack, " +
            "MaxHP, MaxHpPct, MaxMP, MaxMpPct, HitRate, AvoidRate, Str, Sta, Dex, Intel, " +
            "Cha, FireR, ColdR, LightningR, MagicR, DiseaseR, PoisonR, ExpPct",
            static reader => new MagicType4
            {
                ID = reader.GetInt32(0),
                BuffType = reader.GetByte(1),
                Radius = reader.GetByte(2),
                Duration = reader.GetInt16(3),
                AttackSpeed = reader.GetByte(4),
                Speed = reader.GetByte(5),
                Armor = reader.GetInt16(6),
                ArmorPercent = reader.GetInt16(7),
                AttackPower = reader.GetByte(8),
                MagicPower = reader.GetByte(9),
                MaxHp = reader.GetInt16(10),
                MaxHpPercent = reader.GetInt16(11),
                MaxMp = reader.GetInt16(12),
                MaxMpPercent = reader.GetInt16(13),
                HitRate = reader.GetByte(14),
                AvoidRate = reader.GetInt16(15),
                Strength = reader.GetInt16(16),
                Stamina = reader.GetInt16(17),
                Dexterity = reader.GetInt16(18),
                Intelligence = reader.GetInt16(19),
                Charisma = reader.GetInt16(20),
                FireResist = reader.GetByte(21),
                ColdResist = reader.GetByte(22),
                LightningResist = reader.GetByte(23),
                MagicResist = reader.GetByte(24),
                DiseaseResist = reader.GetByte(25),
                PoisonResist = reader.GetByte(26),
                ExpPercent = reader.GetByte(27),
            },
            cancellationToken);

    public Task<List<MagicType7>?> LoadMagicType7TableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAGIC_TYPE7",
            "nIndex, byValidGroup, byNatoinChange, shMonsterNum, byTargetChange, byStateChange, byRadius, shHitrate, shDuration, shDamage, " +
            "byVisoin, nNeedItem",
            static reader => new MagicType7
            {
                ID = reader.GetInt32(0),
                ValidGroup = reader.GetByte(1),
                NationChange = reader.GetByte(2),
                MonsterNumber = reader.GetInt16(3),
                TargetChange = reader.GetByte(4),
                StateChange = reader.GetByte(5),
                Radius = reader.GetByte(6),
                HitRate = reader.GetInt16(7),
                Duration = reader.GetInt16(8),
                Damage = reader.GetInt16(9),
                Vision = reader.GetByte(10),
                NeedItem = reader.GetInt32(11),
            },
            cancellationToken);

    public Task<List<MakeItemGroup>?> LoadMakeItemGroupTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAKE_ITEM_GROUP",
            "iItemGroupNum, iItem_1, iItem_2, iItem_3, iItem_4, iItem_5, iItem_6, iItem_7, iItem_8, iItem_9, " +
            "iItem_10, iItem_11, iItem_12, iItem_13, iItem_14, iItem_15, iItem_16, iItem_17, iItem_18, iItem_19, " +
            "iItem_20, iItem_21, iItem_22, iItem_23, iItem_24, iItem_25, iItem_26, iItem_27, iItem_28, iItem_29, iItem_30",
            static reader =>
            {
                var items = new int[30];
                for (int i = 0; i < items.Length; i++)
                    items[i] = reader.GetInt32(i + 1);

                return new MakeItemGroup
                {
                    ItemGroupNumber = reader.GetInt32(0),
                    Item = items,
                };
            },
            cancellationToken);

    public Task<List<MakeWeapon>?> LoadMakeWeaponTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAKE_WEAPON",
            "byLevel, sClass_1, sClass_2, sClass_3, sClass_4, sClass_5, sClass_6, sClass_7, sClass_8, sClass_9, " +
            "sClass_10, sClass_11, sClass_12",
            static reader =>
            {
                var classes = new short[12];
                for (int i = 0; i < classes.Length; i++)
                    classes[i] = reader.GetInt16(i + 1);

                return new MakeWeapon
                {
                    Level = reader.GetByte(0),
                    Class = classes,
                };
            },
            cancellationToken);

    public Task<List<MakeDefensive>?> LoadMakeDefensiveTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAKE_DEFENSIVE",
            "byLevel, sClass_1, sClass_2, sClass_3, sClass_4, sClass_5, sClass_6, sClass_7",
            static reader => new MakeDefensive
            {
                Level = reader.GetByte(0),
                Class1 = reader.GetInt16(1),
                Class2 = reader.GetInt16(2),
                Class3 = reader.GetInt16(3),
                Class4 = reader.GetInt16(4),
                Class5 = reader.GetInt16(5),
                Class6 = reader.GetInt16(6),
                Class7 = reader.GetInt16(7),
            },
            cancellationToken);

    public Task<List<MakeItemGradeCode>?> LoadMakeItemGradeCodeTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAKE_ITEM_GRADECODE",
            "byItemIndex, byGrade_1, byGrade_2, byGrade_3, byGrade_4, byGrade_5, byGrade_6, byGrade_7, byGrade_8, byGrade_9",
            static reader =>
            {
                // The byGrade_* columns bind as int16 in the C++ model despite
                // their "by" prefix; Convert tolerates tinyint/smallint schemas.
                var grades = new short[9];
                for (int i = 0; i < grades.Length; i++)
                    grades[i] = Convert.ToInt16(reader.GetValue(i + 1));

                return new MakeItemGradeCode
                {
                    ItemIndex = reader.GetByte(0),
                    Grade = grades,
                };
            },
            cancellationToken);

    public Task<List<MakeItemRareCode>?> LoadMakeItemRareCodeTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "MAKE_ITEM_LARECODE",
            "byLevelGrade, sUpgradeItem, sLareItem, sMagicItem, sGereralItem",
            static reader => new MakeItemRareCode
            {
                LevelGrade = reader.GetByte(0),
                UpgradeItem = reader.GetInt16(1),
                RareItem = reader.GetInt16(2),
                MagicItem = reader.GetInt16(3),
                GeneralItem = reader.GetInt16(4),
            },
            cancellationToken);

    public Task<List<ZoneInfo>?> LoadZoneInfoTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "ZONE_INFO",
            "ServerNo, ZoneNo, strZoneName, InitX, InitZ, InitY, Type, RoomEvent",
            static reader => new ZoneInfo
            {
                ServerId = reader.GetByte(0),
                ZoneId = reader.GetInt16(1),
                Name = GetString(reader, 2),
                InitX = reader.GetInt32(3),
                InitZ = reader.GetInt32(4),
                InitY = reader.GetInt32(5),
                Type = reader.GetByte(6),
                RoomEvent = reader.GetByte(7),
            },
            cancellationToken);

    private async Task<List<T>?> LoadTableAsync<T>(
        string tableName, string columns, Func<SqlDataReader, T> readRow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand($"SELECT {columns} FROM {tableName}", connection);

            var list = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                list.Add(readRow(reader));

            return list;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Load of table {TableName} failed", tableName);
            return null;
        }
    }

    private static Npc ReadNpc(SqlDataReader reader) => new()
    {
        NpcId = reader.GetInt16(0),
        Name = GetString(reader, 1),
        PictureId = reader.GetInt16(2),
        Size = reader.GetInt16(3),
        Weapon1 = reader.GetInt32(4),
        Weapon2 = reader.GetInt32(5),
        Group = reader.GetByte(6),
        ActType = reader.GetByte(7),
        Type = reader.GetByte(8),
        Family = reader.GetByte(9),
        Rank = reader.GetByte(10),
        Title = reader.GetByte(11),
        SellingGroup = reader.GetInt32(12),
        Level = reader.GetInt16(13),
        Exp = reader.GetInt32(14),
        Loyalty = reader.GetInt32(15),
        HitPoints = reader.GetInt32(16),
        ManaPoints = reader.GetInt16(17),
        Attack = reader.GetInt16(18),
        Armor = reader.GetInt16(19),
        HitRate = reader.GetInt16(20),
        EvadeRate = reader.GetInt16(21),
        Damage = reader.GetInt16(22),
        AttackDelay = reader.GetInt16(23),
        WalkSpeed = reader.GetByte(24),
        RunSpeed = reader.GetByte(25),
        StandTime = reader.GetInt16(26),
        Magic1 = reader.GetInt32(27),
        Magic2 = reader.GetInt32(28),
        Magic3 = reader.GetInt32(29),
        FireResist = reader.GetInt16(30),
        ColdResist = reader.GetInt16(31),
        LightningResist = reader.GetInt16(32),
        MagicResist = reader.GetInt16(33),
        DiseaseResist = reader.GetInt16(34),
        PoisonResist = reader.GetInt16(35),
        LightResist = reader.GetInt16(36),
        Bulk = reader.GetInt16(37),
        AttackRange = reader.GetByte(38),
        SearchRange = reader.GetByte(39),
        TracingRange = reader.GetByte(40),
        Money = reader.GetInt32(41),
        Item = reader.GetInt16(42),
        DirectAttack = reader.GetByte(43),
        MagicAttack = reader.GetByte(44),
        MoneyType = reader.GetByte(45),
    };

    private static Monster ReadMonster(SqlDataReader reader) => new()
    {
        MonsterId = reader.GetInt16(0),
        Name = GetString(reader, 1),
        PictureId = reader.GetInt16(2),
        Size = reader.GetInt16(3),
        Weapon1 = reader.GetInt32(4),
        Weapon2 = reader.GetInt32(5),
        Group = reader.GetByte(6),
        ActType = reader.GetByte(7),
        Type = reader.GetByte(8),
        Family = reader.GetByte(9),
        Rank = reader.GetByte(10),
        Title = reader.GetByte(11),
        SellingGroup = reader.GetInt32(12),
        Level = reader.GetInt16(13),
        Exp = reader.GetInt32(14),
        Loyalty = reader.GetInt32(15),
        HitPoints = reader.GetInt32(16),
        ManaPoints = reader.GetInt16(17),
        Attack = reader.GetInt16(18),
        Armor = reader.GetInt16(19),
        HitRate = reader.GetInt16(20),
        EvadeRate = reader.GetInt16(21),
        Damage = reader.GetInt16(22),
        AttackDelay = reader.GetInt16(23),
        WalkSpeed = reader.GetByte(24),
        RunSpeed = reader.GetByte(25),
        StandTime = reader.GetInt16(26),
        Magic1 = reader.GetInt32(27),
        Magic2 = reader.GetInt32(28),
        Magic3 = reader.GetInt32(29),
        FireResist = reader.GetInt16(30),
        ColdResist = reader.GetInt16(31),
        LightningResist = reader.GetInt16(32),
        MagicResist = reader.GetInt16(33),
        DiseaseResist = reader.GetInt16(34),
        PoisonResist = reader.GetInt16(35),
        LightResist = reader.GetInt16(36),
        Bulk = reader.GetInt16(37),
        AttackRange = reader.GetByte(38),
        SearchRange = reader.GetByte(39),
        TracingRange = reader.GetByte(40),
        Money = reader.GetInt32(41),
        Item = reader.GetInt16(42),
        DirectAttack = reader.GetByte(43),
        MagicAttack = reader.GetByte(44),
        MoneyType = reader.GetByte(45),
    };

    private static string GetString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
}
