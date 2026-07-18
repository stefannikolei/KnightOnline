using System.Numerics;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// The FX joint-anchor maths from <c>CPlayerBase::JointPosGet</c>
/// (Client/WarFare/PlayerBase.cpp:2175): a joint's world position is its joint
/// matrix translation transformed by the character's world matrix — position only,
/// the joint orientation is intentionally unused. Extracted as a pure helper so the
/// executable's <c>ClientFxEntityLocator</c> and the unit tests share one
/// implementation.
/// </summary>
public static class FxJointMath
{
    /// <summary>
    /// <c>vPos = jointMatrix.Pos() * chrWorldMatrix</c> — the joint's local-space
    /// translation mapped into world space by the character's world matrix.
    /// </summary>
    public static Vector3 WorldPos(in Matrix4x4 jointMatrix, in Matrix4x4 chrWorld) =>
        Vector3.Transform(jointMatrix.Translation, chrWorld);
}
