using OpenKO.Client.Assets.Player;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Sub-slice 9.6-1 pins: the __TABLE_UPC_SKILL row decode (field → column mapping) over a
/// synthetic table, plus a real-corpus row decode of a known skill.
/// </summary>
public class SkillTableTests
{
    [Fact]
    public void SkillRow_DecodesFieldOrder()
    {
        // 30 columns, __TABLE_UPC_SKILL field order (GameDef.h:910). Values chosen so each
        // decoded field is distinct.
        N3TableFile table = BuildTable(SkillColumns(),
        [
            [
                1105u, "Berserk", "Berserker Rage", "A mighty rage.", // id, eng, name, desc
                10, 11,                                              // selfAnim1/2
                12, 13, 14, 15, 16, 17, 18, 19,                      // targetAnim, selfFX/part x2, flyingFX, targetFX/part
                2,                                                   // 15 target
                60,                                                  // 16 needLevel
                45,                                                  // 17 needSkill (% 10 == 5 → first spec tab)
                120,                                                 // 18 exhaustMsp
                30,                                                  // 19 exhaustHp
                7u,                                                  // 20 needItem
                379000u,                                             // 21 exhaustItem
                500,                                                 // 22 castTime
                1500,                                                // 23 recastTime
                0.0f, 0.0f,                                          // 24/25 fIDK0/1
                90,                                                  // 26 percentSuccess
                3u, 4u,                                              // 27/28 1st/2nd table type
                80,                                                  // 29 validDist
                0,                                                   // 30 iIDK2
            ],
        ]);

        var set = new SkillTableSet(table);
        SkillRow? maybe = set.Find(1105);
        Assert.NotNull(maybe);
        SkillRow row = maybe!;

        Assert.Equal(1105u, row.Id);
        Assert.Equal("Berserk", row.EngName);
        Assert.Equal("Berserker Rage", row.Name);
        Assert.Equal("A mighty rage.", row.Desc);
        Assert.Equal(10, row.SelfAnimId1);
        Assert.Equal(2, row.Target);
        Assert.Equal(60, row.NeedLevel);
        Assert.Equal(45, row.NeedSkill);
        Assert.Equal(120, row.ExhaustMsp);
        Assert.Equal(30, row.ExhaustHp);
        Assert.Equal(7u, row.NeedItem);
        Assert.Equal(379000u, row.ExhaustItem);
        Assert.Equal(500, row.CastTime);
        Assert.Equal(1500, row.ReCastTime);
        Assert.Equal(90, row.PercentSuccess);
        Assert.Equal(3u, row.FirstTableType);
        Assert.Equal(4u, row.SecondTableType);
        Assert.Equal(80, row.ValidDist);

        // Ordered + indexed enumeration match the id lookup.
        Assert.Equal(1, set.Count);
        Assert.Equal(1105u, set[0].Id);
        Assert.Same(row, set.All[0]);
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void RealSkillTable_DecodesKnownRow()
    {
        if (AssetCorpus.Root == null)
            return;

        string path = Path.Combine(AssetCorpus.Root, "Data", "skill_magic_main_us.tbl");
        if (!File.Exists(path))
            return;

        SkillTableSet set = SkillTableSet.LoadFromFile(path);
        Assert.True(set.Count > 0);

        // Every row's id splits into a class block; a base-warrior skill (id 101xxx) exists,
        // and its name/level/mana are plausible.
        SkillRow? sample = null;
        foreach (SkillRow r in set.All)
        {
            if (r.Id / 1000 == 101 && r.Id % 1000 != 0)
            {
                sample = r;
                break;
            }
        }

        Assert.NotNull(sample);
        Assert.False(string.IsNullOrEmpty(sample!.Name));
        Assert.InRange(sample.NeedLevel, 1, 200);
        Assert.InRange(sample.ExhaustMsp, 0, 100000);

        // The id lookup round-trips against the ordered enumeration.
        Assert.Same(sample, set.Find(sample.Id));
    }

    private static TblType[] SkillColumns() =>
    [
        TblType.Dword, TblType.String, TblType.String, TblType.String, // 01-04
        TblType.Int, TblType.Int,                                      // 05-06 selfAnim1/2
        TblType.Int, TblType.Int, TblType.Int, TblType.Int, TblType.Int, TblType.Int, TblType.Int, TblType.Int, // 07-14
        TblType.Int, TblType.Int, TblType.Int,                         // 15-17 target/needLevel/needSkill
        TblType.Int, TblType.Int,                                      // 18-19 exhaustMsp/hp
        TblType.Dword, TblType.Dword,                                  // 20-21 needItem/exhaustItem
        TblType.Int, TblType.Int,                                      // 22-23 cast/recast
        TblType.Float, TblType.Float,                                  // 24-25 fIDK0/1
        TblType.Int, TblType.Dword, TblType.Dword,                     // 26-28 percent/1st/2nd
        TblType.Int, TblType.Int,                                      // 29-30 validDist/iIDK2
    ];

    private static N3TableFile BuildTable(IReadOnlyList<TblType> columns, IReadOnlyList<object[]> rows)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(columns.Count);
        foreach (TblType t in columns)
            w.Write((int)t);
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
            case TblType.Int: w.Write(Convert.ToInt32(value)); break;
            case TblType.Dword: w.Write(Convert.ToUInt32(value)); break;
            case TblType.Float: w.Write(Convert.ToSingle(value)); break;
            case TblType.String:
                var s = (string)value;
                w.Write(s.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                break;
            default: throw new InvalidOperationException();
        }
    }
}
