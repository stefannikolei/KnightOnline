namespace OpenKO.Core.Protocol;

/// <summary>
/// AIServer &lt;-&gt; Ebenezer opcodes (AG_*) from <c>shared/packets.h</c>.
/// Needed from stage 3 onward; defined here so all opcode constants live in one place.
/// </summary>
public static class AiOpcode
{
    public const byte AI_SERVER_CONNECT = 1;
    public const byte NPC_INFO_ALL = 2;
    public const byte MOVE_REQ = 3;
    public const byte MOVE_RESULT = 4;
    public const byte MOVE_END_REQ = 5;
    public const byte MOVE_END_RESULT = 6;
    public const byte AG_NPC_INFO = 7;
    public const byte AG_NPC_GIVE_ITEM = 8;
    public const byte AG_NPC_GATE_OPEN = 9;
    public const byte AG_NPC_GATE_DESTORY = 10;
    public const byte AG_NPC_INOUT = 11;
    public const byte AG_NPC_EVENT_ITEM = 12;
    public const byte AG_NPC_HP_REQ = 13;

    public const byte AG_SERVER_INFO = 50;
    public const byte AG_ATTACK_REQ = 51;
    public const byte AG_ATTACK_RESULT = 52;
    public const byte AG_DEAD = 53;
    public const byte AG_SYSTEM_MSG = 54;
    public const byte AG_CHECK_ALIVE_REQ = 55;
    public const byte AG_COMPRESSED_DATA = 56;
    public const byte AG_ZONE_CHANGE = 57;
    public const byte AG_MAGIC_ATTACK_REQ = 58;
    public const byte AG_MAGIC_ATTACK_RESULT = 59;
    public const byte AG_USER_INFO_ALL = 60;
    public const byte AG_LONG_MAGIC_ATTACK = 61;
    public const byte AG_PARTY_INFO_ALL = 62;
    public const byte AG_HEAL_MAGIC = 63;
    public const byte AG_TIME_WEATHER = 64;
    public const byte AG_BATTLE_EVENT = 65;
    public const byte AG_COMPRESSED = 66;

    public const byte AG_USER_INFO = 101;
    public const byte AG_USER_INOUT = 102;
    public const byte AG_USER_MOVE = 103;
    public const byte AG_USER_MOVEEDGE = 104;
    public const byte AG_USER_SET_HP = 105;
    public const byte AG_USER_LOG_OUT = 106;
    public const byte AG_USER_REGENE = 107;
    public const byte AG_USER_EXP = 108;
    public const byte AG_USER_UPDATE = 109;
    public const byte AG_USER_FAIL = 110;
    public const byte AG_USER_PARTY = 111;
    public const byte AG_USER_VISIBILITY = 112;
    public const byte AG_NPC_HP_CHANGE = 113;
}
