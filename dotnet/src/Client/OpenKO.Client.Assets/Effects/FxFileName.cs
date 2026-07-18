namespace OpenKO.Client.Assets.Effects;

/// <summary>
/// The <c>.fxb</c> filename normalization the C++ FX manager applies to a
/// <c>__TABLE_FX::szFN</c> before using it as the origin-bundle cache key and load
/// path (CN3FXMgr::TriggerBundle: <c>strTmp = pFX-&gt;szFN; _strlwr(strTmp)</c>).
/// <para>
/// The raw table field can carry trailing whitespace and may omit the
/// <c>.fxb</c> extension, so the port trims, lower-cases (the C++
/// <c>_strlwr</c>) and appends <c>.fxb</c> when no extension is present. Two FXIDs
/// that share a file therefore dedupe onto one origin.
/// </para>
/// </summary>
public static class FxFileName
{
    /// <summary>The effect-bundle extension appended when the table field has none.</summary>
    public const string Extension = ".fxb";

    /// <summary>
    /// Trim → lower-case → append <c>.fxb</c> when the name carries no extension.
    /// An empty/whitespace-only input yields the empty string (an unresolvable key).
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string name = raw.Trim().ToLowerInvariant();

        // "no extension" = no '.' after the last directory separator. A dot in a
        // directory segment does not count as an extension for the leaf file.
        int lastSep = name.LastIndexOfAny(['\\', '/']);
        int lastDot = name.LastIndexOf('.');
        bool hasExtension = lastDot > lastSep;
        if (!hasExtension)
            name += Extension;

        return name;
    }
}
