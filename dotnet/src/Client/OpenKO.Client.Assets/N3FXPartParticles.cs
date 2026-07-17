using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPartParticles</c> (Client/N3Base/N3FXPartParticles.cpp) —
/// the particle-emitter part. Only the load-time emitter description is modelled;
/// the per-frame particle pool / sim (m_pVBList_Alive, CreateParticles, Tick …)
/// is deferred to slice 9.10b.
/// </summary>
public sealed class N3FXPartParticles : N3FXPartBase
{
    /// <summary>SUPPORTED_PART_VERSION — highest particle version the reader understands.</summary>
    public const int SupportedPartVersion = 11;

    /// <summary>m_iNumParticle — the maximum particle count.</summary>
    public int NumParticle { get; set; }

    /// <summary>m_pair_fParticleSize.first — particle size range minimum.</summary>
    public float ParticleSizeMin { get; set; }

    /// <summary>m_pair_fParticleSize.second — particle size range maximum.</summary>
    public float ParticleSizeMax { get; set; }

    /// <summary>m_pair_fParticleLife.first — particle life range minimum.</summary>
    public float ParticleLifeMin { get; set; }

    /// <summary>m_pair_fParticleLife.second — particle life range maximum.</summary>
    public float ParticleLifeMax { get; set; }

    public Vector3 MinCreateRange { get; set; }

    public Vector3 MaxCreateRange { get; set; }

    public float CreateDelay { get; set; } = 0.01f;

    public int NumCreate { get; set; } = 1;

    /// <summary>m_dwEmitType — e_FXPartParticleEmitType.</summary>
    public FxPartParticleEmitType EmitType { get; set; } = FxPartParticleEmitType.Normal;

    /// <summary>m_uEmitCon — the emit condition union (only the field matching EmitType is serialized).</summary>
    public ParticleEmitCondition EmitCondition { get; set; }

    public Vector3 PtEmitDir { get; set; } = new(0f, 0f, -1f);

    public float PtVelocity { get; set; }

    public float PtAccel { get; set; }

    public float PtRotVelocity { get; set; }

    public float PtGravity { get; set; }

    /// <summary>m_bChangeColor — whether the color-key table is present.</summary>
    public bool ChangeColor { get; set; }

    /// <summary>
    /// The number of color keys actually stored on disk (iNumKeyColor). The C++
    /// Save always emits NUM_KEY_COLOR (100); Load reads whatever count precedes
    /// the block into the fixed 100-entry array.
    /// </summary>
    public int ChangeColorKeyCount { get; set; } = N3FxDef.NumKeyColor;

    /// <summary>m_dwChangeColor[NUM_KEY_COLOR] — the color-key table (D3DCOLOR/ARGB).</summary>
    public uint[] ChangeColors { get; } = CreateDefaultColors();

    /// <summary>m_bAnimKey — whether an FX shape drives the emitter.</summary>
    public bool AnimKey { get; set; }

    /// <summary>m_fMeshFPS — shape animation speed (AnimKey only).</summary>
    public float MeshFps { get; set; } = 30f;

    /// <summary>The referenced FX shape file name — a fixed 260-byte field (AnimKey only).</summary>
    public byte[] ShapeFileNameBytes { get; set; } = new byte[N3FxDef.MaxPath];

    public string ShapeFileName
    {
        get => N3BinaryIo.DecodeFixedString(ShapeFileNameBytes);
        set => ShapeFileNameBytes = N3BinaryIo.EncodeFixedString(value, N3FxDef.MaxPath);
    }

    // Version >= 5
    public float TexRotateVelocity { get; set; }

    public float ScaleVelX { get; set; }

    public float ScaleVelY { get; set; }

    // Version >= 6
    public bool DistanceNumFix { get; set; }

    // Version >= 7
    public bool ParticleYAxisFix { get; set; }

    // Version >= 8
    public bool ParticleNotRotate { get; set; }

    public Vector3 ParticleNotRotateAxis { get; set; }

    // Version >= 9
    public float PtRangeMin { get; set; }

    public float PtRangeMax { get; set; }

    /// <summary>The 5 bytes the C++ skips for version &gt;= 10. Kept raw for round trips.</summary>
    public byte[] Version10Unknown { get; set; } = new byte[5];

    /// <summary>The 12 bytes the C++ skips for version &gt;= 11. Kept raw for round trips.</summary>
    public byte[] Version11Unknown { get; set; } = new byte[12];

    private static uint[] CreateDefaultColors()
    {
        var colors = new uint[N3FxDef.NumKeyColor];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = 0xffffffff;
        return colors;
    }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        NumParticle = reader.ReadInt32();

        if (Version < 4)
        {
            float size = reader.ReadSingle();
            ParticleSizeMin = size;
            ParticleSizeMax = size;
        }
        else
        {
            ParticleSizeMin = reader.ReadSingle();
            ParticleSizeMax = reader.ReadSingle();
        }

        ParticleLifeMin = reader.ReadSingle();
        ParticleLifeMax = reader.ReadSingle();

