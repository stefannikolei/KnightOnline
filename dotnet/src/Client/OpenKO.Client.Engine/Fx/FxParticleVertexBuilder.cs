using System.Numerics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Pure port of the camera-facing quad build inside <c>CN3FXParticle::Tick</c>
/// (N3FXParticle.cpp): each live particle becomes four <c>__VertexXyzColorT1</c>
/// vertices, <c>(unit[i] * scale) * texRotate * mtxVI + worldPos</c>, where
/// <c>mtxVI</c> is the inverse-view rotation (translation zeroed) that makes the
/// quad face the camera. Kept out of the sim so it is device-free and the exact
/// vertex positions/UVs are golden-testable; <c>FxRenderer</c> uploads the array.
/// </summary>
public static class FxParticleVertexBuilder
{
    /// <summary>NUM_VERTEX_PARTICLE.</summary>
    public const int VerticesPerParticle = N3FxDef.NumVertexParticle;

    /// <summary>m_vUnit — the unit quad corners (CN3FXPartParticles ctor).</summary>
    public static readonly Vector3[] UnitPositions =
    [
        new(-0.5f, 0.5f, 0f),
        new(0.5f, 0.5f, 0f),
        new(0.5f, -0.5f, 0f),
        new(-0.5f, -0.5f, 0f),
    ];

    /// <summary>The matching UV corners.</summary>
    public static readonly Vector2[] UnitUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    /// <summary>
    /// Builds the four vertices for one particle into <paramref name="dest"/>
    /// starting at <paramref name="offset"/>. <paramref name="viewInverseRotation"/>
    /// is the C++ <c>m_mtxVI</c> (inverse view with position zeroed).
    /// </summary>
    public static void BuildParticle(
        FxRuntimeParticle p,
        in Matrix4x4 viewInverseRotation,
        float texRotateVelocity,
        float scaleVelX,
        float scaleVelY,
        Span<N3VertexXyzColorT1> dest,
        int offset)
    {
        var scale = new Vector3(
            p.Size + (scaleVelX * p.VertexCurrLife),
            p.Size + (scaleVelY * p.VertexCurrLife),
            p.Size);
        if (scale.X < 0f)
            scale.X = 0f;
        if (scale.Y < 0f)
            scale.Y = 0f;
        if (scale.Z < 0f)
            scale.Z = 0f;

        Matrix4x4 texRotate = FxMath.RotationZ(texRotateVelocity * p.VertexCurrLife);

        for (int i = 0; i < VerticesPerParticle; i++)
        {
            Vector3 local = UnitPositions[i] * scale;
            local = Vector3.Transform(local, texRotate);
            Vector3 pos = Vector3.Transform(local, viewInverseRotation) + p.WorldPos;

            dest[offset + i] = new N3VertexXyzColorT1
            {
                Position = pos,
                Color = p.Color,
                Tu = UnitUvs[i].X,
                Tv = UnitUvs[i].Y,
            };
        }
    }

    /// <summary>
    /// Builds every live particle into a fresh <c>alive*4</c> vertex array, in the
    /// particle order the sim holds (which is the emission order). Vertices are in
    /// fan corner order per particle; use <see cref="ExpandFanIndices"/> for a
    /// triangle list.
    /// </summary>
    public static N3VertexXyzColorT1[] Build(
        IReadOnlyList<FxRuntimeParticle> alive,
        in Matrix4x4 viewInverseRotation,
        float texRotateVelocity,
        float scaleVelX,
        float scaleVelY)
    {
        var vertices = new N3VertexXyzColorT1[alive.Count * VerticesPerParticle];
        Span<N3VertexXyzColorT1> span = vertices;
        for (int i = 0; i < alive.Count; i++)
        {
            BuildParticle(
                alive[i], viewInverseRotation, texRotateVelocity, scaleVelX, scaleVelY,
                span, i * VerticesPerParticle);
        }

        return vertices;
    }

    /// <summary>
    /// Expands N quads (4 fan verts each) into a triangle-list index buffer:
    /// the fan 0-1-2, 0-2-3 per quad, so the device layer can draw the whole batch
    /// with one non-indexed... (indexed) call.
    /// </summary>
    public static short[] ExpandFanIndices(int quadCount)
    {
        var indices = new short[quadCount * 6];
        for (int q = 0; q < quadCount; q++)
        {
            int baseVertex = q * VerticesPerParticle;
            int o = q * 6;
            indices[o + 0] = (short)(baseVertex + 0);
            indices[o + 1] = (short)(baseVertex + 1);
            indices[o + 2] = (short)(baseVertex + 2);
            indices[o + 3] = (short)(baseVertex + 0);
            indices[o + 4] = (short)(baseVertex + 2);
            indices[o + 5] = (short)(baseVertex + 3);
        }

        return indices;
    }
}
