using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Objects;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.2 pins: vertex conversion and the KO path resolver.</summary>
public class MeshGeometryTests
{
    [Fact]
    public void VertexConversion_MapsFieldsOneToOne()
    {
        N3VertexT1[] src =
        [
            new N3VertexT1
            {
                Position = new Vector3(1, 2, 3),
                Normal = new Vector3(0, 1, 0),
                Tu = 0.25f,
                Tv = 0.75f,
            },
        ];

        VertexPositionNormalTexture[] dst = MeshGeometry.ToXna(src);
        Assert.Equal(1f, dst[0].Position.X);
        Assert.Equal(3f, dst[0].Position.Z);
        Assert.Equal(1f, dst[0].Normal.Y);
        Assert.Equal(0.25f, dst[0].TextureCoordinate.X);
        Assert.Equal(0.75f, dst[0].TextureCoordinate.Y);
    }

    [Fact]
    public void IndexConversion_IsBitIdentical()
    {
        short[] indices = MeshGeometry.ToIndexBuffer([0, 1, 40000]);
        Assert.Equal(0, indices[0]);
        Assert.Equal(1, indices[1]);
        Assert.Equal(unchecked((short)40000), indices[2]);
        Assert.Equal(40000, (ushort)indices[2]); // round-trips
    }

    [Fact]
    public void KoPathResolver_ResolvesBackslashesAndCasing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ko-resolver-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Item"));
            File.WriteAllBytes(Path.Combine(root, "Item", "Mir_Fork.DXT"), [1]);

            var resolver = new KoPathResolver(root);
            string? resolved = resolver.Resolve(@"item\mir_fork.dxt");
            Assert.NotNull(resolved);
            Assert.EndsWith(Path.Combine("Item", "Mir_Fork.DXT"), resolved);

            Assert.Null(resolver.Resolve(@"item\missing.dxt"));
            Assert.Null(resolver.Resolve(string.Empty));

            // Cached second lookup returns the same result.
            Assert.Equal(resolved, resolver.Resolve(@"ITEM\MIR_FORK.dxt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
