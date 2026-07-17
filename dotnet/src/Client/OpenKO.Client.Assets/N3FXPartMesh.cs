using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPartMesh</c> (Client/N3Base/N3FXPartMesh.cpp) — a part that
/// draws an animated FX shape (FX_PART_TYPE_MESH). Load-time fields only; the
/// referenced shape is named here and resolved by the FX manager later.
/// </summary>
public sealed class N3FXPartMesh : N3FXPartBase
{
    /// <summary>SUPPORTED_PART_VERSION for mesh parts.</summary>
    public const int SupportedPartVersion = 9;

    /// <summary>The FX shape file name — a fixed 260-byte field read right after the base header.</summary>
    public byte[] ShapeFileNameBytes { get; set; } = new byte[N3FxDef.MaxPath];

    public string ShapeFileName
    {
        get => N3BinaryIo.DecodeFixedString(ShapeFileNameBytes);
        set => ShapeFileNameBytes = N3BinaryIo.EncodeFixedString(value, N3FxDef.MaxPath);
    }

    /// <summary>m_cTextureMoveDir — 0=none 1=up 2=down 3=left 4=right (char, 1 byte).</summary>
    public byte TextureMoveDir { get; set; }

    public float TexU { get; set; }

    public float TexV { get; set; }

    public Vector3 ScaleVelocity { get; set; }

    // Version >= 2
    public bool TexLoop { get; set; }

    // Version >= 3
    public Vector3 ScaleAcceleration { get; set; }

    // Version >= 4
    public float MeshFps { get; set; } = 30f;

    // Version >= 5
    public Vector3 UnitScale { get; set; } = Vector3.One;

    // Version >= 6
    public bool ShapeLoop { get; set; }

    // Version >= 7
    public bool ViewFix { get; set; }

    // Version >= 8
    public bool UseFadeShowLife { get; set; }

    /// <summary>The 260 bytes the C++ skips for version &gt;= 9. Kept raw for round trips.</summary>
    public byte[] Version9Unknown { get; set; } = new byte[N3FxDef.MaxPath];

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        ShapeFileNameBytes = reader.ReadFixedBytes(N3FxDef.MaxPath);

        TextureMoveDir = reader.ReadByte();
        TexU = reader.ReadSingle();
        TexV = reader.ReadSingle();
        ScaleVelocity = reader.ReadVector3();

        if (Version >= 2)
            TexLoop = reader.ReadByte() != 0;

        if (Version >= 3)
            ScaleAcceleration = reader.ReadVector3();

        if (Version >= 4)
            MeshFps = reader.ReadSingle();

        if (Version >= 5)
            UnitScale = reader.ReadVector3();

        if (Version >= 6)
            ShapeLoop = reader.ReadByte() != 0;

        if (Version >= 7)
            ViewFix = reader.ReadByte() != 0;

        if (Version >= 8)
            UseFadeShowLife = reader.ReadByte() != 0;

        if (Version >= 9)
            Version9Unknown = reader.ReadFixedBytes(N3FxDef.MaxPath);
    }

    /// <summary>
    /// Save mirror — symmetric with <see cref="Load"/> for every version. (The
    /// C++ Save only writes up to the version-5 layout.)
    /// </summary>
    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.WriteFixedBytes(ShapeFileNameBytes, N3FxDef.MaxPath);

        writer.Write(TextureMoveDir);
        writer.Write(TexU);
        writer.Write(TexV);
        writer.Write(ScaleVelocity);

        if (Version >= 2)
            writer.Write(TexLoop ? (byte)1 : (byte)0);

        if (Version >= 3)
            writer.Write(ScaleAcceleration);

        if (Version >= 4)
            writer.Write(MeshFps);

        if (Version >= 5)
            writer.Write(UnitScale);

        if (Version >= 6)
            writer.Write(ShapeLoop ? (byte)1 : (byte)0);

        if (Version >= 7)
            writer.Write(ViewFix ? (byte)1 : (byte)0);

        if (Version >= 8)
            writer.Write(UseFadeShowLife ? (byte)1 : (byte)0);

        if (Version >= 9)
            writer.WriteFixedBytes(Version9Unknown, N3FxDef.MaxPath);
    }
}
