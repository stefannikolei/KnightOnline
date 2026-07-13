using OpenKO.Client.Assets.Player;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Pins for the runtime character-assembly data layer: the MakeResrcFileNameForUPC
/// filename rule, the item id split, and the CPlayerOther::Init part/plug wiring.
/// </summary>
public class CharacterAssemblyTests
{
    // ---- ItemResourceNamer (MakeResrcFileNameForUPC) ----

    [Fact]
    public void ResourceName_BodyPart_FoldsRaceIntoMidField()
    {
        // Upper-body item (attach point UPPER=5), resource id 20010010.
        var basic = new ItemBasicRow
        {
            Id = 200100100,
            ResourceId = 20010010,
            AttachPoint = KoItemPosition.Upper,
        };

        ItemResourceName r = ItemResourceNamer.MakeResourceFileName(basic, null, KoRace.Man);

        Assert.Equal(KoItemType.Part, r.Type);
        Assert.Equal(KoPartPosition.Upper, r.PartPosition);
        // id 20010010: D7=2, mid=(20010010/1000)%10000=0010 -> +race(12)=0022, D2D1=(.. /10)%100=01, D0=0
        Assert.Equal("Item\\2_0022_01_0.n3cpart", r.ResourceFileName);
    }

    [Fact]
    public void ResourceName_Weapon_IsPlugWithoutRaceFold()
    {
        // Right-hand weapon (attach point RIGHTHAND=1), resource id 10020030.
        var basic = new ItemBasicRow
        {
            Id = 100200300,
            ResourceId = 10020030,
            AttachPoint = KoItemPosition.RightHand,
        };

        ItemResourceName r = ItemResourceNamer.MakeResourceFileName(basic, null, KoRace.Man);

        Assert.Equal(KoItemType.Plug, r.Type);
        Assert.Equal(KoPlugPosition.RightHand, r.PlugPosition);
        // No race fold for plugs: mid = (10020030/1000)%10000 = 0020.
        Assert.Equal("Item\\1_0020_03_0.n3cplug", r.ResourceFileName);
    }

    [Fact]
    public void ResourceName_ExtResourceIdOverridesBasic()
    {
        var basic = new ItemBasicRow { ResourceId = 20010010, AttachPoint = KoItemPosition.Gloves };
        var ext = new ItemExtRow { ResourceId = 20030040 };

        ItemResourceName r = ItemResourceNamer.MakeResourceFileName(basic, ext, KoRace.Unknown);
        // Ext id used; no race fold (race unknown). mid = (20030040/1000)%10000 = 0030.
        Assert.Equal("Item\\2_0030_04_0.n3cpart", r.ResourceFileName);
        Assert.Equal(KoPartPosition.Hands, r.PartPosition);
    }

    [Fact]
    public void ResourceName_IconOnly_ClearsResourceWhenNoModel()
    {
        var basic = new ItemBasicRow { ResourceId = 0, IconId = 30040050, AttachPoint = KoItemPosition.Ear };
        ItemResourceName r = ItemResourceNamer.MakeResourceFileName(basic, null);
        Assert.Equal(KoItemType.IconOnly, r.Type);
        Assert.Equal(string.Empty, r.ResourceFileName);
        Assert.Equal("UI\\ItemIcon_3_0040_05_0.dxt", r.IconFileName);
    }

    // ---- ItemTableSet (id split) ----

