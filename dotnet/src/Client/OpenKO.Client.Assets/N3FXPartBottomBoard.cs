namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPartBottomBoard</c> (Client/N3Base/N3FXPartBottomBoard.cpp) —
/// a ground-hugging fan part (FX_PART_TYPE_BOTTOMBOARD). Load-time fields only.
/// </summary>
public sealed class N3FXPartBottomBoard : N3FXPartBase
{
    /// <summary>SUPPORTED_PART_VERSION for bottom-boards.</summary>
    public const int SupportedPartVersion = 3;

    public float SizeX { get; set; } = 1f;

    public float SizeZ { get; set; } = 1f;

    public float ScaleVelX { get; set; }

    public float ScaleVelZ { get; set; }

    public bool TexLoop { get; set; }

    // Version >= 1
    public float Gap { get; set; }

    // Version >= 2
    public bool NewUv { get; set; }

    // Version >= 3
    public bool HdrUv { get; set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        SizeX = reader.ReadSingle();
        SizeZ = reader.ReadSingle();

        ScaleVelX = reader.ReadSingle();
        ScaleVelZ = reader.ReadSingle();

        TexLoop = reader.ReadByte() != 0;

        if (Version >= 1)
            Gap = reader.ReadSingle();

        if (Version >= 2)
            NewUv = reader.ReadByte() != 0;

        if (Version >= 3)
            HdrUv = reader.ReadByte() != 0;
    }

    /// <summary>
    /// Save mirror — symmetric with <see cref="Load"/>. (The C++ Save always
    /// writes the version-1 gap and stops there.)
    /// </summary>
    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(SizeX);
        writer.Write(SizeZ);

        writer.Write(ScaleVelX);
        writer.Write(ScaleVelZ);

        writer.Write(TexLoop ? (byte)1 : (byte)0);

        if (Version >= 1)
            writer.Write(Gap);

        if (Version >= 2)
            writer.Write(NewUv ? (byte)1 : (byte)0);

        if (Version >= 3)
            writer.Write(HdrUv ? (byte)1 : (byte)0);
    }
}
