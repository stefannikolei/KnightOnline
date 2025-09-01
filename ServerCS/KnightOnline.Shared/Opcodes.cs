namespace KnightOnline.Shared;

/// <summary>
/// Packet opcodes for Knight Online servers
/// </summary>
public static class Opcodes
{
    // Login server opcodes
    public const byte LS_VERSION_REQ = 0x01;
    public const byte LS_DOWNLOADINFO_REQ = 0x02;
    public const byte LS_CRYPTION = 0xF2;
    public const byte LS_LOGIN_REQ = 0xF3;
    public const byte LS_MGAME_LOGIN = 0xF4;
    public const byte LS_SERVERLIST = 0xF5;
    public const byte LS_NEWS = 0xF6;

    // Packet constants
    public const byte PACKET_START1 = 0xAA;
    public const byte PACKET_START2 = 0x55;
    public const byte PACKET_END1 = 0x55;
    public const byte PACKET_END2 = 0xAA;

    // Connection states
    public const byte STATE_CONNECTED = 0x01;
    public const byte STATE_DISCONNECTED = 0x02;
    public const byte STATE_GAMESTART = 0x03;

    // Socket types
    public const byte TYPE_ACCEPT = 0x01;
    public const byte TYPE_CONNECT = 0x02;
}