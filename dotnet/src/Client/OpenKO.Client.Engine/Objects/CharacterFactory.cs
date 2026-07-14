using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.IO;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Runtime character factory — the engine-side of <c>CPlayerOther::Init</c>.
/// Loads the looks + item tables (Data\UPC_DefaultLooks.tbl, NPC_Looks.tbl,
/// Item_Org_*.tbl, Item_Ext_*_*.tbl) via the path resolver and assembles a live
/// <see cref="ChrRenderer"/> from a race + equipment (no baked .n3chr).
/// </summary>
public sealed class CharacterFactory
{
    private readonly ChrAssetCaches _caches;
    private readonly PlayerLooksTable _playerLooks;
    private readonly PlayerLooksTable? _npcLooks;
    private readonly ItemTableSet _items;

    public CharacterFactory(
        ChrAssetCaches caches, PlayerLooksTable playerLooks,
        ItemTableSet items, PlayerLooksTable? npcLooks = null)
    {
        _caches = caches;
        _playerLooks = playerLooks;
        _items = items;
        _npcLooks = npcLooks;
    }

    /// <summary>
    /// Load the game-data tables from the corpus. Returns null when the player
    /// looks or item table is missing (falls back to the baked-.n3chr path).
    /// </summary>
    public static CharacterFactory? TryLoad(
        KoPathResolver resolver, ChrAssetCaches caches, string lang = "us")
    {
        string? looksPath = resolver.Resolve("Data\\UPC_DefaultLooks.tbl");
        string? itemPath = resolver.Resolve($"Data\\Item_Org_{lang}.tbl");
        if (looksPath == null || itemPath == null)
            return null;

        try
        {
            var playerLooks = PlayerLooksTable.LoadFromFile(looksPath);
            var basic = N3TableFile.LoadFromFile(itemPath);

            var exts = new N3TableFile?[ItemTableSet.MaxItemExtension];
            for (int i = 0; i < ItemTableSet.MaxItemExtension; i++)
            {
                string? extPath = resolver.Resolve($"Data\\Item_Ext_{i}_{lang}.tbl");
                if (extPath != null)
                    exts[i] = N3TableFile.LoadFromFile(extPath);
            }

            string? npcPath = resolver.Resolve("Data\\NPC_Looks.tbl");
            PlayerLooksTable? npcLooks = npcPath != null ? PlayerLooksTable.LoadFromFile(npcPath) : null;

            return new CharacterFactory(caches, playerLooks, new ItemTableSet(basic, exts), npcLooks);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Assemble a player character (CPlayerOther::Init) and build its renderer,
    /// or null when the race has no looks row or the skeleton fails to load.
    /// </summary>
    public ChrRenderer? CreatePlayer(KoRace race, int face, int hair, ReadOnlySpan<uint> itemIds)
    {
        AssembledCharacter? assembled =
            CharacterAssembler.Assemble(_playerLooks, _items, race, face, hair, itemIds);
        if (assembled == null)
            return null;

        try
        {
            var renderer = new ChrRenderer(assembled.Chr, _caches, assembled.PlugJointAnchors);
            return renderer;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Build a monster/NPC renderer from NPC_Looks (keyed by proto/model id):
    /// a baked whole <c>.n3chr</c> when the row carries one, otherwise a runtime
    /// assembly from the row's skeleton + default parts. Null when unavailable.
    /// </summary>
    public ChrRenderer? CreateNpc(int protoId)
    {
        if (_npcLooks == null)
            return null;

        PlayerLooksRow? looks = _npcLooks.Find((uint)protoId);
        if (looks == null)
            return null;

        try
        {
            var chr = new N3Chr();
            if (!string.IsNullOrEmpty(looks.ChrFileName))
            {
                string? path = _caches.Resolver.Resolve(looks.ChrFileName);
                if (path == null)
                    return null;
                chr.LoadFromFile(path);
            }
            else
            {
                chr.JointFileName = looks.JointFileName;
                chr.AniCtrlFileName = looks.AniCtrlFileName;
                chr.FxPlugFileName = looks.FxPlugFileName;
                foreach (string part in looks.PartFileNames)
                    if (!string.IsNullOrEmpty(part))
                        chr.PartFileNames.Add(part);
            }

            var renderer = new ChrRenderer(chr, _caches);
            return renderer.HasSkeleton ? renderer : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
