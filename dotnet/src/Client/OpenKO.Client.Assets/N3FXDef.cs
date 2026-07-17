using System.Numerics;

namespace OpenKO.Client.Assets;

/// <summary>
/// Constants + enums from <c>Client/N3Base/N3FXDef.h</c> — the shared vocabulary
/// of the .fxb effect bundle format. Data/load layer only (slice 9.10a).
/// </summary>
public static class N3FxDef
{
    /// <summary>MAX_FX_PART_V0 — part-array size for bundle version 0.</summary>
    public const int MaxFxPartV0 = 8;

    /// <summary>MAX_FX_PART_V1_ORIG — the original v1 part-array size (16).</summary>
    public const int MaxFxPartV1Orig = 16;

    /// <summary>MAX_FX_PART_V1 — the v1(+) part-array size (26), bumped without a version change.</summary>
    public const int MaxFxPartV1 = 26;

    /// <summary>MAX_FX_PART — the count a bundle can hold at once (== MAX_FX_PART_V1).</summary>
    public const int MaxFxPart = MaxFxPartV1;

    /// <summary>NUM_VERTEX_PARTICLE — vertices per particle quad.</summary>
    public const int NumVertexParticle = 4;

    /// <summary>NUM_VERTEX_BOTTOM — vertices in a bottom-board fan.</summary>
    public const int NumVertexBottom = 10;

    /// <summary>NUM_KEY_COLOR — size of the particle color-key table.</summary>
    public const int NumKeyColor = 100;

    /// <summary>Windows MAX_PATH / _MAX_PATH — the fixed char[] field width used by the loaders.</summary>
    public const int MaxPath = 260;
}

/// <summary>e_FXPartType (N3FXDef.h): what a part is made of.</summary>
public enum FxPartType
{
    None = 0,
    Particle = 1,
    Board = 2,
    Mesh = 3,
    BottomBoard = 4,
}

/// <summary>e_FXPartState (N3FXDef.h): a part's lifecycle state (runtime).</summary>
public enum FxPartState
{
    Dead = 0,
    Dying = 1,
    Live = 2,
    Ready = 3,
}

/// <summary>e_FXBundleState (N3FXDef.h): a bundle's lifecycle state (runtime).</summary>
public enum FxBundleState
{
    Dead = 0,
    Dying = 1,
    Live = 2,
}

/// <summary>e_FXBundleAct (N3FXDef.h): a bundle's movement behaviour.</summary>
public enum FxBundleAct : uint
{
    MoveDirFixedTarget = 0,
    MoveDirFlexableTarget = 1,
    MoveDirFlexableTargetRatio = 2,
    MoveCurveFixedTarget = 3,
    MoveDirSlow = 4,
    RegionPoison = 5,
    MoveNone = 0xffffffff,
}

/// <summary>e_FXPartParticleEmitType (N3FXDef.h): the shape a particle part emits in.</summary>
public enum FxPartParticleEmitType : uint
{
    Normal = 0,
    Spread = 1,
    Gather = 2,
}

/// <summary>
/// PARTICLEEMITCONDITION (N3FXDef.h): a union — a gather point (Gather emit) or
/// an emit angle (Spread emit). Kept as separate fields; only the field matching
/// the emit type is serialized.
/// </summary>
public struct ParticleEmitCondition
{
    /// <summary>vGatherPoint — used when EmitType == Gather.</summary>
    public Vector3 GatherPoint;

    /// <summary>fEmitAngle — used when EmitType == Spread.</summary>
    public float EmitAngle;
}

/// <summary>
/// __FXBInfo (N3FXDef.h): one entry in a .fxg group — a fixed 260-byte FXB file
/// name, a joint index and a looping flag. Blitted to disk as 268 bytes
/// (char[260] + int + BOOL).
/// </summary>
public sealed class FxbInfo
{
    /// <summary>FXBName — raw fixed char[MAX_PATH] bytes (kept raw for byte-exact round trips).</summary>
    public byte[] NameBytes { get; set; } = new byte[N3FxDef.MaxPath];

    /// <summary>joint — the joint index this bundle attaches to (-1 = none).</summary>
    public int Joint { get; set; } = -1;

    /// <summary>IsLooping — BOOL (4 bytes on disk).</summary>
    public bool IsLooping { get; set; }

    /// <summary>FXBName decoded up to the first NUL.</summary>
    public string Name
    {
        get
        {
            int len = System.Array.IndexOf(NameBytes, (byte)0);
            if (len < 0)
                len = NameBytes.Length;
            return OpenKO.Core.Text.KoEncoding.Cp949.GetString(NameBytes, 0, len);
        }
        set
        {
            var buffer = new byte[N3FxDef.MaxPath];
            byte[] encoded = OpenKO.Core.Text.KoEncoding.Cp949.GetBytes(value);
            System.Array.Copy(encoded, buffer, System.Math.Min(encoded.Length, buffer.Length));
            NameBytes = buffer;
        }
    }

    public void Load(BinaryReader reader)
    {
        NameBytes = reader.ReadBytes(N3FxDef.MaxPath);
        if (NameBytes.Length != N3FxDef.MaxPath)
            throw new EndOfStreamException("FXBInfo name is truncated");
        Joint = reader.ReadInt32();
        IsLooping = reader.ReadInt32() != 0;
    }

    public void Save(BinaryWriter writer)
    {
        var buffer = NameBytes;
        if (buffer.Length != N3FxDef.MaxPath)
        {
            buffer = new byte[N3FxDef.MaxPath];
            System.Array.Copy(NameBytes, buffer, System.Math.Min(NameBytes.Length, buffer.Length));
        }

        writer.Write(buffer);
        writer.Write(Joint);
        writer.Write(IsLooping ? 1 : 0);
    }
}
