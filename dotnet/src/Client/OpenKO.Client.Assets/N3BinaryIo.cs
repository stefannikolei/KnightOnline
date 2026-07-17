using System.Numerics;
using System.Runtime.InteropServices;
using OpenKO.Core.Text;

namespace OpenKO.Client.Assets;

/// <summary>
/// The raw-read conventions of the C++ loaders (File::Read of packed structs):
/// little-endian primitives, [int32 length][bytes] strings (CP949), __Vector3 as
/// three floats, __Quaternion as x/y/z/w, __Matrix44 as 16 row-major floats
/// (identical to <see cref="Matrix4x4"/>), D3DCOLOR as a uint (ARGB).
/// </summary>
public static class N3BinaryIo
{
    /// <summary>The [int32 len][bytes] string read used across the N3 loaders.</summary>
    public static string ReadN3String(this BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0)
            return string.Empty;

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("N3 string is truncated");

        return KoEncoding.Cp949.GetString(bytes);
    }

    public static void WriteN3String(this BinaryWriter writer, string value)
    {
        byte[] bytes = KoEncoding.Cp949.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public static Vector3 ReadVector3(this BinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public static void Write(this BinaryWriter writer, in Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }

    public static Quaternion ReadQuaternion(this BinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public static void Write(this BinaryWriter writer, in Quaternion q)
    {
        writer.Write(q.X);
        writer.Write(q.Y);
        writer.Write(q.Z);
        writer.Write(q.W);
    }

    /// <summary>__Matrix44 — row-major like System.Numerics.</summary>
    public static Matrix4x4 ReadMatrix4x4(this BinaryReader reader)
    {
        var m = new Matrix4x4();
        m.M11 = reader.ReadSingle(); m.M12 = reader.ReadSingle(); m.M13 = reader.ReadSingle(); m.M14 = reader.ReadSingle();
        m.M21 = reader.ReadSingle(); m.M22 = reader.ReadSingle(); m.M23 = reader.ReadSingle(); m.M24 = reader.ReadSingle();
        m.M31 = reader.ReadSingle(); m.M32 = reader.ReadSingle(); m.M33 = reader.ReadSingle(); m.M34 = reader.ReadSingle();
        m.M41 = reader.ReadSingle(); m.M42 = reader.ReadSingle(); m.M43 = reader.ReadSingle(); m.M44 = reader.ReadSingle();
        return m;
    }

    public static void Write(this BinaryWriter writer, in Matrix4x4 m)
    {
        writer.Write(m.M11); writer.Write(m.M12); writer.Write(m.M13); writer.Write(m.M14);
        writer.Write(m.M21); writer.Write(m.M22); writer.Write(m.M23); writer.Write(m.M24);
        writer.Write(m.M31); writer.Write(m.M32); writer.Write(m.M33); writer.Write(m.M34);
        writer.Write(m.M41); writer.Write(m.M42); writer.Write(m.M43); writer.Write(m.M44);
    }

    /// <summary>
    /// Bulk-reads a packed struct array the way File::Read blits C++ arrays.
    /// Little-endian only, like the original x86 client.
    /// </summary>
    public static T[] ReadStructs<T>(this BinaryReader reader, int count) where T : unmanaged
    {
        if (count <= 0)
            return [];

        var result = new T[count];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(result.AsSpan()));
        return result;
    }

    public static void WriteStructs<T>(this BinaryWriter writer, ReadOnlySpan<T> values) where T : unmanaged
        => writer.Write(MemoryMarshal.AsBytes(values));

    public static T ReadStruct<T>(this BinaryReader reader) where T : unmanaged
    {
        T value = default;
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(new Span<T>(ref value)));
        return value;
    }

    public static void WriteStruct<T>(this BinaryWriter writer, in T value) where T : unmanaged
    {
        T copy = value;
        writer.Write(MemoryMarshal.AsBytes(new Span<T>(ref copy)));
    }

    /// <summary>
    /// The C++ loaders' bare filename read: [int32 len][bytes], CP949, no
    /// read at all when len &lt;= 0 (unlike ReadN3String this keeps a
    /// prefilled value untouched in that case — matching char szFN[] reuse
    /// is NOT wanted; the callers always treat len &lt;= 0 as "no name").
    /// </summary>
    public static string ReadN3FileName(this BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0)
            return string.Empty;

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("N3 filename is truncated");

        return KoEncoding.Cp949.GetString(bytes);
    }

    public static void WriteN3FileName(this BinaryWriter writer, string value)
        => writer.WriteN3String(value);

    /// <summary>
    /// Reads a fixed-width raw byte field (a C++ <c>char[N]</c> member blitted
    /// verbatim, e.g. m_pTexName[MAX_PATH]). Throws on truncation.
    /// </summary>
    public static byte[] ReadFixedBytes(this BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new EndOfStreamException("N3 fixed byte field is truncated");
        return bytes;
    }

    /// <summary>
    /// Writes a fixed-width raw byte field, padding with zeros or truncating so
    /// exactly <paramref name="count"/> bytes are emitted.
    /// </summary>
    public static void WriteFixedBytes(this BinaryWriter writer, byte[] value, int count)
    {
        if (value.Length == count)
        {
            writer.Write(value);
            return;
        }

        var buffer = new byte[count];
        Array.Copy(value, buffer, Math.Min(value.Length, count));
        writer.Write(buffer);
    }

    /// <summary>Decodes a fixed char[] field (CP949) up to its first NUL.</summary>
    public static string DecodeFixedString(byte[] bytes)
    {
        int len = Array.IndexOf(bytes, (byte)0);
        if (len < 0)
            len = bytes.Length;
        return KoEncoding.Cp949.GetString(bytes, 0, len);
    }

    /// <summary>Encodes a string into a fresh zero-padded fixed char[] field of <paramref name="count"/> bytes.</summary>
    public static byte[] EncodeFixedString(string value, int count)
    {
        var buffer = new byte[count];
        byte[] encoded = KoEncoding.Cp949.GetBytes(value);
        Array.Copy(encoded, buffer, Math.Min(encoded.Length, count));
        return buffer;
    }
}
