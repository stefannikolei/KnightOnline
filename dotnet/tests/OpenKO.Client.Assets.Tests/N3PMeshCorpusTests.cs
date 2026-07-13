using OpenKO.Client.Assets;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>
/// Full-corpus scan: every .n3pmesh in Client/Data must parse, consume the
/// file exactly, and carry a consistent progressive-mesh structure.
/// </summary>
public class N3PMeshCorpusTests
{
    [Fact]
    [Trait("Category", "Corpus")]
    public void EveryPMeshInCorpus_ParsesAndConsumesWholeFile()
    {
        if (AssetCorpus.Root is null)
            return; // Client/Data submodule not checked out (e.g. CI)

        var failures = new List<string>();
        int count = 0;

        foreach (string path in AssetCorpus.EnumerateFiles("*.n3pmesh"))
        {
            count++;
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                var mesh = new N3PMesh();
                mesh.Load(reader);

                if (stream.Position != stream.Length)
                {
                    failures.Add($"{path}: {stream.Length - stream.Position} trailing bytes");
                    continue;
                }

                // Structural sanity: the LOD walk must stay inside the arrays.
                if (mesh.MinNumVertices > mesh.MaxNumVertices || mesh.MinNumIndices > mesh.MaxNumIndices)
                    failures.Add($"{path}: min > max ({mesh.MinNumVertices}/{mesh.MaxNumVertices})");
                for (int i = 0; i < mesh.NumCollapses; i++)
                {
                    N3PMesh.EdgeCollapse c = mesh.Collapses[i];
                    if (c.NumIndicesToChange > 0 &&
                        c.IndexChangesOffset + c.NumIndicesToChange > mesh.AllIndexChanges.Length)
                    {
                        failures.Add($"{path}: collapse {i} reaches past AllIndexChanges");
                        break;
                    }
                }

                // The LOD walk itself must not throw and must land at max detail.
                var instance = new N3PMeshInstance(mesh);
                instance.SetLodByNumVertices(int.MaxValue);
                if (instance.NumVertices > mesh.MaxNumVertices || instance.NumIndices > mesh.MaxNumIndices)
                    failures.Add($"{path}: LOD walk exceeded max ({instance.NumVertices}/{instance.NumIndices})");
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count > 25)
                break;
        }

        Assert.True(count > 3000, $"Corpus scan found only {count} .n3pmesh files — checkout incomplete?");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {count} .n3pmesh files failed:\n{string.Join('\n', failures)}");
    }
}
