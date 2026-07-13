using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Device-side companion of <see cref="N3PMeshInstance"/>: keeps the
/// converted vertex array and draws the instance's current LOD window with
/// DrawUserIndexedPrimitives — the CN3PMeshInstance::Render equivalent (the
/// C++ 1000-primitive chunking is unnecessary without the *UP limits).
/// </summary>
public sealed class PMeshInstanceRenderer
{
    private readonly N3PMeshInstance _instance;
    private readonly VertexPositionNormalTexture[] _vertices;
    private short[] _indices;
    private bool _indicesDirty;

    public PMeshInstanceRenderer(N3PMesh mesh)
    {
        _instance = new N3PMeshInstance(mesh);
        _vertices = MeshGeometry.ToXna(mesh.Vertices);
        _indices = MeshGeometry.ToIndexBuffer(_instance.Indices);
    }

    public N3PMeshInstance Instance => _instance;

    /// <summary>CN3PMeshInstance::SetLOD(distance * FOV).</summary>
    public void SetLod(float lodValue)
    {
        int before = _instance.NumVertices;
        _instance.SetLod(lodValue);
        if (_instance.NumVertices != before)
            _indicesDirty = true;
    }

    public void SetLodByNumVertices(int numVertices)
    {
        int before = _instance.NumVertices;
        _instance.SetLodByNumVertices(numVertices);
        if (_instance.NumVertices != before)
            _indicesDirty = true;
    }

    public void Draw(GraphicsDevice device)
    {
        if (_instance.NumIndices < 3 || _vertices.Length == 0)
            return;

        if (_indicesDirty)
        {
            _indices = MeshGeometry.ToIndexBuffer(_instance.Indices);
            _indicesDirty = false;
        }

        device.DrawUserIndexedPrimitives(
            PrimitiveType.TriangleList,
            _vertices, 0, _instance.NumVertices,
            _indices, 0, _instance.NumIndices / 3);
    }
}
