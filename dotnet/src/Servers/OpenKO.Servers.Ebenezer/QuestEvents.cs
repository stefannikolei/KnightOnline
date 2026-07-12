using Microsoft.Extensions.Logging;

namespace OpenKO.Servers.Ebenezer;

/// <summary>e_Exec (Ebenezer Define.h).</summary>
public static class QuestExecOp
{
    public const byte None = 0x00;
    public const byte Say = 0x01;
    public const byte SelectMsg = 0x02;
    public const byte RunEvent = 0x03;
    public const byte GiveItem = 0x04;
    public const byte RobItem = 0x05;
    public const byte Return = 0x06;
    public const byte OpenEditBox = 0x07;
    public const byte GiveNoah = 0x08;
    public const byte LogCouponItem = 0x09;
    public const byte SaveComEvent = 0x0A;
    public const byte RobNoah = 0x0B;
}

/// <summary>e_LogicCheck (Ebenezer Define.h).</summary>
public static class QuestLogicOp
{
    public const byte None = 0x00;
    public const byte CheckUnderWeight = 0x01;
    public const byte CheckOverWeight = 0x02;
    public const byte CheckSkillPoint = 0x03;
    public const byte CheckExistItem = 0x04;
    public const byte CheckClass = 0x05;
    public const byte CheckWeight = 0x06;
    public const byte CheckEditBox = 0x07;
    public const byte Rand = 0x08;
    public const byte HowMuchItem = 0x09;
    public const byte CheckLevel = 0x0A;
    public const byte NoExistComEvent = 0x0B;
    public const byte ExistComEvent = 0x0C;
    public const byte CheckNoah = 0x0D;
    public const byte CheckNation = 0x0E;
    public const byte CheckExistEvent = 0x15;
    public const byte CheckNoExistEvent = 0x16;
    public const byte CheckPromotionEligible = 0x17;
    public const byte CheckNoExistItem = 0x19;
    public const byte CheckItemChangeNum = 0x1A;
    public const byte CheckNoClass = 0x1B;
    public const byte CheckLoyalty = 0x1C;
    public const byte CheckChief = 0x1D;
    public const byte CheckNoChief = 0x1E;
    public const byte CheckClanGrade = 0x1F;
    public const byte CheckKnight = 0x20;
    public const byte CheckMonsterChallengeTime = 0x25;
    public const byte CheckMonsterChallengeUserCount = 0x26;
    public const byte CheckCastle = 0x27;
    public const byte CheckNoCastle = 0x28;
    public const byte CheckEmptySlot = 0x2B;
    public const byte CheckMiddleStatueCapture = 0x2F;
    public const byte CheckMiddleStatueNoCapture = 0x30;
    public const byte CheckBeefRoastKarusVictory = 0x35;
    public const byte CheckBeefRoastElmoradVictory = 0x36;
    public const byte CheckBeefRoastNoVictory = 0x37;
}

/// <summary>EXEC — one 'E' line.</summary>
public sealed class QuestExec
{
    public const int MaxExecInt = 30; // MAX_EXEC_INT

    public byte Exec;
    public readonly int[] Ints = new int[MaxExecInt];
}

/// <summary>LOGIC_ELSE — one 'A' line (always AND upstream).</summary>
public sealed class QuestLogic
{
    public const int MaxLogicInt = 10; // MAX_LOGIC_ELSE_INT

    public byte LogicElse;
    public bool And = true;
    public readonly int[] Ints;

    public QuestLogic()
    {
        Ints = new int[MaxLogicInt];
        Array.Fill(Ints, -1);
    }
}

/// <summary>EVENT_DATA — one EVENT block.</summary>
public sealed class QuestEventData
{
    public int EventNum;
    public readonly List<QuestExec> Execs = [];
    public readonly List<QuestLogic> Logics = [];
}

