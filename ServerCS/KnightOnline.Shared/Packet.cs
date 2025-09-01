using System;

namespace KnightOnline.Shared;

/// <summary>
/// C# port of the C++ Packet class
/// </summary>
public class Packet : ByteBuffer
{
    public Packet() : base()
    {
    }

    public Packet(byte opcode) : base(4096)
    {
        Append(opcode);
    }

    public Packet(byte opcode, int reserve) : base(reserve)
    {
        Append(opcode);
    }

    public Packet(Packet other) : base(other)
    {
    }

    public byte GetOpcode()
    {
        return Size == 0 ? (byte)0 : Contents[0];
    }

    /// <summary>
    /// Clear packet and set opcode in one operation
    /// </summary>
    public void Initialize(byte opcode)
    {
        Clear();
        Append(opcode);
    }
}