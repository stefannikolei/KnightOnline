using OpenKO.Client.Assets.Player;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Sub-slice 9.5-2 pins: the extended item-table stat block (basic + ext), the
/// buy/sell price port, and a real-corpus row decode.
/// </summary>
public class ItemTableStatsTests
{
    [Fact]
    public void BasicRow_DecodesFullStatBlock()
    {
        // 37 columns, field order per __TABLE_ITEM_BASIC (GameDef.h).
        N3TableFile basic = BuildTable(BasicColumns(),
        [
            [
                100010000u, (byte)22, "Fork", "Event Item", 0u, (byte)0,
                10001000u, 10001000u, 310u, 301u,               // resrc/icon/sounds
                (byte)51, (byte)0, (byte)1,                      // class, robe, attach
                (byte)2, (byte)3,                                // needRace, needClass
                (short)3, (short)200, (short)20, (short)60,      // dmg, interval, range, weight
                (short)4000, 5000, 1,                            // maxDur, price, saleType(=FULL)
                (short)7, (byte)1,                               // defense, countable
                0u, 0u,                                          // effectID1/2
                (sbyte)-5, (sbyte)0,                             // needLevel(signed), cIDK2
                (byte)4, (byte)6,                                // needRank, needTitle
                (byte)10, (byte)11, (byte)12, (byte)13, (byte)14, // need str/sta/dex/int/magic
                (byte)9, (byte)8,                                // sellGroup, grade
            ],
        ]);

        var set = new ItemTableSet(basic, new N3TableFile?[ItemTableSet.MaxItemExtension]);
        (ItemBasicRow? row, _) = set.Find(100010000);
        Assert.NotNull(row);
        row = row!;

        Assert.Equal("Fork", row.Name);
        Assert.Equal("Event Item", row.Remark);
        Assert.Equal((byte)2, row.NeedRace);
        Assert.Equal((byte)3, row.NeedClass);
        Assert.Equal((short)3, row.Damage);
        Assert.Equal((short)200, row.AttackInterval);
        Assert.Equal((short)20, row.AttackRange);
        Assert.Equal((short)60, row.Weight);
        Assert.Equal((short)4000, row.MaxDurability);
        Assert.Equal(5000, row.Price);
        Assert.Equal(ItemTableSet.SaleTypeFull, row.SaleType);
        Assert.Equal((short)7, row.Defense);
        Assert.True(row.Countable);
        Assert.Equal((sbyte)-5, row.NeedLevel);
        Assert.Equal((byte)4, row.NeedRank);
        Assert.Equal((byte)6, row.NeedTitle);
        Assert.Equal((byte)10, row.NeedStrength);
        Assert.Equal((byte)11, row.NeedStamina);
        Assert.Equal((byte)12, row.NeedDexterity);
        Assert.Equal((byte)13, row.NeedInteli);
        Assert.Equal((byte)14, row.NeedMagicAttack);
        Assert.Equal((byte)9, row.SellGroup);
        Assert.Equal((byte)8, row.Grade);
    }

    [Fact]
    public void ExtRow_DecodesFullStatBlock()
    {
        N3TableFile ext = BuildTable(ExtColumns(),
        [
            [
                42u, "Iron", 0u, "rem", 0u,                      // id, header, base, remark, idk0
                20030040u, 20030040u, (byte)4,                   // resrc, icon, magicOrRare
                (short)15, (short)110, (short)20, (short)25,      // dmg, atkInt%, hit, evade
                (short)5000, (short)3, (short)12,                // maxDur, priceMul, defense
                (short)1, (short)2, (short)3, (short)4, (short)5, (short)6, // defenseRate x6
                (byte)7, (byte)8, (byte)9, (byte)10,             // damage fire/ice/thunder/poison
                (byte)0, (byte)0, (byte)0, (byte)0, (byte)0,     // stillHP/dmgMP/stillMP/return/soulbind
                (short)21, (short)22, (short)23, (short)24, (short)25, (short)26, (short)27, // bonus x7
                (short)31, (short)32, (short)33, (short)34, (short)35, (short)36,            // regist x6
                0u, 0u,                                          // effectID1/2
                (short)40, (short)41, (short)42, (short)43, (short)44, (short)45, (short)46, (short)47, // need x8
            ],
        ]);

        // A basic row (id 5000000, extIndex 5) selects ext table 5, row id 42.
        N3TableFile basic = BuildTable(
            [TblType.Dword, TblType.Byte],
            [[5000000u, (byte)5]]);
        var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
        exts[5] = ext;
        var set = new ItemTableSet(basic, exts);

        (_, ItemExtRow? maybe) = set.Find(5000042);
        Assert.NotNull(maybe);
        ItemExtRow row = maybe!;

        Assert.Equal("Iron", row.Header);
        Assert.Equal(20030040u, row.ResourceId);
        Assert.Equal((byte)4, row.MagicOrRare);
        Assert.Equal((short)15, row.Damage);
        Assert.Equal((short)110, row.AttackIntervalPercentage);
        Assert.Equal((short)20, row.HitRate);
        Assert.Equal((short)25, row.EvationRate);
        Assert.Equal((short)5000, row.MaxDurability);
        Assert.Equal((short)3, row.PriceMultiply);
        Assert.Equal((short)12, row.Defense);
        Assert.Equal((short)1, row.DefenseRateDagger);
        Assert.Equal((short)6, row.DefenseRateArrow);
        Assert.Equal((byte)7, row.DamageFire);
        Assert.Equal((byte)8, row.DamageIce);
        Assert.Equal((byte)9, row.DamageThunder);
        Assert.Equal((byte)10, row.DamagePoison);
        Assert.Equal((short)21, row.BonusStr);
        Assert.Equal((short)27, row.BonusMSP);
        Assert.Equal((short)31, row.RegistFire);
        Assert.Equal((short)36, row.RegistCurse);
        Assert.Equal((short)40, row.NeedLevel);
        Assert.Equal((short)47, row.NeedMagicAttack);
    }

