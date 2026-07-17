namespace OpenKO.Client.Engine.Audio;

/// <summary>e_Nation (GameDef.h) — the nation the BGM choice keys off.</summary>
public enum BgmNation
{
    /// <summary>NATION_NOTSELECTED — no nation yet (treated as El Morad for the battle track,
    /// matching the C++ ternary default).</summary>
    None = 0,

    /// <summary>NATION_KARUS.</summary>
    Karus = 1,

    /// <summary>NATION_ELMORAD.</summary>
    ElMorad = 2,
}

/// <summary>The selected background track: its shipped sound id and a stable key/name.</summary>
public readonly record struct BgmTrack(int Id, string Name);

/// <summary>
/// Pure port of <c>CGameProcMain</c>'s town/battle BGM choice
/// (Client/WarFare/GameProcMain.cpp <c>InitZone</c> + <c>PlayBGM_Town</c> /
/// <c>PlayBGM_Battle</c> / <c>UpdateBGM</c>): the town theme plays by default and
/// the nation's battle theme takes over while hostiles are near. The C++ ids come
/// from <c>GameDef.h</c> (<c>ID_SOUND_BGM_*</c>). Deterministic and headless —
/// the executable feeds it the nation + a battle flag each zone/battle change.
/// <para>
/// The shipped client does not vary the town/battle themes by zone (only by
/// nation for the battle theme); <see cref="Select"/> still accepts a
/// <c>zone</c> for signature parity and future zone-specific themes, but it does
/// not affect the base choice.
/// </para>
/// </summary>
public static class BgmSelector
{
    /// <summary>ID_SOUND_BGM_TOWN (GameDef.h) — the town/village theme.</summary>
    public const int TownId = 20000;

    /// <summary>ID_SOUND_BGM_KA_BATTLE (GameDef.h) — the Karus battle theme.</summary>
    public const int KarusBattleId = 20002;

    /// <summary>ID_SOUND_BGM_EL_BATTLE (GameDef.h) — the El Morad battle theme.</summary>
    public const int ElMoradBattleId = 20003;

    /// <summary>Stable sound-manager key for the town theme.</summary>
    public const string TownName = "bgm_town";

    /// <summary>Stable sound-manager key for the Karus battle theme.</summary>
    public const string KarusBattleName = "bgm_ka_battle";

    /// <summary>Stable sound-manager key for the El Morad battle theme.</summary>
    public const string ElMoradBattleName = "bgm_el_battle";

    /// <summary>
    /// The track to play for the current nation + battle state. In battle the
    /// nation's battle theme plays (Karus → KA, everything else → EL, matching the
    /// C++ <c>NATION_KARUS ? KA : EL</c> ternary); otherwise the town theme.
    /// </summary>
    public static BgmTrack Select(BgmNation nation, bool battle, int zone = 0)
    {
        _ = zone; // Reserved: the shipped client does not vary the theme by zone.

        if (!battle)
            return new BgmTrack(TownId, TownName);

        return nation == BgmNation.Karus
            ? new BgmTrack(KarusBattleId, KarusBattleName)
            : new BgmTrack(ElMoradBattleId, ElMoradBattleName);
    }
}
