namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3Chr</c> loading (N3Chr.cpp) — the .n3chr file: the
/// transform+collision header plus the file names of the skeleton, parts,
/// plugs, animation control and FX plug. The C++ resolves each name through
/// its resource managers; the asset library records them for the caller.
/// </summary>
public sealed class N3Chr : N3TransformCollision
{
    public const int MaxAniParts = 2; // MAX_CHR_ANI_PART

    public string JointFileName { get; set; } = string.Empty;

    /// <summary>Per part slot the .n3cpart file name (empty = unset slot, as in the C++).</summary>
    public List<string> PartFileNames { get; } = [];

    /// <summary>Per plug slot the .n3cplug/.n3cloak file name (empty = unset slot).</summary>
    public List<string> PlugFileNames { get; } = [];

    public string AniCtrlFileName { get; set; } = string.Empty;

    public int[] JointPartStarts { get; } = [-1, -1];

    public int[] JointPartEnds { get; } = [-1, -1];

    public string FxPlugFileName { get; set; } = string.Empty;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        JointFileName = reader.ReadN3FileName();

        PartFileNames.Clear();
        int partCount = reader.ReadInt32();
        for (int i = 0; i < partCount; i++)
            PartFileNames.Add(reader.ReadN3FileName());

        PlugFileNames.Clear();
        int plugCount = reader.ReadInt32();
        for (int i = 0; i < plugCount; i++)
            PlugFileNames.Add(reader.ReadN3FileName());

        AniCtrlFileName = reader.ReadN3FileName();

        // Pre-2002 files end before some tail fields (intro.n3chr stops after
        // the joint-part indices, before the FXPlug name). The C++ reads past
        // EOF are no-ops keeping the defaults — mirrored with EOF guards.
        for (int i = 0; i < MaxAniParts; i++)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                return;
            JointPartStarts[i] = reader.ReadInt32();
        }

        for (int i = 0; i < MaxAniParts; i++)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                return;
            JointPartEnds[i] = reader.ReadInt32();
        }

        if (reader.BaseStream.Position >= reader.BaseStream.Length)
            return;
        FxPlugFileName = reader.ReadN3FileName();

        // NOTE: many 1298 .n3chr files carry one more [len][name] block after
        // this (an "..._collision.n3cskin" reference from the commented-out
        // m_pSkinCollision feature). The C++ never reads it; neither do we.
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.WriteN3FileName(JointFileName);

        writer.Write(PartFileNames.Count);
        foreach (string name in PartFileNames)
            writer.WriteN3FileName(name);

        writer.Write(PlugFileNames.Count);
        foreach (string name in PlugFileNames)
            writer.WriteN3FileName(name);

        writer.WriteN3FileName(AniCtrlFileName);

        for (int i = 0; i < MaxAniParts; i++)
            writer.Write(JointPartStarts[i]);
        for (int i = 0; i < MaxAniParts; i++)
            writer.Write(JointPartEnds[i]);

        writer.WriteN3FileName(FxPlugFileName);
    }
}