    [Fact]
    public void PriceHelpers_PortIconItemSkillFormula()
    {
        var full = new ItemBasicRow { Price = 5000, SaleType = ItemTableSet.SaleTypeFull };
        var normal = new ItemBasicRow { Price = 5000, SaleType = 0 };
        var ext = new ItemExtRow { PriceMultiply = 3 };

        // Buy = price * multiplier.
        Assert.Equal(15000, ItemTableSet.GetBuyPrice(full, ext));

        // Sale-type FULL sells at the buy price.
        Assert.Equal(15000, ItemTableSet.GetSellPrice(full, ext));

        // Non-full: /6 normal, /4 premium.
        Assert.Equal(2500, ItemTableSet.GetSellPrice(normal, ext));           // 15000/6
        Assert.Equal(3750, ItemTableSet.GetSellPrice(normal, ext, hasPremium: true)); // 15000/4

        // Floor at 1.
        var cheap = new ItemBasicRow { Price = 1, SaleType = 0 };
        var mul1 = new ItemExtRow { PriceMultiply = 1 };
        Assert.Equal(1, ItemTableSet.GetSellPrice(cheap, mul1));

        // Null-guarded like the C++.
        Assert.Equal(0, ItemTableSet.GetBuyPrice(null, ext));
        Assert.Equal(0, ItemTableSet.GetSellPrice(full, null));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealItemTable_DecodesKnownRow()
    {
        if (AssetCorpus.Root == null)
            return;

        string path = Path.Combine(AssetCorpus.Root, "Data", "Item_Org_us.tbl");
        if (!File.Exists(path))
            return;

        N3TableFile basic = N3TableFile.LoadFromFile(path);
        var set = new ItemTableSet(basic, new N3TableFile?[ItemTableSet.MaxItemExtension]);

        // Item 100010000 "Fork" (see Item_Org_us.tbl.csv): a known event weapon.
        (ItemBasicRow? row, _) = set.Find(100010000);
        Assert.NotNull(row);
        Assert.Equal("Fork", row!.Name);
        Assert.Equal((short)60, row.Weight);
        Assert.Equal(5000, row.Price);
        Assert.Equal((short)4000, row.MaxDurability);
        Assert.Equal((short)3, row.Damage);
        Assert.Equal((sbyte)1, row.NeedLevel);
        Assert.Equal((byte)22, row.ExtIndex);
    }

    private static TblType[] BasicColumns() =>
    [
        TblType.Dword, TblType.Byte, TblType.String, TblType.String, TblType.Dword, TblType.Byte,
        TblType.Dword, TblType.Dword, TblType.Dword, TblType.Dword,       // 07-10
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, // 11-15
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, // 16-20
        TblType.Int, TblType.Int, TblType.Short, TblType.Byte,           // 21-24
        TblType.Dword, TblType.Dword, TblType.Char, TblType.Char,        // 25-28
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, // 29-35
        TblType.Byte, TblType.Byte,                                      // 36-37
    ];

    private static TblType[] ExtColumns() =>
    [
        TblType.Dword, TblType.String, TblType.Dword, TblType.String, TblType.Dword, // 01-05
        TblType.Dword, TblType.Dword, TblType.Byte,                     // 06-08
        TblType.Short, TblType.Short, TblType.Short, TblType.Short,      // 09-12
        TblType.Short, TblType.Short, TblType.Short,                     // 13-15
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, // 16-21
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte,          // 22-25
        TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, TblType.Byte, // 26-30
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, // 31-37
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, // 38-43
        TblType.Dword, TblType.Dword,                                    // 44-45
        TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, TblType.Short, // 46-53
    ];

    private static N3TableFile BuildTable(IReadOnlyList<TblType> columns, IReadOnlyList<object[]> rows)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(columns.Count);
        foreach (TblType t in columns) w.Write((int)t);
        w.Write(rows.Count);
        foreach (object[] row in rows)
            for (int j = 0; j < columns.Count; j++)
                WriteCell(w, columns[j], row[j]);
        w.Flush();
        return N3TableFile.Load(ms.ToArray(), encrypted: false);
    }

    private static void WriteCell(BinaryWriter w, TblType type, object value)
    {
        switch (type)
        {
            case TblType.Char: w.Write(unchecked((byte)Convert.ToSByte(value))); break;
            case TblType.Byte: w.Write(Convert.ToByte(value)); break;
            case TblType.Short: w.Write(Convert.ToInt16(value)); break;
            case TblType.Word: w.Write(Convert.ToUInt16(value)); break;
            case TblType.Int: w.Write(Convert.ToInt32(value)); break;
            case TblType.Dword: w.Write(Convert.ToUInt32(value)); break;
            case TblType.String:
                var s = (string)value;
                w.Write(s.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                break;
            default: throw new InvalidOperationException();
        }
    }
}
