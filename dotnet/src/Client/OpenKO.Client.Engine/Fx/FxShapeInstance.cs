using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Device-ready geometry for a mesh part's <c>CN3FXShape</c> (the shape a
/// <see cref="FxMeshSimulator"/> drives): each <see cref="N3FXShapePart"/> resolved
/// to its <see cref="N3FXPMesh"/> colored vertex/index buffers + animation-frame
/// textures + pivot. Built once by the host (which caches it by shape file name)
/// and rendered by <see cref="FxRenderer"/> under the sim's parent matrix.
/// <para>
/// The progressive-mesh LOD walk (CN3FXPMeshInstance::SetLOD) is not ported, so the
/// full-detail buffers are used every frame — always geometrically valid, just not
/// distance-reduced. The per-part texture material's blend/cull/z come from the FX
/// mesh <em>part</em> descriptor (CN3FXPartMesh::SetPartsMtl copies them onto the
/// shape parts), so only the geometry + textures live here.
/// </para>
/// </summary>
public sealed class FxShapeInstance
{
    /// <summary>One resolved shape part: its colored geometry, pivot and frame textures.</summary>
    public sealed class Part
    {
        public required N3VertexXyzColorT1[] Vertices { get; init; }

        public required ushort[] Indices { get; init; }

        public required Vector3 Pivot { get; init; }

        public required Texture2D?[] Textures { get; init; }

        public required float TexFps { get; init; }
    }

    private readonly List<Part> _parts = [];

    /// <param name="shape">The resolved <c>.n3fxshape</c>.</param>
    /// <param name="meshResolver">shape-part mesh file name → the loaded <see cref="N3FXPMesh"/> (or null).</param>
    /// <param name="textureResolver">shape part + frame index → the frame texture (or null).</param>
    public FxShapeInstance(
        N3FXShape shape,
        Func<string, N3FXPMesh?> meshResolver,
        Func<N3FXShapePart, int, Texture2D?> textureResolver)
    {
        foreach (N3FXShapePart part in shape.Parts)
        {
            N3FXPMesh? mesh = meshResolver(part.MeshFileName);
            if (mesh == null || mesh.MaxNumVertices <= 0 || mesh.MaxNumIndices < 3)
                continue;

            var textures = new Texture2D?[Math.Max(part.TexNames.Count, 0)];
            for (int i = 0; i < textures.Length; i++)
                textures[i] = textureResolver(part, i);

            _parts.Add(new Part
            {
                Vertices = mesh.ColorVertices(),
                Indices = (ushort[])mesh.Indices.Clone(),
                Pivot = part.Pivot,
                Textures = textures,
                TexFps = part.TexFps,
            });
        }
    }

    /// <summary>The resolved, drawable shape parts (empty when nothing resolved).</summary>
    public IReadOnlyList<Part> Parts => _parts;
}
