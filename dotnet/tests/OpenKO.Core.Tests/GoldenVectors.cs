using System.Text.Json;

namespace OpenKO.Core.Tests;

/// <summary>Loads the checked-in golden vectors generated from the C++ reference
/// implementation by <c>dotnet/tools/golden-gen</c>.</summary>
public static class GoldenVectors
{
    public static string VectorPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "vectors", fileName);

    public static JsonElement Load(string fileName)
    {
        using var stream = File.OpenRead(VectorPath(fileName));
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    public static byte[] Hex(string hex)
    {
        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = byte.Parse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
        return result;
    }
}
