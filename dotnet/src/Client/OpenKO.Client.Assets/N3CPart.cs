namespace OpenKO.Client.Assets;

/// <summary>Number of character LOD steps (MAX_CHR_LOD).</summary>
public static class N3ChrConstants
{
    public const int MaxChrLod = 4;
}

/// <summary>
/// Port of <c>CN3CPartSkins</c> (N3Chr.cpp) — the .n3cskins container:
/// exactly four LOD skins back to back after the name header.
/// </summary>
public sealed class N3CPartSkins : N3BaseFile
{
    public N3Skin[] Skins { get; } =
        [new N3Skin(), new N3Skin(), new N3Skin(), new N3Skin()];

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);
        foreach (N3Skin skin in Skins)
        {
            skin.FileFormatVersion = FileFormatVersion;
            skin.Load(reader);
        }
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);
        foreach (N3Skin skin in Skins)
            skin.Save(writer);
    }
}

/// <summary>
/// Port of <c>CN3CPart</c> (N3Chr.cpp) — the .n3cpart file: material plus
/// the texture and skins file names (resolved by the caller).
/// </summary>
public sealed class N3CPart : N3BaseFile
{
    public uint Reserved { get; set; }

    public N3Material Material { get; set; }

    public string TexFileName { get; set; } = string.Empty;

    public string SkinsFileName { get; set; } = string.Empty;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        Reserved = reader.ReadUInt32();
        Material = reader.ReadStruct<N3Material>();
        TexFileName = reader.ReadN3FileName();
        SkinsFileName = reader.ReadN3FileName();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(Reserved);
        writer.WriteStruct(Material);
        writer.WriteN3FileName(TexFileName);
        writer.WriteN3FileName(SkinsFileName);
    }
}
