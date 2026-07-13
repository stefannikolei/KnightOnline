namespace OpenKO.Client.Assets.Player;

/// <summary>
/// An <see cref="N3Chr"/> assembled at runtime from the looks + item tables,
/// plus the per-plug joint anchors the C++ <c>CPlayerBase::PlugSet</c> writes
/// onto the loaded plug (which the renderer applies once it loads the plug).
/// </summary>
public sealed class AssembledCharacter
{
    public required N3Chr Chr { get; init; }

    public required PlayerLooksRow Looks { get; init; }

    /// <summary>Joint index per <see cref="KoPlugPosition"/> (−1 = no override).</summary>
    public int[] PlugJointAnchors { get; } = [-1, -1, -1, -1];
}

/// <summary>
/// Port of <c>CPlayerOther::Init</c> / <c>CPlayerBase::InitChr</c> + the
/// <c>PartSet</c>/<c>PlugSet</c>/<c>InitFace</c>/<c>InitHair</c> plumbing
/// (PlayerOther.cpp, PlayerBase.cpp): builds a character's <see cref="N3Chr"/>
/// from a race, face/hair index and the eight equipped item ids — no baked
/// .n3chr, exactly like the live client.
/// </summary>
public static class CharacterAssembler
{
    /// <summary>MAX_ITEM_SLOT_OPC — the eight visible-equipment slots.</summary>
    public const int MaxItemSlots = 8;

    public static AssembledCharacter? Assemble(
        PlayerLooksTable looksTable, ItemTableSet items,
        KoRace race, int face, int hair, ReadOnlySpan<uint> itemIds)
    {
        PlayerLooksRow? looks = looksTable.Find(race);
        if (looks == null)
            return null;

        var chr = new N3Chr
        {
            JointFileName = looks.JointFileName,
            AniCtrlFileName = looks.AniCtrlFileName,
            FxPlugFileName = looks.FxPlugFileName,
        };

        // PartAlloc(PART_POS_COUNT) / PlugAlloc(PLUG_POS_COUNT): fixed empty slots.
        for (int i = 0; i < (int)KoPartPosition.Count; i++)
            chr.PartFileNames.Add(string.Empty);
        for (int i = 0; i < (int)KoPlugPosition.Count; i++)
            chr.PlugFileNames.Add(string.Empty);

        // InitChr joint-part ranges (RACE != NPC): part 0 lower, part 1 upper.
        if (race != KoRace.Npc)
        {
            chr.JointPartStarts[0] = 16;
            chr.JointPartEnds[0] = 23;
            chr.JointPartStarts[1] = 1;
            chr.JointPartEnds[1] = 15;
        }

        var result = new AssembledCharacter { Chr = chr, Looks = looks };
        var partItems = new ItemBasicRow?[(int)KoPartPosition.Count];

        int count = Math.Min(itemIds.Length, MaxItemSlots);
        for (int i = 0; i < count; i++)
        {
            uint itemId = itemIds[i];
            if (itemId == 0)
            {
                // Nothing equipped: the looks-table default body part (head/cloak
                // are left for InitHair / cape).
                (KoPartPosition part, string fn) = i switch
                {
                    0 => (KoPartPosition.Upper, looks.PartFileNames[0]),
                    1 => (KoPartPosition.Lower, looks.PartFileNames[1]),
                    3 => (KoPartPosition.Hands, looks.PartFileNames[3]),
                    4 => (KoPartPosition.Feet, looks.PartFileNames[4]),
                    _ => (KoPartPosition.Unknown, string.Empty),
                };
                if (part != KoPartPosition.Unknown)
                    SetPart(chr, partItems, part, fn, null);
                continue;
            }

            (ItemBasicRow? basic, ItemExtRow? ext) = items.Find(itemId);
            if (basic == null || ext == null)
                continue; // C++ asserts and skips when either row is missing

            ItemResourceName res = ItemResourceNamer.MakeResourceFileName(basic, ext, race);

            // The dispatch position is fixed by the slot index i (not by the
            // item's own attach point) — verbatim CPlayerOther::Init.
            switch (i)
            {
                case 0: SetPart(chr, partItems, KoPartPosition.Upper, res.ResourceFileName, basic); break;
                case 1: SetPart(chr, partItems, KoPartPosition.Lower, res.ResourceFileName, basic); break;
                case 2: SetPart(chr, partItems, KoPartPosition.HairHelmet, res.ResourceFileName, basic); break;
                case 3: SetPart(chr, partItems, KoPartPosition.Hands, res.ResourceFileName, basic); break;
                case 4: SetPart(chr, partItems, KoPartPosition.Feet, res.ResourceFileName, basic); break;
                case 6: SetPlug(chr, result, looks, KoPlugPosition.RightHand, res.ResourceFileName, basic); break;
                case 7: SetPlug(chr, result, looks, KoPlugPosition.LeftHand, res.ResourceFileName, basic); break;
                // case 5 (cloak): no model
            }
        }

        // Faces last, hair only when no helmet occupies the head slot.
        InitFace(chr, looks, face);
        if (string.IsNullOrEmpty(chr.PartFileNames[(int)KoPartPosition.HairHelmet]))
            InitHair(chr, looks, hair);

        return result;
    }

