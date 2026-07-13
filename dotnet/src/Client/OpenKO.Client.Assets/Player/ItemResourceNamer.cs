using System.Globalization;

namespace OpenKO.Client.Assets.Player;

/// <summary>The result of <see cref="ItemResourceNamer.MakeResourceFileName"/>.</summary>
public readonly record struct ItemResourceName(
    KoItemType Type,
    KoPartPosition PartPosition,
    KoPlugPosition PlugPosition,
    string ResourceFileName,
    string IconFileName);

/// <summary>
/// Port of <c>CGameBase::MakeResrcFileNameForUPC</c> (GameBase.cpp): turns an
/// item (basic + ext) into a part/plug/icon resource file name and classifies
/// its slot. The wearable-part path folds the race into the mid id field; the
/// digit decomposition of an 8-digit id <c>D7 D6D5D4D3 D2D1 D0</c> is verbatim.
/// </summary>
public static class ItemResourceNamer
{
    public static ItemResourceName MakeResourceFileName(
        ItemBasicRow? item, ItemExtRow? ext, KoRace race = KoRace.Unknown)
    {
        var partPos = KoPartPosition.Unknown;
        var plugPos = KoPlugPosition.Unknown;

        if (item == null)
            return new ItemResourceName(KoItemType.Unknown, partPos, plugPos, string.Empty, string.Empty);

        var type = KoItemType.Unknown;
        KoItemPosition pos = item.AttachPoint;
        string ext2 = string.Empty;

        if (pos is >= KoItemPosition.Dual and <= KoItemPosition.TwoHandLeft)
        {
            plugPos = pos is KoItemPosition.Dual or KoItemPosition.RightHand or KoItemPosition.TwoHandRight
                ? KoPlugPosition.RightHand
                : KoPlugPosition.LeftHand;
            type = KoItemType.Plug;
            ext2 = ".n3cplug";
        }
        else if (pos is >= KoItemPosition.Upper and <= KoItemPosition.Shoes)
        {
            partPos = pos switch
            {
                KoItemPosition.Upper => KoPartPosition.Upper,
                KoItemPosition.Lower => KoPartPosition.Lower,
                KoItemPosition.Head => KoPartPosition.HairHelmet,
                KoItemPosition.Gloves => KoPartPosition.Hands,
                KoItemPosition.Shoes => KoPartPosition.Feet,
                _ => KoPartPosition.Unknown,
            };
            type = KoItemType.Part;
            ext2 = ".n3cpart";
        }
        else if (pos is >= KoItemPosition.Ear and <= KoItemPosition.Inventory)
        {
            type = KoItemType.IconOnly;
            ext2 = ".dxt";
        }
        else if (pos == KoItemPosition.Gold)
        {
            type = KoItemType.Gold;
            ext2 = ".dxt";
        }
        else if (pos == KoItemPosition.Songpyun)
        {
            type = KoItemType.Songpyun;
            ext2 = ".dxt";
        }

        // Resource/icon ids: the ext row overrides the basic when non-zero.
        uint idResrc = ext is { ResourceId: not 0 } ? ext.ResourceId : item.ResourceId;
        uint idIcon = ext is { IconId: not 0 } ? ext.IconId : item.IconId;

        string resrcFn = string.Empty;
        if (item.ResourceId != 0)
        {
            bool foldRace = race != KoRace.Unknown
                && pos is >= KoItemPosition.Upper and <= KoItemPosition.Shoes;
            uint mid = (idResrc / 1000) % 10000;
            if (foldRace)
                mid += (uint)race;
            resrcFn = FormatName("Item\\", idResrc, mid, ext2);
        }

        string iconFn = FormatName("UI\\ItemIcon_", idIcon, (idIcon / 1000) % 10000, ".dxt");

        return new ItemResourceName(type, partPos, plugPos, resrcFn, iconFn);
    }

    // fmt "{:01}_{:04}_{:02}_{:01}{ext}" — field1 = D7, mid = D6..D3 (+race), D2D1, D0.
    private static string FormatName(string prefix, uint id, uint mid, string ext)
    {
        uint f1 = id / 10000000;
        uint f3 = (id / 10) % 100;
        uint f4 = id % 10;
        return string.Create(CultureInfo.InvariantCulture,
            $"{prefix}{f1:D1}_{mid:D4}_{f3:D2}_{f4:D1}{ext}");
    }
}
