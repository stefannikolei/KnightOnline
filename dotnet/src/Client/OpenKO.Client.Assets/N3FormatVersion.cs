namespace OpenKO.Client.Assets;

/// <summary>N3FORMAT_VER_* (Client/N3Base/N3BaseFileAccess.h).</summary>
public static class N3FormatVersion
{
    public const uint Unknown = 0x00000000;
    public const uint V1068 = 0x00000001;
    public const uint V1264 = 0x00000002;
    public const uint V1298 = 0x00000004;
    public const uint V2062 = 0x00000008;

    /// <summary>N3FORMAT_VER_DEFAULT — the repo default is 1264.</summary>
    public const uint Default = V1264;
}