    /// <summary>CPlayerBase::PartSet — with the robe upper/lower interaction.</summary>
    private static void SetPart(
        N3Chr chr, ItemBasicRow?[] partItems, KoPartPosition part, string fn, ItemBasicRow? item)
    {
        int idx = (int)part;

        if (part == KoPartPosition.Upper && item is { IsRobeType: true }
            && !string.IsNullOrEmpty(chr.PartFileNames[(int)KoPartPosition.Lower]))
        {
            // A robe clears the lower slot.
            chr.PartFileNames[(int)KoPartPosition.Lower] = string.Empty;
            partItems[(int)KoPartPosition.Lower] = null;
        }
        else if (part == KoPartPosition.Lower && item != null
            && partItems[(int)KoPartPosition.Upper] is { IsRobeType: true })
        {
            // Wearing a robe up top hides any lower part.
            partItems[idx] = item;
            chr.PartFileNames[idx] = string.Empty;
            return;
        }

        chr.PartFileNames[idx] = fn;
        partItems[idx] = item;
    }

    /// <summary>CPlayerBase::PlugSet — records the hand/forearm joint anchor.</summary>
    private static void SetPlug(
        N3Chr chr, AssembledCharacter result, PlayerLooksRow looks,
        KoPlugPosition plug, string fn, ItemBasicRow? item)
    {
        int idx = (int)plug;
        chr.PlugFileNames[idx] = fn;

        result.PlugJointAnchors[idx] = plug switch
        {
            KoPlugPosition.RightHand => looks.JointRightHand,
            KoPlugPosition.LeftHand => item is { Class: KoItemClass.Shield }
                ? looks.JointLeftForearm
                : looks.JointLeftHand,
            _ => -1,
        };
    }

    private static void InitFace(N3Chr chr, PlayerLooksRow looks, int face)
    {
        string template = looks.PartFileNames[(int)KoPartPosition.Face];
        if (!string.IsNullOrEmpty(template))
            chr.PartFileNames[(int)KoPartPosition.Face] = InsertIndex(template, face);
    }

    private static void InitHair(N3Chr chr, PlayerLooksRow looks, int hair)
    {
        string template = looks.PartFileNames[(int)KoPartPosition.HairHelmet];
        chr.PartFileNames[(int)KoPartPosition.HairHelmet] =
            string.IsNullOrEmpty(template) ? string.Empty : InsertIndex(template, hair);
    }

    /// <summary>The _splitpath + "{dir}{name}{index:02}{ext}" rule (backslash paths).</summary>
    public static string InsertIndex(string template, int index)
    {
        int slash = template.LastIndexOfAny(['\\', '/']);
        string dir = slash >= 0 ? template[..(slash + 1)] : string.Empty;
        string rest = slash >= 0 ? template[(slash + 1)..] : template;
        int dot = rest.LastIndexOf('.');
        string name = dot >= 0 ? rest[..dot] : rest;
        string ext = dot >= 0 ? rest[dot..] : string.Empty;
        return $"{dir}{name}{index:D2}{ext}";
    }
}
