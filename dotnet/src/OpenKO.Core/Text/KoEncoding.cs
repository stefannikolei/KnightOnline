using System.Text;

namespace OpenKO.Core.Text;

/// <summary>
/// Knight Online client text is CP949 (EUC-KR superset).
/// Wire strings should be handled as raw bytes; convert with <see cref="Cp949"/>
/// only at logging/DB/UI boundaries.
/// </summary>
public static class KoEncoding
{
    static KoEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Cp949 { get; } = GetCp949();

    private static Encoding GetCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }
}