/// <summary>
/// Port of <c>EVENT::LoadEvent</c> (Server/Ebenezer/EVENT.cpp): the line-based
/// QUESTS/&lt;zone&gt;.evt parser. The C++ dispatches the opcode strings through
/// compile-time djb2 hashes; the port compares the strings directly (the hash
/// never leaves the process).
/// </summary>
public static class QuestEventFile
{
    public static Dictionary<int, QuestEventData>? Load(string path, int zone, ILogger logger)
    {
        var events = new Dictionary<int, QuestEventData>();
        QuestEventData? current = null;

        int lineNumber = 0;
        foreach (string raw in File.ReadLines(path))
        {
            lineNumber++;
            string line = raw.TrimEnd('\r');
            if (line.Length <= 1)
                continue;

            if (line[0] is ';' or '/')
                continue;

            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            switch (tokens[0])
            {
                case "EVENT":
                {
                    int eventNum = Atoi(tokens.ElementAtOrDefault(1));
                    if (current is not null)
                        return Fail(logger, path, zone, eventNum);

                    if (events.ContainsKey(eventNum))
                    {
                        logger.LogError("QuestEventFile: duplicate definition [eventId={EventId}]", eventNum);
                        return Fail(logger, path, zone, eventNum);
                    }

                    current = new QuestEventData { EventNum = eventNum };
                    events[eventNum] = current;
                    break;
                }

                case "E":
                {
                    if (current is null)
                        return Fail(logger, path, zone, -1);

                    current.Execs.Add(ParseExec(tokens, path, lineNumber, logger));
                    break;
                }

                case "A":
                {
                    if (current is null)
                        return Fail(logger, path, zone, -1);

                    current.Logics.Add(ParseLogic(tokens, path, lineNumber, logger));
                    break;
                }

                case "END":
                    if (current is null)
                        return Fail(logger, path, zone, -1);

                    current = null;
                    break;

                default:
                    if (char.IsLetterOrDigit(tokens[0][0]))
                    {
                        logger.LogWarning("QuestEventFile({Zone}): unhandled opcode '{Opcode}' ({Path}:{Line})",
                            zone, tokens[0], path, lineNumber);
                    }
                    break;
            }
        }

        return events;
    }

    private static Dictionary<int, QuestEventData>? Fail(ILogger logger, string path, int zone, int eventNum)
    {
        logger.LogError("QUEST INFO READ FAIL ({Zone})({EventNum}) [{Path}]", zone, eventNum, path);
        return null;
    }

    /// <summary>atoi semantics: leading integer or 0.</summary>
    private static int Atoi(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return 0;

        int i = 0;
        bool negative = false;
        if (token[0] is '-' or '+')
        {
            negative = token[0] == '-';
            i = 1;
        }

        long value = 0;
        for (; i < token.Length && char.IsAsciiDigit(token[i]); i++)
            value = value * 10 + (token[i] - '0');

        return (int)(negative ? -value : value);
    }

    private static QuestExec ParseExec(string[] tokens, string path, int lineNumber, ILogger logger)
    {
        var exec = new QuestExec();
        int args = 0;

        switch (tokens.ElementAtOrDefault(1))
        {
            case "SAY":
                exec.Exec = QuestExecOp.Say;
                args = 10;
                break;

            case "SELECT_MSG":
                exec.Exec = QuestExecOp.SelectMsg;
                args = 22;
                break;

            case "RUN_EVENT":
                exec.Exec = QuestExecOp.RunEvent;
                args = 1;
                break;

            case "GIVE_ITEM":
                exec.Exec = QuestExecOp.GiveItem;
                args = 2;
                break;

            case "ROB_ITEM":
                exec.Exec = QuestExecOp.RobItem;
                args = 2;
                break;

            case "OPEN_EDITBOX":
                exec.Exec = QuestExecOp.OpenEditBox;
                args = 3;
                break;

            case "GIVE_NOAH":
                exec.Exec = QuestExecOp.GiveNoah;
                args = 1;
                break;

            case "LOG_COUPON_ITEM":
                exec.Exec = QuestExecOp.LogCouponItem;
                args = 2;
                break;

            case "RETURN":
                exec.Exec = QuestExecOp.Return;
                break;

            default:
                logger.LogWarning("QuestEventFile: unhandled E opcode '{Opcode}' ({Path}:{Line})",
                    tokens.ElementAtOrDefault(1), path, lineNumber);
                break;
        }

        for (int i = 0; i < args; i++)
            exec.Ints[i] = Atoi(tokens.ElementAtOrDefault(2 + i));

        return exec;
    }