    [Fact]
    public void ItemTable_SplitsIdIntoBaseAndExt()
    {
        // basic keyed by id/1000*1000, ext keyed by id%1000.
        N3TableFile basic = BuildTable(
            [TblType.Dword, TblType.Byte, TblType.String, TblType.String,
             TblType.Dword, TblType.Byte, TblType.Dword, TblType.Dword, TblType.Dword,
             TblType.Dword, TblType.Byte, TblType.Byte, TblType.Byte],
            [BasicRow(379001000, extIndex: 2, resrc: 20010010, cls: 0, robe: 0, attach: (byte)KoItemPosition.Upper)]);

        N3TableFile ext = BuildTable(
            [TblType.Dword, TblType.String, TblType.Dword, TblType.String, TblType.Dword,
             TblType.Dword, TblType.Dword, TblType.Byte],
            [ExtRow(id: 42, resrc: 20030040)]);

        var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
        exts[2] = ext;
        var set = new ItemTableSet(basic, exts);

        (ItemBasicRow? b, ItemExtRow? e) = set.Find(379001042);
        Assert.NotNull(b);
        Assert.Equal(379001000u, b!.Id);
        Assert.Equal(2, b.ExtIndex);
        Assert.Equal(KoItemPosition.Upper, b.AttachPoint);
        Assert.NotNull(e);
        Assert.Equal(20030040u, e!.ResourceId);
    }

    // ---- CharacterAssembler (CPlayerOther::Init) ----

    [Fact]
    public void Assemble_UsesLooksDefaultsAndFaceHairTemplates()
    {
        PlayerLooksTable looks = BuildLooks();
        var items = new ItemTableSet(BuildTable([TblType.Dword], []), new N3TableFile?[ItemTableSet.MaxItemExtension]);

        // All eight slots empty → default parts + InitFace/InitHair.
        AssembledCharacter? c = CharacterAssembler.Assemble(looks, items, KoRace.Man, face: 3, hair: 5, new uint[8]);

        Assert.NotNull(c);
        Assert.Equal("Chr\\Man.n3cjoint", c!.Chr.JointFileName);
        Assert.Equal("Chr\\Man\\Upper.n3cpart", c.Chr.PartFileNames[(int)KoPartPosition.Upper]);
        Assert.Equal("Chr\\Man\\Feet.n3cpart", c.Chr.PartFileNames[(int)KoPartPosition.Feet]);
        // InitFace/InitHair suffix the templates with the 2-digit index.
        Assert.Equal("Chr\\Man\\Face03.n3cpart", c.Chr.PartFileNames[(int)KoPartPosition.Face]);
        Assert.Equal("Chr\\Man\\Hair05.n3cpart", c.Chr.PartFileNames[(int)KoPartPosition.HairHelmet]);
    }

    [Fact]
    public void Assemble_HelmetSuppressesHair()
    {
        PlayerLooksTable looks = BuildLooks();
        // A head item (slot 2) → HairHelmet gets the helmet model, InitHair is skipped.
        N3TableFile basic = BuildTable(
            [TblType.Dword, TblType.Byte, TblType.String, TblType.String,
             TblType.Dword, TblType.Byte, TblType.Dword, TblType.Dword, TblType.Dword,
             TblType.Dword, TblType.Byte, TblType.Byte, TblType.Byte],
            [BasicRow(500700000, extIndex: 0, resrc: 50070010, cls: 0, robe: 0, attach: (byte)KoItemPosition.Head)]);
        N3TableFile ext = BuildTable(
            [TblType.Dword, TblType.String, TblType.Dword, TblType.String, TblType.Dword,
             TblType.Dword, TblType.Dword, TblType.Byte],
            [ExtRow(id: 0, resrc: 0)]);
        var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
        exts[0] = ext;
        var items = new ItemTableSet(basic, exts);

        var slots = new uint[8];
        slots[2] = 500700000; // head
        AssembledCharacter? c = CharacterAssembler.Assemble(looks, items, KoRace.Man, 1, 1, slots);

        Assert.NotNull(c);
        string head = c!.Chr.PartFileNames[(int)KoPartPosition.HairHelmet];
        Assert.StartsWith("Item\\", head);
        Assert.EndsWith(".n3cpart", head);
        Assert.DoesNotContain("Hair", head); // hair template not applied
    }

