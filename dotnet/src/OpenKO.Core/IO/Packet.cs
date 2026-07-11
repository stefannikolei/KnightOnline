namespace OpenKO.Core.IO;

/// <summary>
/// Port of <c>shared/Packet.h</c> — a ByteBuffer whose first byte is the opcode.
/// </summary>
public class Packet : ByteBuffer
{
    public Packet()
    {
    }

    public Packet(byte opcode)
        : base(4096)
    {
        Append(opcode);
    }

    public Packet(byte opcode, int reserve)
        : base(reserve)
    {
        Append(opcode);
    }

    public Packet(Packet packet)
        : base(packet)
    {
    }

    public byte Opcode => Size == 0 ? (byte)0 : this[0];

    /// <summary>Clear packet and set opcode all in one mighty blow.</summary>
    public void Initialize(byte opcode)
    {
        Clear();
        Append(opcode);
    }
}
