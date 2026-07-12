using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OpenKO.Data;
using OpenKO.Data.Models;

namespace OpenKO.Servers.Ebenezer.Db;

/// <summary>
/// Loads the GAME-DB tables the Ebenezer server reads at startup (the C++
/// recordset loaders in EbenezerApp). Grows table by table as the stage-4
/// slices land; each loader returns null and logs on SqlException, matching
/// the C++ Load_ForbidEmpty error handling.
/// </summary>
public sealed class EbenezerDb(SqlConnectionFactory connectionFactory, ILogger logger)
{
    /// <summary>EbenezerApp::LoadCoefficientTable (COEFFICIENT).</summary>
    public Task<List<Coefficient>?> LoadCoefficientTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "COEFFICIENT",
            "sClass, ShortSword, Sword, Axe, Club, Spear, Pole, Staff, Bow, Hp, Mp, Sp, Ac, Hitrate, Evasionrate",
            static reader => new Coefficient
            {
                ClassId = reader.GetInt16(0),
                ShortSword = reader.GetDouble(1),
                Sword = reader.GetDouble(2),
                Axe = reader.GetDouble(3),
                Club = reader.GetDouble(4),
                Spear = reader.GetDouble(5),
                Pole = reader.GetDouble(6),
                Staff = reader.GetDouble(7),
                Bow = reader.GetDouble(8),
                HitPoint = reader.GetDouble(9),
                ManaPoint = reader.GetDouble(10),
                Sp = reader.GetDouble(11),
                Armor = reader.GetDouble(12),
                HitRate = reader.GetDouble(13),
                EvasionRate = reader.GetDouble(14),
            },
            cancellationToken);

    /// <summary>EbenezerApp::MapFileLoad source rows (ZONE_INFO).</summary>
    public Task<List<ZoneInfo>?> LoadZoneInfoTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "ZONE_INFO",
            "ServerNo, ZoneNo, strZoneName, InitX, InitZ, InitY, Type, RoomEvent",
            static reader => new ZoneInfo
            {
                ServerId = (byte)reader.GetInt16(0),
                ZoneId = reader.GetInt16(1),
                Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).TrimEnd(),
                InitX = reader.GetInt32(3),
                InitZ = reader.GetInt32(4),
                InitY = reader.GetInt32(5),
                Type = reader.GetByte(6),
                RoomEvent = reader.GetByte(7),
            },
            cancellationToken);

    /// <summary>EbenezerApp::LoadItemTable (ITEM).</summary>
    public Task<List<Item>?> LoadItemTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "ITEM",
            "Num, strName, Kind, Slot, Race, Class, Damage, Delay, Range, Weight, Duration, BuyPrice, SellPrice, Ac, " +
            "Countable, Effect1, Effect2, ReqLevel, ReqLevelMax, ReqRank, ReqTitle, ReqStr, ReqSta, ReqDex, ReqIntel, " +
            "ReqCha, SellingGroup, ItemType, Hitrate, Evasionrate, DaggerAc, SwordAc, MaceAc, AxeAc, SpearAc, BowAc, " +
            "FireDamage, IceDamage, LightningDamage, PoisonDamage, HPDrain, MPDamage, MPDrain, MirrorDamage, Droprate, " +
            "StrB, StaB, DexB, IntelB, ChaB, MaxHpB, MaxMpB, FireR, ColdR, LightningR, MagicR, PoisonR, CurseR",
            static reader => new Item
            {
                ID = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).TrimEnd(),
                Kind = reader.GetByte(2),
                Slot = reader.GetByte(3),
                Race = reader.GetByte(4),
                ClassId = reader.GetByte(5),
                Damage = reader.GetInt16(6),
                Delay = reader.GetInt16(7),
                Range = reader.GetInt16(8),
                Weight = reader.GetInt16(9),
                Durability = reader.GetInt16(10),
                BuyPrice = reader.GetInt32(11),
                SellPrice = reader.GetInt32(12),
                Armor = reader.GetInt16(13),
                Countable = reader.GetByte(14),
                MagicEffect = reader.GetInt32(15),
                SpecialEffect = reader.GetInt32(16),
                MinLevel = reader.GetByte(17),
                MaxLevel = reader.GetByte(18),
                RequiredRank = reader.GetByte(19),
                RequiredTitle = reader.GetByte(20),
                RequiredStrength = reader.GetByte(21),
                RequiredStamina = reader.GetByte(22),
                RequiredDexterity = reader.GetByte(23),
                RequiredIntelligence = reader.GetByte(24),
                RequiredCharisma = reader.GetByte(25),
                SellingGroup = reader.GetByte(26),
                Type = reader.GetByte(27),
                HitRate = reader.GetInt16(28),
                EvasionRate = reader.GetInt16(29),
                DaggerArmor = reader.GetInt16(30),
                SwordArmor = reader.GetInt16(31),
                MaceArmor = reader.GetInt16(32),
                AxeArmor = reader.GetInt16(33),
                SpearArmor = reader.GetInt16(34),
                BowArmor = reader.GetInt16(35),
                FireDamage = reader.GetByte(36),
                IceDamage = reader.GetByte(37),
                LightningDamage = reader.GetByte(38),
                PoisonDamage = reader.GetByte(39),
                HpDrain = reader.GetByte(40),
                MpDamage = reader.GetByte(41),
                MpDrain = reader.GetByte(42),
                MirrorDamage = reader.GetByte(43),
                DropRate = reader.GetByte(44),
                StrengthBonus = reader.GetInt16(45),
                StaminaBonus = reader.GetInt16(46),
                DexterityBonus = reader.GetInt16(47),
                IntelligenceBonus = reader.GetInt16(48),
                CharismaBonus = reader.GetInt16(49),
                MaxHpBonus = reader.GetInt16(50),
                MaxMpBonus = reader.GetInt16(51),
                FireResist = reader.GetInt16(52),
                ColdResist = reader.GetInt16(53),
                LightningResist = reader.GetInt16(54),
                MagicResist = reader.GetInt16(55),
                PoisonResist = reader.GetInt16(56),
                CurseResist = reader.GetInt16(57),
            },
            cancellationToken);

    /// <summary>EbenezerApp::LoadLevelUpTable (LEVEL_UP).</summary>
    public Task<List<LevelUp>?> LoadLevelUpTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "LEVEL_UP",
            "level, Exp",
            static reader => new LevelUp
            {
                Level = reader.GetByte(0),
                RequiredExp = reader.GetInt32(1),
            },
            cancellationToken);

    /// <summary>EbenezerApp::LoadHomeTable (HOME).</summary>
    public Task<List<Home>?> LoadHomeTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "HOME",
            "Nation, ElmoZoneX, ElmoZoneZ, ElmoZoneLX, ElmoZoneLZ, KarusZoneX, KarusZoneZ, KarusZoneLX, KarusZoneLZ, " +
            "FreeZoneX, FreeZoneZ, FreeZoneLX, FreeZoneLZ, BattleZoneX, BattleZoneZ, BattleZoneLX, BattleZoneLZ, " +
            "BattleZone2X, BattleZone2Z, BattleZone2LX, BattleZone2LZ",
            static reader => new Home
            {
                Nation = reader.GetByte(0),
                ElmoZoneX = reader.GetInt32(1),
                ElmoZoneZ = reader.GetInt32(2),
                ElmoZoneLX = reader.GetByte(3),
                ElmoZoneLZ = reader.GetByte(4),
                KarusZoneX = reader.GetInt32(5),
                KarusZoneZ = reader.GetInt32(6),
                KarusZoneLX = reader.GetByte(7),
                KarusZoneLZ = reader.GetByte(8),
                FreeZoneX = reader.GetInt32(9),
                FreeZoneZ = reader.GetInt32(10),
                FreeZoneLX = reader.GetByte(11),
                FreeZoneLZ = reader.GetByte(12),
                BattleZoneX = reader.GetInt32(13),
                BattleZoneZ = reader.GetInt32(14),
                BattleZoneLX = reader.GetByte(15),
                BattleZoneLZ = reader.GetByte(16),
                BattleZone2X = reader.GetInt32(17),
                BattleZone2Z = reader.GetInt32(18),
                BattleZone2LX = reader.GetByte(19),
                BattleZone2LZ = reader.GetByte(20),
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
}