    [Fact]
    public void Assemble_ShieldAnchorsToForearmJoint()
    {
        PlayerLooksTable looks = BuildLooks();
        N3TableFile basic = BuildTable(
            [TblType.Dword, TblType.Byte, TblType.String, TblType.String,
             TblType.Dword, TblType.Byte, TblType.Dword, TblType.Dword, TblType.Dword,
             TblType.Dword, TblType.Byte, TblType.Byte, TblType.Byte],
            [BasicRow(600100000, extIndex: 0, resrc: 60010010, cls: KoItemClass.Shield, robe: 0, attach: (byte)KoItemPosition.LeftHand)]);
        N3TableFile ext = BuildTable(
            [TblType.Dword, TblType.String, TblType.Dword, TblType.String, TblType.Dword,
             TblType.Dword, TblType.Dword, TblType.Byte],
            [ExtRow(id: 0, resrc: 0)]);
        var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
        exts[0] = ext;
        var items = new ItemTableSet(basic, exts);

        var slots = new uint[8];
        slots[7] = 600100000; // left hand shield
        AssembledCharacter? c = CharacterAssembler.Assemble(looks, items, KoRace.Man, 1, 1, slots);

        Assert.NotNull(c);
        // Looks forearm joint = 21 (set in BuildLooks).
        Assert.Equal(21, c!.PlugJointAnchors[(int)KoPlugPosition.LeftHand]);
        Assert.NotEqual(string.Empty, c.Chr.PlugFileNames[(int)KoPlugPosition.LeftHand]);
    }

    [Fact]
    public void InsertIndex_MatchesSplitpathRule()
    {
        Assert.Equal("Chr\\Elm\\Face07.n3cpart",
            CharacterAssembler.InsertIndex("Chr\\Elm\\Face.n3cpart", 7));
        Assert.Equal("Hair10.n3cpart", CharacterAssembler.InsertIndex("Hair.n3cpart", 10));
    }

    // ---- fixtures ----

    private static PlayerLooksTable BuildLooks()
    {
        // Columns: dwID, szName, szJointFN, szAniFN, szPartFNs[10], szSkinFN,
        // szChrFN, szFXPlugFN, iIdk1, iJointRH, iJointLH, iJointLH2, iJointCloak.
        var cols = new List<TblType> { TblType.Dword, TblType.String, TblType.String, TblType.String };
        for (int i = 0; i < 10; i++) cols.Add(TblType.String);
        cols.AddRange([TblType.String, TblType.String, TblType.String,
            TblType.Int, TblType.Int, TblType.Int, TblType.Int, TblType.Int]);

        object[] row =
        [
            (uint)KoRace.Man, "ElMoradMan", "Chr\\Man.n3cjoint", "Chr\\Man.n3canim",
            "Chr\\Man\\Upper.n3cpart", "Chr\\Man\\Lower.n3cpart", "Chr\\Man\\Face.n3cpart",
            "Chr\\Man\\Hands.n3cpart", "Chr\\Man\\Feet.n3cpart", "Chr\\Man\\Hair.n3cpart",
            "", "", "", "", // remaining part slots
            "", "", "", // skin, chr, fxplug
            0, 30, 25, 21, 40, // iIdk1, RH, LH, LH2(forearm), cloak
        ];
        return new PlayerLooksTable(BuildTable(cols, [row]));
    }

    private static object[] BasicRow(uint id, byte extIndex, uint resrc, byte cls, byte robe, byte attach) =>
        [id, extIndex, "name", "remark", 0u, (byte)0, resrc, 0u, 0u, 0u, cls, robe, attach];

    private static object[] ExtRow(uint id, uint resrc) =>
        [id, "hdr", 0u, "remark", 0u, resrc, 0u, (byte)0];

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
            case TblType.Char:
            case TblType.Byte: w.Write(Convert.ToByte(value)); break;
            case TblType.Short: w.Write(Convert.ToInt16(value)); break;
            case TblType.Word: w.Write(Convert.ToUInt16(value)); break;
            case TblType.Int: w.Write(Convert.ToInt32(value)); break;
            case TblType.Dword: w.Write(Convert.ToUInt32(value)); break;
            case TblType.Float: w.Write(Convert.ToSingle(value)); break;
            case TblType.Double: w.Write(Convert.ToDouble(value)); break;
            case TblType.String:
                var s = (string)value;
                w.Write(s.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes(s));
                break;
            default: throw new InvalidOperationException();
        }
    }
}
