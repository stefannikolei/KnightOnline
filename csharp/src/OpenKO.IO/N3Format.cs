namespace OpenKO.IO;

/// <summary>
/// N3 on-disk format version flags (port of the N3FORMAT_VER_* macros in
/// Client/N3Base/N3BaseFileAccess.h). These are bit flags used by loaders to decide which
/// fields are present, keeping backwards compatibility across client versions.
/// </summary>
[Flags]
public enum N3FormatVersion : uint
{
    Unknown = 0x00000000,
    V1068 = 0x00000001,
    V1264 = 0x00000002,
    V1298 = 0x00000004,
    V2062 = 0x00000008,
    Current = 0x40000000,
    Hero = 0x80000000,
}

public static class N3Format
{
    /// <summary>Default format version used when none is specified (matches the original).</summary>
    public const N3FormatVersion Default = N3FormatVersion.V1264;
}
