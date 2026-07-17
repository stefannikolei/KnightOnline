namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPMeshInstance</c> (Client/N3Base/N3FXPMeshInstance.cpp) — a
/// per-use instance of an <see cref="N3FXPMesh"/>. It is never serialized; it is
/// created from a mesh (<see cref="Create"/>) and copies the mesh's colored
/// vertex buffer + index buffer, starting at the minimum LOD.
/// <para>
/// The LOD walking (SetLOD / CollapseOne / SplitOne / Render) is deferred to the
/// rendering slice 9.10c — only the data initialization is modelled here.
/// </para>
/// </summary>
public sealed class N3FXPMeshInstance
{
    /// <summary>The per-instance colored vertex buffer (copied from the mesh).</summary>
    public N3VertexXyzColorT1[] ColorVertices { get; private set; } = [];

    /// <summary>The per-instance index buffer (copied from the mesh).</summary>
    public ushort[] Indices { get; private set; } = [];

    /// <summary>m_iNumVertices — vertices in use right now (starts at the min LOD).</summary>
    public int NumVertices { get; private set; }

    /// <summary>m_iNumIndices — indices in use right now (starts at the min LOD).</summary>
    public int NumIndices { get; private set; }

    /// <summary>The mesh this instance references.</summary>
    public N3FXPMesh? Mesh { get; private set; }

    /// <summary>CN3FXPMeshInstance::Create(CN3FXPMesh*) — copies the buffers and sets the min LOD.</summary>
    public bool Create(N3FXPMesh? mesh)
    {
        if (mesh == null)
        {
            Release();
            return false;
        }

        Mesh = mesh;

        ColorVertices = mesh.MaxNumVertices > 0 ? mesh.ColorVertices() : [];
        Indices = mesh.MaxNumIndices > 0 ? (ushort[])mesh.Indices.Clone() : [];

        // Lowest level of detail to start with.
        NumVertices = mesh.MinNumVertices;
        NumIndices = mesh.MinNumIndices;

        return true;
    }

    public void Release()
    {
        ColorVertices = [];
        Indices = [];
        NumVertices = 0;
        NumIndices = 0;
        Mesh = null;
    }

    /// <summary>Sets every in-use vertex's color (CN3FXPMeshInstance::SetColor).</summary>
    public void SetColor(uint color = 0xffffffff)
    {
        for (int i = 0; i < NumVertices && i < ColorVertices.Length; i++)
            ColorVertices[i].Color = color;
    }
}
