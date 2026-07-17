namespace OpenKO.Client.Assets;

/// <summary>
/// One occupied part slot in a bundle: the part plus the time it starts,
/// mirroring the C++ FXPARTWITHSTARTTIME.
/// </summary>
public sealed class N3FXBundlePart
{
    public float StartTime { get; set; }

    public N3FXPartBase Part { get; set; } = null!;
}

/// <summary>
/// Port of <c>CN3FXBundle</c> (Client/N3Base/N3FXBundle.cpp) — the root reader
/// of a <c>.fxb</c> effect bundle.
/// <para>
/// Unlike most CN3BaseFileAccess files, the bundle's <c>Load</c> does NOT read
/// the [len][name] header — it reads <c>m_iVersion</c> (int) first. This class
/// still derives from <see cref="N3BaseFile"/> for the FileName/version
/// infrastructure but its <see cref="Load"/>/<see cref="Save"/> deliberately do
/// not call the base header IO.
/// </para>
/// <para>
/// Simulation/rendering fields (state, positions, target ids, sound object …)
/// are deferred to slices 9.10b/9.10c.
/// </para>
/// </summary>
public sealed class N3FXBundle : N3BaseFile
{
    /// <summary>SUPPORTED_BUNDLE_VERSION — highest bundle version the reader understands.</summary>
    public const int SupportedBundleVersion = 2;

    /// <summary>m_iVersion — the bundle version, read first.</summary>
    public int Version { get; set; } = SupportedBundleVersion;

    /// <summary>m_fLife0 — the bundle lifetime (clamped to 10.0 on load).</summary>
    public float Life0 { get; set; }

    /// <summary>m_fVelocity.</summary>
    public float Velocity { get; set; }

    /// <summary>m_bDependScale (1 byte).</summary>
    public bool DependScale { get; set; }

    /// <summary>m_bStatic (1 byte, version &gt;= 2 only).</summary>
    public bool Static { get; set; }

    /// <summary>
    /// Bytes a writer newer than <see cref="SupportedBundleVersion"/> appended
    /// past <see cref="Static"/> that the C++ client does NOT read (e.g. the
    /// 5-byte tail every version-3 <c>.fxb</c> carries). Captured verbatim so the
    /// file fully consumes and round-trips; empty for versions &lt;= 2.
    /// </summary>
    public byte[] UnsupportedVersionTail { get; set; } = [];

    /// <summary>
    /// The part slots, length MAX_FX_PART (26). A null entry is an empty
    /// (FX_PART_TYPE_NONE) slot.
    /// </summary>
    public N3FXBundlePart?[] Parts { get; } = new N3FXBundlePart?[N3FxDef.MaxFxPart];

    /// <summary>
    /// CN3FXBundle::GetPartCountForVersion — reproduced verbatim:
    /// v&lt;0 → 0, v==0 → 8, else 26.
    /// <para>
    /// NOTE: In the current C++ the return value is computed into a local but is
    /// NOT actually used as the load-loop bound — the loop always iterates
    /// MAX_FX_PART (26). <see cref="Load"/> reproduces the real loop bound (26);
    /// this method is kept for parity and callers that want the per-version size.
    /// </para>
    /// </summary>
    public int GetPartCountForVersion()
    {
        if (Version < 0)
            return 0;

        if (Version == 0)
            return N3FxDef.MaxFxPartV0;

        return N3FxDef.MaxFxPartV1;
    }

    /// <summary>CN3FXBundle::AllocatePart — the part-type dispatch (null for invalid types).</summary>
    public static N3FXPartBase? AllocatePart(FxPartType type) => type switch
    {
        FxPartType.Particle => new N3FXPartParticles(),
        FxPartType.Board => new N3FXPartBillBoard(),
        FxPartType.Mesh => new N3FXPartMesh(),
        FxPartType.BottomBoard => new N3FXPartBottomBoard(),
        _ => null,
    };

    public override void Load(BinaryReader reader)
    {
        // NOTE: no base.Load — the bundle has no [len][name] header.
        Version = reader.ReadInt32();

        Life0 = reader.ReadSingle();
        if (Life0 > 10f)
            Life0 = 10f;

        Velocity = reader.ReadSingle();
        DependScale = reader.ReadByte() != 0;

        // GetPartCountForVersion() is computed-but-unused in the C++; the loop's
        // written bound is MAX_FX_PART (26). But the tool only ever wrote
        // MAX_FX_PART_TOOL (16) slots, and the runtime relies on File::Read being
        // a no-op past EOF: the extra slot-type reads return 0 (== NONE) and
        // m_bStatic keeps its default. Reproduced here by stopping the loop once
        // there aren't 4 bytes left for a slot-type int (i.e. at/after EOF).
        Stream stream = reader.BaseStream;
        for (int i = 0; i < N3FxDef.MaxFxPart; i++)
        {
            if (stream.Position + 4 > stream.Length)
                break; // past-EOF slot reads are NONE in the C++

            var type = (FxPartType)reader.ReadInt32();

            if (type == FxPartType.None)
                continue;

            N3FXPartBase? part = AllocatePart(type);
            if (part == null)
                break; // invalid type: the C++ stops parsing here

            float startTime = reader.ReadSingle();
            part.Type = type;
            part.Load(reader);

            Parts[i] = new N3FXBundlePart { StartTime = startTime, Part = part };
        }

        // m_bStatic is read only for version >= 2, and only if a byte is actually
        // present (a past-EOF read would leave it at its default in the C++).
        if (Version >= 2 && stream.Position < stream.Length)
            Static = reader.ReadByte() != 0;

        // A writer newer than SUPPORTED_BUNDLE_VERSION (e.g. version 3) appends
        // extra bytes the C++ client never reads. Capture them verbatim so the
        // file is fully consumed and can round-trip.
        if (Version > SupportedBundleVersion && stream.Position < stream.Length)
            UnsupportedVersionTail = reader.ReadBytes((int)(stream.Length - stream.Position));
    }

    /// <summary>
    /// CN3FXBundle::Save — mirrors the C++ Save exactly, including that
    /// <see cref="Static"/> is always written (Load only reads it when
    /// Version &gt;= 2, so a Version &lt; 2 stream carries one extra trailing byte,
    /// just like the original).
    /// </summary>
    public override void Save(BinaryWriter writer)
    {
        writer.Write(Version);
        writer.Write(Life0);
        writer.Write(Velocity);
        writer.Write(DependScale ? (byte)1 : (byte)0);

        for (int i = 0; i < N3FxDef.MaxFxPart; i++)
        {
            N3FXBundlePart? slot = Parts[i];
            if (slot?.Part != null)
            {
                writer.Write((int)slot.Part.Type);
                writer.Write(slot.StartTime);
                slot.Part.Save(writer);
            }
            else
            {
                writer.Write((int)FxPartType.None);
            }
        }

        writer.Write(Static ? (byte)1 : (byte)0);

        if (Version > SupportedBundleVersion)
            writer.Write(UnsupportedVersionTail);
    }
}
