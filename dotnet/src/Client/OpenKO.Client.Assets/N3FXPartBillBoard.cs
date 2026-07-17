using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPartBillBoard</c> (Client/N3Base/N3FXPartBillBoard.cpp) — a
/// camera-facing quad part (FX_PART_TYPE_BOARD). Load-time fields only.
/// </summary>
public sealed class N3FXPartBillBoard : N3FXPartBase
{
    /// <summary>SUPPORTED_PART_VERSION for billboards.</summary>
    public const int SupportedPartVersion = 9;

    /// <summary>m_iNum — number of boards.</summary>
    public int Num { get; set; } = 1;

    public float SizeX { get; set; } = 1f;

    public float SizeY { get; set; } = 1f;

    public bool TexLoop { get; set; }

    public float Radius { get; set; }

    // Version >= 3
    public bool RotateOnlyY { get; set; }

    // Version >= 4
    public float ScaleVelX { get; set; }

    public float ScaleVelY { get; set; }

    public float ScaleAccelX { get; set; }

    public float ScaleAccelY { get; set; }

    /// <summary>m_mtxRot — rotation matrix (version &gt;= 5).</summary>
    public Matrix4x4 RotationMatrix { get; set; } = Matrix4x4.Identity;

    // Version >= 6
    public bool OnScreen { get; set; }

    // Version >= 7
    public bool RotationRate { get; set; }

    /// <summary>The 13 bytes the C++ skips for version &gt;= 8. Kept raw for round trips.</summary>
    public byte[] Version8Unknown { get; set; } = new byte[13];

    /// <summary>The 12 bytes the C++ skips for version &gt;= 9. Kept raw for round trips.</summary>
    public byte[] Version9Unknown { get; set; } = new byte[12];

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        Num = reader.ReadInt32();
        SizeX = reader.ReadSingle();
        SizeY = reader.ReadSingle();

        TexLoop = reader.ReadByte() != 0;
        Radius = reader.ReadSingle();

        if (Version >= 3)
            RotateOnlyY = reader.ReadByte() != 0;

        if (Version >= 4)
        {
            ScaleVelX = reader.ReadSingle();
            ScaleVelY = reader.ReadSingle();
            ScaleAccelX = reader.ReadSingle();
            ScaleAccelY = reader.ReadSingle();
        }

        if (Version >= 5)
            RotationMatrix = reader.ReadMatrix4x4();

        if (Version >= 6)
            OnScreen = reader.ReadByte() != 0;

        if (Version >= 7)
            RotationRate = reader.ReadByte() != 0;

        if (Version >= 8)
            Version8Unknown = reader.ReadFixedBytes(13);

        if (Version >= 9)
            Version9Unknown = reader.ReadFixedBytes(12);
    }

    /// <summary>
    /// Save mirror — symmetric with <see cref="Load"/> for every version. (The
    /// C++ Save only writes up to the version-5 layout.)
    /// </summary>
    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(Num);
        writer.Write(SizeX);
        writer.Write(SizeY);

        writer.Write(TexLoop ? (byte)1 : (byte)0);
        writer.Write(Radius);

        if (Version >= 3)
            writer.Write(RotateOnlyY ? (byte)1 : (byte)0);

        if (Version >= 4)
        {
            writer.Write(ScaleVelX);
            writer.Write(ScaleVelY);
            writer.Write(ScaleAccelX);
            writer.Write(ScaleAccelY);
        }

        if (Version >= 5)
            writer.Write(RotationMatrix);

        if (Version >= 6)
            writer.Write(OnScreen ? (byte)1 : (byte)0);

        if (Version >= 7)
            writer.Write(RotationRate ? (byte)1 : (byte)0);

        if (Version >= 8)
            writer.WriteFixedBytes(Version8Unknown, 13);

        if (Version >= 9)
            writer.WriteFixedBytes(Version9Unknown, 12);
    }
}
