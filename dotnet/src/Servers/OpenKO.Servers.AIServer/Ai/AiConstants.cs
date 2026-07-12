namespace OpenKO.Servers.AIServer.Ai;

/// <summary>Constants from Server/AIServer/Define.h and Npc.h.</summary>
public static class AiConstants
{
    public const int MaxUser = 3000;
    public const int NpcsPerThread = 20;    // NPC_NUM
    public const int ViewDistance = 48;     // VIEW_DIST (region size, meters)
    public const int MaxPathLine = 100;

    public const int NpcMaxUserList = 5;    // NPC_HAVE_USER_LIST
    public const int NpcHaveItemList = 6;
    public const int NpcMaxPathList = 100;
    public const int NpcExpRange = 50;
    public const int NpcViewRange = 100;
    public const int NpcTracingStep = 100;

    public const int MaxMagicType3 = 20;
    public const int MaxMagicType4 = 9;

    public const int MaxMapArraySize = 10000; // MAX_MAP_SIZE (pathfind window, x*sizeY+y)
}

/// <summary>NPC state machine states (NPC_* in Define.h).</summary>
public enum NpcState : byte
{
    Dead = 0x00,
    Live = 0x01,
    Attacking = 0x02,
    Standing = 0x05,
    Moving = 0x06,
    Tracing = 0x07,
    Fighting = 0x08,
    Strategy = 0x09,
    Back = 0x0A,
    Sleeping = 0x0B,
    Fainting = 0x0C,
    Healing = 0x0D,
}

/// <summary>_Target: the user a NPC is attacking.</summary>
public struct NpcTarget
{
    public int Id;
    public float X;
    public float Y;
    public float Z;
    public int FailCount;
}

/// <summary>_ExpUserList: damage bookkeeping for exp distribution.</summary>
public struct ExpUserEntry
{
    public string UserId;
    public int Uid;
    public int Damage;
    public bool InSight;
}

/// <summary>_PattenPos.</summary>
public struct PatternPos
{
    public short X;
    public short Z;
}

/// <summary>_MagicType3: damage-over-time effect on a NPC.</summary>
public struct NpcMagicType3
{
    public short AttackUserId;
    public short HpAmount;
    public byte Duration;
    public byte Interval;
    public double StartTime;
}

/// <summary>_MagicType4: stat buff/debuff on a NPC.</summary>
public struct NpcMagicType4
{
    public byte Amount;
    public short DurationTime;
    public double StartTime;
}
