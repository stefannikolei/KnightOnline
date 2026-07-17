using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The WIZ_CLASS_CHANGE packets (CUIClassChange). The client sends a
/// <c>N3_SP_CLASS_CHANGE_REQ</c> promotion request; the server replies with a result
/// sub-opcode that drives the dialog. Field order pinned against the C++ client.
/// </summary>
public static class ClassChangeProtocol
{
    // e_SubPacket_ClassChange_Main (client → server).
    public const byte SubClassChangeReq = 0x02; // N3_SP_CLASS_CHANGE_REQ

    // e_SubPacket_ClassChange result family (server → client) — PacketDef.h.
    public const byte ResultFailure = 0x00;    // N3_SP_CLASS_CHANGE_FAILURE
    public const byte ResultSuccess = 0x01;    // N3_SP_CLASS_CHANGE_SUCCESS
    public const byte ResultNotYet = 0x02;     // N3_SP_CLASS_CHANGE_NOT_YET
    public const byte ResultAlready = 0x03;    // N3_SP_CLASS_CHANGE_ALREADY
    public const byte ResultItemInSlot = 0x04; // N3_SP_CLASS_CHANGE_ITEM_IN_SLOT

    /// <summary>
    /// The class-change request: <c>[0x34][0x02][newClass:i16]</c> (4 bytes) —
    /// the promoted class the client is switching to.
    /// </summary>
    public static byte[] BuildRequest(short newClass)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_CLASS_CHANGE);
        w.SetByte(SubClassChangeReq);
        w.SetShort(newClass);
        return w.Written.ToArray();
    }

    /// <summary>The server reply's result sub-opcode (payload[1], 0x00..0x04).</summary>
    public static byte ParseResult(ReadOnlySpan<byte> payload) => payload[1];

    /// <summary>
    /// The first-promotion map (shared/globals.h): a level-10 base class promotes to
    /// its single first-tier advanced class. Non-base (already promoted) classes and
    /// unknown ids return unchanged, so a double promotion is a no-op here.
    /// </summary>
    public static short Promote(short baseClass) => baseClass switch
    {
        101 => 105, // CLASS_KA_WARRIOR → CLASS_KA_BERSERKER
        102 => 107, // CLASS_KA_ROGUE   → CLASS_KA_HUNTER
        103 => 109, // CLASS_KA_WIZARD  → CLASS_KA_SORCERER
        104 => 111, // CLASS_KA_PRIEST  → CLASS_KA_SHAMAN
        201 => 205, // CLASS_EL_WARRIOR → CLASS_EL_BLADE
        202 => 207, // CLASS_EL_ROGUE   → CLASS_EL_RANGER
        203 => 209, // CLASS_EL_WIZARD  → CLASS_EL_MAGE
        204 => 211, // CLASS_EL_PRIEST  → CLASS_EL_CLERIC
        _ => baseClass,
    };

    /// <summary>True for the eight level-10 base classes (KA 101-104, El 201-204).</summary>
    public static bool IsBaseClass(short cls) => cls is (>= 101 and <= 104) or (>= 201 and <= 204);
}
