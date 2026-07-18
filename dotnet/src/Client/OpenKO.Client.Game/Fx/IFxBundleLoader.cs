using OpenKO.Client.Assets;

namespace OpenKO.Client.Game.Fx;

/// <summary>
/// The seam <see cref="FxManager"/> uses to resolve an FXID to a loaded
/// <see cref="N3FXBundle"/> — the port of the C++
/// <c>s_pTbl_FXSource.Find(FXID)-&gt;szFN</c> effect-table lookup plus the
/// <c>LoadFromFile</c> that reads the <c>.fxb</c>. The returned
/// <see cref="cacheKey"/> is the lower-cased <c>.fxb</c> filename the C++ keys its
/// <c>m_OriginBundle</c> cache by, so two FXIDs that share a file dedupe onto one
/// origin. <see cref="soundId"/> surfaces <c>__TABLE_FX::dwSoundID</c> — the
/// <c>sound.tbl</c> id the C++ passes into <c>TriggerBundle</c> alongside the
/// bundle (0 = no sound).
/// <para>
/// The executable implements this over the FX effect table + the asset loader
/// (with its own bundle cache); tests use a fake returning a synthetic bundle.
/// Returns false for an unknown FXID (the trigger is then a no-op, matching the
/// C++ early-out).
/// </para>
/// </summary>
public interface IFxBundleLoader
{
    bool TryResolve(int fxId, out string cacheKey, out uint soundId, out N3FXBundle bundle);
}
