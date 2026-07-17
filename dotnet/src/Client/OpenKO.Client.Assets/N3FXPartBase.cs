using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXPartBase</c> (Client/N3Base/N3FXPartBase.cpp) — the shared
/// header every effect part starts with: two version bytes, life, kinematics,
/// the texture-set reference and the blend/render state.
/// <para>
/// Although the C++ class derives from CN3BaseFileAccess, its <c>Load</c> does
/// NOT call the base loader — there is no [len][name] header. So this is a plain
/// class with its own <see cref="Load"/> / <see cref="Save"/>. The four concrete
/// parts extend it and read their extra fields after <see cref="Load"/>.
/// </para>
/// <para>
/// Simulation/rendering fields (m_dwState, m_vCurrPos, the resolved textures …)
/// are deferred to slices 9.10b/9.10c; only load-time data is modelled here.
/// </para>
/// </summary>
public class N3FXPartBase
{
    /// <summary>SUPPORTED_PART_BASE_VERSION — the highest base version the reader understands.</summary>
    public const int SupportedPartBaseVersion = 4;

    /// <summary>m_iVersion — the concrete part subclass version (1 byte on disk).</summary>
    public int Version { get; set; }

    /// <summary>m_iBaseVersion — the shared header version (1 byte on disk).</summary>
    public int BaseVersion { get; set; } = SupportedPartBaseVersion;

    /// <summary>m_iType — e_FXPartType (1 byte on disk). Set by the bundle before Load.</summary>
    public FxPartType Type { get; set; } = FxPartType.None;

    /// <summary>m_fLife — play time in seconds (clamped to 10.0 on load).</summary>
    public float Life { get; set; }

    /// <summary>
    /// The two unknown ints read only when BaseVersion &gt;= 3 (iIDK0/iIDK1 in the
    /// C++, discarded there). Kept for byte-exact round trips.
    /// </summary>
    public int BaseVersion3Unknown0 { get; set; }

    public int BaseVersion3Unknown1 { get; set; }

    public Vector3 Velocity { get; set; }

    public Vector3 Acceleration { get; set; }

    public Vector3 RotVelocity { get; set; }

    /// <summary>m_bOnGround (1 byte).</summary>
    public bool OnGround { get; set; }

    /// <summary>m_vPos — the part's position within the bundle.</summary>
    public Vector3 Pos { get; set; }

    /// <summary>m_iNumTex — number of animation-frame textures.</summary>
    public int NumTex { get; set; }

    /// <summary>m_fTexFPS — texture animation speed.</summary>
    public float TexFps { get; set; } = 30f;

    /// <summary>m_pTexName — the raw fixed char[MAX_PATH] texture base name.</summary>
    public byte[] TexNameBytes { get; set; } = new byte[N3FxDef.MaxPath];

    /// <summary>m_pTexName decoded up to the first NUL.</summary>
    public string TexName
    {
        get => N3BinaryIo.DecodeFixedString(TexNameBytes);
        set => TexNameBytes = N3BinaryIo.EncodeFixedString(value, N3FxDef.MaxPath);
    }

    /// <summary>m_bAlpha (BOOL when BaseVersion &lt; 2; derived from the render flags otherwise).</summary>
    public bool Alpha { get; set; } = true;

    /// <summary>m_dwSrcBlend — D3DBLEND.</summary>
    public uint SrcBlend { get; set; } = 2; // D3DBLEND_ONE

    /// <summary>m_dwDestBlend — D3DBLEND.</summary>
    public uint DestBlend { get; set; } = 2; // D3DBLEND_ONE

    public float FadeOut { get; set; }

    public float FadeIn { get; set; }

    /// <summary>m_dwRenderFlag — RF_* bits (BaseVersion &gt;= 2 only).</summary>
    public uint RenderFlag { get; set; }

    /// <summary>
    /// The shape_hdrname field read (skipped) only when BaseVersion &gt;= 4 —
    /// a fixed 260-byte block. Kept raw for byte-exact round trips.
    /// </summary>
    public byte[] BaseVersion4ShapeHeaderName { get; set; } = new byte[N3FxDef.MaxPath];

    /// <summary>CN3FXPartBase::Load — no name header; reads the two version bytes first.</summary>
    public virtual void Load(BinaryReader reader)
    {
        Version = reader.ReadByte();
        BaseVersion = reader.ReadByte();

        Life = reader.ReadSingle();
        if (Life > 10f)
            Life = 10f;

        if (BaseVersion >= 3)
        {
            BaseVersion3Unknown0 = reader.ReadInt32();
            BaseVersion3Unknown1 = reader.ReadInt32();
        }

        Type = (FxPartType)reader.ReadByte();

        Velocity = reader.ReadVector3();
        Acceleration = reader.ReadVector3();
        RotVelocity = reader.ReadVector3();

        OnGround = reader.ReadByte() != 0;

        Pos = reader.ReadVector3();

        NumTex = reader.ReadInt32();
        TexFps = reader.ReadSingle();
        TexNameBytes = reader.ReadFixedBytes(N3FxDef.MaxPath);

        if (BaseVersion < 2)
        {
            Alpha = reader.ReadInt32() != 0; // BOOL, 4 bytes
            SrcBlend = reader.ReadUInt32();
            DestBlend = reader.ReadUInt32();
            FadeOut = reader.ReadSingle();
            FadeIn = reader.ReadSingle();
        }
        else
        {
            SrcBlend = reader.ReadUInt32();
            DestBlend = reader.ReadUInt32();
            FadeOut = reader.ReadSingle();
            FadeIn = reader.ReadSingle();
            RenderFlag = reader.ReadUInt32();
            // Alpha is derived from RF_ALPHABLENDING (0x1) in the C++.
            Alpha = (RenderFlag & 0x1) != 0;
        }

        if (BaseVersion >= 4)
            BaseVersion4ShapeHeaderName = reader.ReadFixedBytes(N3FxDef.MaxPath);
    }

    /// <summary>
    /// Save mirror of <see cref="Load"/> — field-for-field symmetric across all
    /// base versions so tests round-trip. (The C++ Save only ever emits the
    /// BaseVersion&gt;=2 layout and never the v3/v4 extras; see the slice notes.)
    /// </summary>
    public virtual void Save(BinaryWriter writer)
    {
        writer.Write((byte)Version);
        writer.Write((byte)BaseVersion);

        writer.Write(Life);

        if (BaseVersion >= 3)
        {
            writer.Write(BaseVersion3Unknown0);
            writer.Write(BaseVersion3Unknown1);
        }

        writer.Write((byte)Type);

        writer.Write(Velocity);
        writer.Write(Acceleration);
        writer.Write(RotVelocity);

        writer.Write(OnGround ? (byte)1 : (byte)0);

        writer.Write(Pos);

        writer.Write(NumTex);
        writer.Write(TexFps);
        writer.WriteFixedBytes(TexNameBytes, N3FxDef.MaxPath);

        if (BaseVersion < 2)
        {
            writer.Write(Alpha ? 1 : 0); // BOOL, 4 bytes
            writer.Write(SrcBlend);
            writer.Write(DestBlend);
            writer.Write(FadeOut);
            writer.Write(FadeIn);
        }
        else
        {
            writer.Write(SrcBlend);
            writer.Write(DestBlend);
            writer.Write(FadeOut);
            writer.Write(FadeIn);
            writer.Write(RenderFlag);
        }

        if (BaseVersion >= 4)
            writer.WriteFixedBytes(BaseVersion4ShapeHeaderName, N3FxDef.MaxPath);
    }
}
