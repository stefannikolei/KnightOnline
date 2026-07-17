using System.Numerics;

namespace OpenKO.Client.Game.Fx;

/// <summary>
/// The seam <see cref="FxManager"/>/<see cref="FxBundleGame"/> use to resolve a
/// source/target world position (optionally at a joint) from an entity id — the
/// port of the C++ <c>CGameProcMain::CharacterGetByID</c> + <c>JointPosGet</c>
/// lookups the FX bundles do every frame to follow their emitter/target.
/// <para>
/// The executable implements this over <c>WorldEntities</c> (local + remote
/// players + NPCs) and the character renderer's joint matrices; tests use a fake.
/// A <paramref name="joint"/> below zero means "the entity origin" (no joint
/// offset). Returns false when the entity is not present (left the region / died),
/// which the movement acts treat as "target lost" (fly straight on).
/// </para>
/// </summary>
public interface IFxEntityLocator
{
    bool TryGetPosition(int entityId, int joint, out Vector3 pos);
}