    private static QuestLogic ParseLogic(string[] tokens, string path, int lineNumber, ILogger logger)
    {
        var logic = new QuestLogic();
        int args = 0;

        switch (tokens.ElementAtOrDefault(1))
        {
            case "CHECK_UNDER_WEIGHT":
                logic.LogicElse = QuestLogicOp.CheckUnderWeight;
                args = 1;
                break;

            case "CHECK_OVER_WEIGHT":
                logic.LogicElse = QuestLogicOp.CheckOverWeight;
                args = 1;
                break;

            case "CHECK_SKILL_POINT":
                logic.LogicElse = QuestLogicOp.CheckSkillPoint;
                args = 3;
                break;

            case "CHECK_EXIST_ITEM":
                logic.LogicElse = QuestLogicOp.CheckExistItem;
                args = 2;
                break;

            case "CHECK_NOEXIST_ITEM":
                logic.LogicElse = QuestLogicOp.CheckNoExistItem;
                args = 2;
                break;

            case "CHECK_CLASS":
                logic.LogicElse = QuestLogicOp.CheckClass;
                args = 6;
                break;

            case "CHECK_WEIGHT":
                logic.LogicElse = QuestLogicOp.CheckWeight;
                args = 2;
                break;

            case "CHECK_EDITBOX":
                logic.LogicElse = QuestLogicOp.CheckEditBox;
                args = 1;
                break;

            case "RAND":
                logic.LogicElse = QuestLogicOp.Rand;
                args = 1;
                break;

            case "CHECK_NOAH":
                logic.LogicElse = QuestLogicOp.CheckNoah;
                args = 2;
                break;

            case "CHECK_LV":
                logic.LogicElse = QuestLogicOp.CheckLevel;
                args = 2;
                break;

            case "HOWMUCH_ITEM":
                logic.LogicElse = QuestLogicOp.HowMuchItem;
                args = 3;
                break;

            case "NOEXIST_COM_EVENT":
                logic.LogicElse = QuestLogicOp.NoExistComEvent;
                args = 1;
                break;

            case "CHECK_NATION":
                logic.LogicElse = QuestLogicOp.CheckNation;
                args = 1;
                break;

            case "EXIST_COM_EVENT":
                logic.LogicElse = QuestLogicOp.ExistComEvent;
                args = 1;
                break;

            case "CHECK_PROMOTION_ELIGIBLE":
                logic.LogicElse = QuestLogicOp.CheckPromotionEligible;
                args = 1;
                break;

            case "CHECK_MONSTER_CHALLENGE_TIME":
                logic.LogicElse = QuestLogicOp.CheckMonsterChallengeTime;
                args = 1;
                break;

            case "CHECK_EXIST_EVENT":
                logic.LogicElse = QuestLogicOp.CheckExistEvent;
                args = 2;
                break;

            case "CHECK_NOEXIST_EVENT":
                logic.LogicElse = QuestLogicOp.CheckNoExistEvent;
                args = 2;
                break;

            case "CHECK_ITEMCHANGE_NUM":
                logic.LogicElse = QuestLogicOp.CheckItemChangeNum;
                args = 1;
                break;

            case "CHECK_NOCLASS":
                logic.LogicElse = QuestLogicOp.CheckNoClass;
                args = 6;
                break;

            case "CHECK_LOYALTY":
                logic.LogicElse = QuestLogicOp.CheckLoyalty;
                args = 2;
                break;

            case "CHECK_CHIEF":
                logic.LogicElse = QuestLogicOp.CheckChief;
                args = 1;
                break;

            case "CHECK_NO_CHIEF":
                logic.LogicElse = QuestLogicOp.CheckNoChief;
                args = 1;
                break;

            case "CHECK_CLAN_GRADE":
                logic.LogicElse = QuestLogicOp.CheckClanGrade;
                args = 2;
                break;

            case "CHECK_KNIGHT":
                logic.LogicElse = QuestLogicOp.CheckKnight;
                args = 1;
                break;

            case "CHECK_MIDDLE_STATUE_NOCAPTURE":
                logic.LogicElse = QuestLogicOp.CheckMiddleStatueNoCapture;
                args = 1;
                break;

            case "CHECK_MIDDLE_STATUE_CAPTURE":
                logic.LogicElse = QuestLogicOp.CheckMiddleStatueCapture;
                args = 1;
                break;

            case "CHECK_EMPTY_SLOT":
                logic.LogicElse = QuestLogicOp.CheckEmptySlot;
                args = 1;
                break;

            case "CHECK_NO_CASTLE":
                logic.LogicElse = QuestLogicOp.CheckNoCastle;
                args = 1;
                break;

            case "CHECK_CASTLE":
                logic.LogicElse = QuestLogicOp.CheckCastle;
                args = 1;
                break;

            case "CHECK_MONSTER_CHALLENGE_USERCOUNT":
                logic.LogicElse = QuestLogicOp.CheckMonsterChallengeUserCount;
                args = 1;
                break;

            // The BEEF_ROAST checks never assign m_LogicElse upstream (it
            // stays LOGIC_CHECK_NONE) — kept verbatim, they always fail.
            case "CHECK_BEEF_ROAST_KARUS_VICTORY":
            case "CHECK_BEEF_ROAST_ELMORAD_VICTORY":
            case "CHECK_BEEF_ROAST_NO_VICTORY":
                args = 1;
                break;

            default:
                logger.LogWarning("QuestEventFile: unhandled A opcode '{Opcode}' ({Path}:{Line})",
                    tokens.ElementAtOrDefault(1), path, lineNumber);
                break;
        }

        for (int i = 0; i < args; i++)
            logic.Ints[i] = Atoi(tokens.ElementAtOrDefault(2 + i));

        logic.And = true;
        return logic;
    }
}
