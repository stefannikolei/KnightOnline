using OpenKO.IO;

namespace OpenKO.N3;

/// <summary>
/// Port of the C++ <c>CN3UIImage</c> (Client/N3Base/N3UIImage.cpp) — a textured UI quad. Headless
/// port of the file format: the texture file name, the UV rectangle and the animation frame rate.
/// The actual texture object and vertex buffer are built by the renderer from this data.
///
/// After the base <see cref="N3UIBase"/> data:
/// <code>
///   int32 texNameLen; byte texName[texNameLen]
///   FLOAT_RECT uvRect   // 4 floats: left, top, right, bottom
///   float animFrame     // images drawn per second (for animated images)
/// </code>
/// </summary>
public class N3UIImage : N3UIBase
{
    public N3UIImage()
    {
        Type = UiType.Image;
    }

    /// <summary>Texture file name referenced by this image (port of <c>m_szTexFN</c>).</summary>
    public string TextureFileName { get; private set; } = string.Empty;

    /// <summary>UV rectangle into the texture (port of <c>m_frcUVRect</c>).</summary>
    public FloatRect UvRect { get; private set; }

    /// <summary>Frames per second for animated images (port of <c>m_fAnimFrame</c>).</summary>
    public float AnimFrame { get; private set; }

    public override void Release()
    {
        base.Release();
        TextureFileName = string.Empty;
        UvRect = default;
        AnimFrame = 0;
    }

    public override bool Load(IFile file)
    {
        if (!base.Load(file))
            return false;

        var reader = (FileReader)file;

        int texLen = reader.ReadInt32();
        if (texLen > 0)
            TextureFileName = reader.ReadFixedString(texLen);

        UvRect = new FloatRect(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());

        AnimFrame = reader.ReadSingle();

        return true;
    }
}
