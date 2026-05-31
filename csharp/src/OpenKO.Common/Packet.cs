namespace OpenKO.Common;

/// <summary>
/// Port of the C++ <c>Packet</c> (shared/Packet.h): a <see cref="ByteBuffer"/> whose first
/// byte is the opcode.
/// </summary>
public class Packet : ByteBuffer
{
    public Packet()
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

    public Packet(GameOpcode opcode) : this((byte)opcode)
    {
    }

    public Packet(LoginOpcode opcode) : this((byte)opcode)
    {
    }

    public Packet(Packet other) : base(other)
    {
    }

    public byte Opcode => Size == 0 ? (byte)0 : this[0];

    /// <summary>Clear packet and set opcode all in one mighty blow.</summary>
    public void Initialize(byte opcode)
    {
        Clear();
        Append(opcode);
    }

    public void Initialize(GameOpcode opcode) => Initialize((byte)opcode);
}
