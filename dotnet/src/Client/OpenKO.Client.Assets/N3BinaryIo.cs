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
}
