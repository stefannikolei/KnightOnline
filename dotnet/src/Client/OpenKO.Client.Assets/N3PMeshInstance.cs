namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3PMeshInstance</c> as a pure data structure: one LOD view of a
/// <see cref="N3PMesh"/> with its own index buffer copy and the collapse walk
/// pointer. Starts at the lowest level of detail, exactly like the C++.
/// </summary>
public sealed class N3PMeshInstance
{
    private readonly N3PMesh _mesh;

    /// <summary>The walk position into <see cref="N3PMesh.Collapses"/> (m_pCollapseUpTo).</summary>
    private int _collapsePos;

    public N3PMeshInstance(N3PMesh mesh)
    {
        _mesh = mesh;
        Indices = (ushort[])mesh.Indices.Clone();
        NumVertices = mesh.MinNumVertices;
        NumIndices = mesh.MinNumIndices;
        _collapsePos = 0;
    }

    public N3PMesh Mesh => _mesh;

    /// <summary>This instance's index buffer (mutated by collapse/split).</summary>
    public ushort[] Indices { get; }

    public int NumVertices { get; private set; }

    public int NumIndices { get; private set; }

    /// <summary>
    /// CN3PMeshInstance::SetLODByNumVertices — splits/collapses toward the
    /// target, then keeps splitting while the current record says stopping
    /// here would leave holes (bShouldCollapse).
    /// </summary>
    public void SetLodByNumVertices(int numVertices)
    {
        if (_mesh.Collapses.Length == 0)
            return; // m_pCollapseUpTo == nullptr

        int diff = numVertices - NumVertices;
        if (diff > 0)
        {
            while (numVertices > NumVertices)
            {
                // Anti-flicker guard from the C++.
                if (_mesh.Collapses[_collapsePos].NumVerticesToLose + NumVertices > numVertices)
                    break;
                if (!SplitOne())
                    break;
            }
        }
        else if (diff < 0)
        {
            while (numVertices < NumVertices)
            {
                if (!CollapseOne())
                    break;
            }
        }

        while (_mesh.Collapses[_collapsePos].ShouldCollapse)
        {
            if (!SplitOne())
                break;
        }
    }

    /// <summary>
    /// CN3PMeshInstance::SetLOD — value is distance * FOV; maps through the
    /// LOD control table (or full detail when the mesh has none).
    /// </summary>
    public void SetLod(float value)
    {
        if (_mesh.LodCtrlValues.Length == 0)
        {
            SetLodByNumVertices(int.MaxValue);
            return;
        }

        N3PMesh.LodCtrlValue first = _mesh.LodCtrlValues[0];
        N3PMesh.LodCtrlValue last = _mesh.LodCtrlValues[^1];

        if (value < first.Distance)
        {
            SetLodByNumVertices(first.NumVertices);
        }
        else if (last.Distance < value)
        {
            SetLodByNumVertices(last.NumVertices);
        }
        else
        {
            for (int i = 1; i < _mesh.LodCtrlValues.Length; i++)
            {
                if (value < _mesh.LodCtrlValues[i].Distance)
                {
                    N3PMesh.LodCtrlValue hi = _mesh.LodCtrlValues[i];
                    N3PMesh.LodCtrlValue lo = _mesh.LodCtrlValues[i - 1];
                    float vertices = (hi.NumVertices - lo.NumVertices) *
                        (value - lo.Distance) / (hi.Distance - lo.Distance);
                    SetLodByNumVertices(lo.NumVertices + (int)vertices);
                    break;
                }
            }
        }
    }

    public bool CollapseOne()
    {
        if (_collapsePos <= 0)
            return false;

        _collapsePos--;
        ref readonly N3PMesh.EdgeCollapse c = ref _mesh.Collapses[_collapsePos];

        NumIndices -= c.NumIndicesToLose;
        for (int i = c.IndexChangesOffset; i < c.IndexChangesOffset + c.NumIndicesToChange; i++)
            Indices[_mesh.AllIndexChanges[i]] = (ushort)c.CollapseTo;

        NumVertices -= c.NumVerticesToLose;
        return true;
    }

    public bool SplitOne()
    {
        // The C++ deliberately allows the pointer to end up ON the sentinel
        // record (one past the last real collapse) so the final polygons get
        // drawn; the sentinel is zeroed so reading it is harmless.
        if (_collapsePos >= _mesh.NumCollapses)
            return false;

        ref readonly N3PMesh.EdgeCollapse c = ref _mesh.Collapses[_collapsePos];
        NumIndices += c.NumIndicesToLose;
        NumVertices += c.NumVerticesToLose;

        if (_mesh.AllIndexChanges.Length > 0)
        {
            for (int i = c.IndexChangesOffset; i < c.IndexChangesOffset + c.NumIndicesToChange; i++)
                Indices[_mesh.AllIndexChanges[i]] = (ushort)(NumVertices - 1);
        }

        _collapsePos++;
        return true;
    }
}
