using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenKO.Client.Engine.Terrain;

/// <summary>
/// The __VertexT2 / FVF_VNT2 layout used by the level-1 terrain patches:
/// position, normal and two texture-coordinate sets (tile texture + tile
/// blend / colormap). Constructing the <see cref="VertexDeclaration"/> needs
/// no GraphicsDevice, so this stays usable from the headless pure layer.
/// </summary>
public struct TerrainVertex : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord0;
    public Vector2 TexCoord1;

    public TerrainVertex(Vector3 position, Vector3 normal, Vector2 texCoord0, Vector2 texCoord1)
    {
        Position = position;
        Normal = normal;
        TexCoord0 = texCoord0;
        TexCoord1 = texCoord1;
    }

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(32, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));

    readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