        MinCreateRange = reader.ReadVector3();
        MaxCreateRange = reader.ReadVector3();

        CreateDelay = reader.ReadSingle();
        NumCreate = reader.ReadInt32();

        EmitType = (FxPartParticleEmitType)reader.ReadUInt32();

        var cond = default(ParticleEmitCondition);
        if (EmitType == FxPartParticleEmitType.Spread)
        {
            cond.EmitAngle = reader.ReadSingle();
        }
        else if (EmitType == FxPartParticleEmitType.Gather)
        {
            cond.GatherPoint = reader.ReadVector3();
        }

        EmitCondition = cond;

        PtEmitDir = reader.ReadVector3();
        PtVelocity = reader.ReadSingle();
        PtAccel = reader.ReadSingle();
        PtRotVelocity = reader.ReadSingle();
        PtGravity = reader.ReadSingle();

        ChangeColor = reader.ReadByte() != 0;
        if (ChangeColor)
        {
            ChangeColorKeyCount = reader.ReadInt32();
            for (int i = 0; i < ChangeColorKeyCount; i++)
            {
                uint color = reader.ReadUInt32();
                if (i < ChangeColors.Length)
                    ChangeColors[i] = color;
            }
        }

        AnimKey = reader.ReadByte() != 0;
        if (AnimKey)
        {
            MeshFps = reader.ReadSingle();
            ShapeFileNameBytes = reader.ReadFixedBytes(N3FxDef.MaxPath);
        }

        if (Version >= 5)
        {
            TexRotateVelocity = reader.ReadSingle();
            ScaleVelX = reader.ReadSingle();
            ScaleVelY = reader.ReadSingle();
        }

        if (Version >= 6)
            DistanceNumFix = reader.ReadByte() != 0;

        if (Version >= 7)
            ParticleYAxisFix = reader.ReadByte() != 0;

        if (Version >= 8)
        {
            ParticleNotRotate = reader.ReadByte() != 0;
            ParticleNotRotateAxis = reader.ReadVector3();
        }

        if (Version >= 9)
        {
            PtRangeMin = reader.ReadSingle();
            PtRangeMax = reader.ReadSingle();
        }

        if (Version >= 10)
            Version10Unknown = reader.ReadFixedBytes(5);

        if (Version >= 11)
            Version11Unknown = reader.ReadFixedBytes(12);
    }

    /// <summary>
    /// Save mirror — symmetric with <see cref="Load"/> across every version so
    /// tests round-trip. (The C++ Save only writes up to the version-5 layout.)
    /// </summary>
    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(NumParticle);

        if (Version < 4)
        {
            writer.Write(ParticleSizeMin);
        }
        else
        {
            writer.Write(ParticleSizeMin);
            writer.Write(ParticleSizeMax);
        }

        writer.Write(ParticleLifeMin);
        writer.Write(ParticleLifeMax);

        writer.Write(MinCreateRange);
        writer.Write(MaxCreateRange);

        writer.Write(CreateDelay);
        writer.Write(NumCreate);

        writer.Write((uint)EmitType);

        if (EmitType == FxPartParticleEmitType.Spread)
        {
            writer.Write(EmitCondition.EmitAngle);
        }
        else if (EmitType == FxPartParticleEmitType.Gather)
        {
            writer.Write(EmitCondition.GatherPoint);
        }

        writer.Write(PtEmitDir);
        writer.Write(PtVelocity);
        writer.Write(PtAccel);
        writer.Write(PtRotVelocity);
        writer.Write(PtGravity);

        writer.Write(ChangeColor ? (byte)1 : (byte)0);
        if (ChangeColor)
        {
            writer.Write(ChangeColorKeyCount);
            for (int i = 0; i < ChangeColorKeyCount; i++)
                writer.Write(i < ChangeColors.Length ? ChangeColors[i] : 0xffffffff);
        }

        writer.Write(AnimKey ? (byte)1 : (byte)0);
        if (AnimKey)
        {
            writer.Write(MeshFps);
            writer.WriteFixedBytes(ShapeFileNameBytes, N3FxDef.MaxPath);
        }

        if (Version >= 5)
        {
            writer.Write(TexRotateVelocity);
            writer.Write(ScaleVelX);
            writer.Write(ScaleVelY);
        }

        if (Version >= 6)
            writer.Write(DistanceNumFix ? (byte)1 : (byte)0);

        if (Version >= 7)
            writer.Write(ParticleYAxisFix ? (byte)1 : (byte)0);

        if (Version >= 8)
        {
            writer.Write(ParticleNotRotate ? (byte)1 : (byte)0);
            writer.Write(ParticleNotRotateAxis);
        }

        if (Version >= 9)
        {
            writer.Write(PtRangeMin);
            writer.Write(PtRangeMax);
        }

        if (Version >= 10)
            writer.WriteFixedBytes(Version10Unknown, 5);

        if (Version >= 11)
            writer.WriteFixedBytes(Version11Unknown, 12);
    }
}
