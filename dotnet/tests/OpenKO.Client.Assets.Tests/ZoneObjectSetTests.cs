using OpenKO.Client.Assets.Zones;
using Xunit;

namespace OpenKO.Client.Assets.Tests;

/// <summary>Corpus pins for the .opd zone-object reader (CN3ShapeMgr::Load).</summary>
public class ZoneObjectSetTests
{
    [Fact]
    [Trait("Category", "Corpus")]
    public void RealOpdFiles_ParseToCompletion()
    {
        if (AssetCorpus.Root == null)
            return;

        string[] files = Directory.EnumerateFiles(
            Path.Combine(AssetCorpus.Root, "Zones"), "*.opd",
            new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).ToArray();

        if (files.Length == 0)
            return;

        int totalObjects = 0;
        foreach (string path in files)
        {
            using FileStream stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var set = new ZoneObjectSet();
            set.Load(reader);

            // The whole file must be consumed — a byte-exact parse (the strongest
            // check that the collision block + every shape read the right length).
            Assert.True(stream.Position == stream.Length,
                $"{Path.GetFileName(path)}: parsed {stream.Position}/{stream.Length} bytes");

            // Every placed object carries a shape; its transform matrix is finite.
            Assert.All(set.Objects, o =>
            {
                Assert.NotNull(o.Shape);
                Assert.True(float.IsFinite(o.Shape.Position.X));
            });

            totalObjects += set.Objects.Count;
        }

        // The town/field zones place many objects (buildings, trees, gates).
        Assert.True(totalObjects > 0, "no zone objects parsed across the corpus");
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void Moradon_HasNamedHeaderAndObjects()
    {
        if (AssetCorpus.Root == null)
            return;

        string path = Path.Combine(AssetCorpus.Root, "Zones", "moradon.opd");
        if (!File.Exists(path))
            return;

        var set = ZoneObjectSet.LoadFromFile(path);
        Assert.Equal("moradon", set.Name, ignoreCase: true);
        Assert.NotEmpty(set.Objects);
    }
}
