using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenKO.Client.Engine.Terrain;

/// <summary>
/// The river render vertex: position, diffuse colour and two texture-coordinate
/// sets (the animated caustic frame + the wave overlay). The source
/// __VertexRiver also carries a normal, but the river pipeline is unlit, so the
/// device vertex drops it. Matches what DualTextureEffect consumes.
/// </summary>
public struct RiverVertex : IVertexType
{
    public Vector3 Position;
    public Color Color;
    public Vector2 TexCoord0;
    public Vector2 TexCoord1;

    public RiverVertex(Vector3 position, Color color, Vector2 texCoord0, Vector2 texCoord1)
    {
        Position = position;
        Color = color;
        TexCoord0 = texCoord0;
        TexCoord1 = texCoord1;
    }

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));

    readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
