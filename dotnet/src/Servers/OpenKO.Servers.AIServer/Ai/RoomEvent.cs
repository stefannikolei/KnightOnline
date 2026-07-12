using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>_RoomEvent (RoomEvent.h): one logic condition or exec statement.</summary>
public struct RoomEventLine
{
    public short Number;
    public short Option1;
    public short Option2;
}

/// <summary>
/// Port of <c>CRoomEvent</c> (Server/AIServer/RoomEvent.cpp): one dungeon room
/// with its trigger conditions (Logic), actions (Exec) and member NPC list.
/// Driven once per second by the zone-event tick while Status == 2.
/// </summary>
public sealed class RoomEvent
{
    public const int MaxCheckEvent = 5; // MAX_CHECK_EVENT

    private const byte WarSystemChat = 8; // WAR_SYSTEM_CHAT (shared/packets.h)
    private const byte SendAllTarget = 0x03; // SEND_ALL
    private const byte KarusZone = 1;   // KARUS_ZONE
    private const byte ElmoradZone = 2; // ELMORAD_ZONE
    private const byte BattleMapEventResult = 2; // BATTLE_MAP_EVENT_RESULT
    private const byte BattleEventResult = 3;    // BATTLE_EVENT_RESULT

    public int ZoneNumber;
    public short RoomNumber;

    /// <summary>m_byStatus: 1 init, 2 progress, 3 clear.</summary>
    public byte Status = 1;

    /// <summary>m_byCheck: number of logic conditions.</summary>
    public byte Check;

    /// <summary>m_byRoomType: 0 normal, 1 trap room, …</summary>
    public byte RoomType;

    public int InitMinX;
    public int InitMinZ;
    public int InitMaxX;
    public int InitMaxZ;

    public int EndMinX;
    public int EndMinZ;
    public int EndMaxX;
    public int EndMaxZ;

    public readonly RoomEventLine[] Logic = new RoomEventLine[MaxCheckEvent];
    public readonly RoomEventLine[] Exec = new RoomEventLine[MaxCheckEvent];

    public double DelayTime;

    /// <summary>m_mapRoomNpcArray: serials (m_sNid) of the room's NPCs.</summary>
    public readonly List<int> RoomNpcs = [];

    /// <summary>m_byLogicNumber: current condition index, 1-based.</summary>
    private byte _logicNumber = 1;

    public AiWorld? World;

    /// <summary>AIServerApp::Send(buf, len, zone) — AG packets to this zone's game server.</summary>
    public Action<byte[]>? SendToZone;

    /// <summary>CRoomEvent::MainRoom.</summary>
    public void MainRoom(double currentTime)
    {
        int eventNum = Logic[_logicNumber - 1].Number;

        if (!CheckEvent(eventNum, currentTime))
            return;

        eventNum = Exec[_logicNumber - 1].Number;
        if (RunEvent(eventNum))
            Status = 3;
    }

    /// <summary>CRoomEvent::CheckEvent — is the current logic condition met?</summary>
    public bool CheckEvent(int eventNum, double currentTime)
    {
        if (_logicNumber == 0 || _logicNumber > MaxCheckEvent)
            return false;

        switch (eventNum)
        {
            // Kill one specific monster.
            case 1:
            {
                Npc? npc = GetNpcPtr(Logic[_logicNumber - 1].Option1);
                if (npc is not null && npc.ChangeType == 100)
                    return true;
                break;
            }

            // Kill all monsters.
            case 2:
                return CheckMonsterCount(0, 0, 3);

            // Survive N minutes.
            case 3:
            {
                int seconds = Logic[_logicNumber - 1].Option1 * 60;
                if (currentTime >= DelayTime + seconds)
                    return true;
                break;
            }

            // Reach the goal area (resolved by MAP::IsRoomCheck instead).
            case 4:
                break;

            // Kill Option2 monsters of type Option1.
            case 5:
                return CheckMonsterCount(
                    Logic[_logicNumber - 1].Option1,
                    Logic[_logicNumber - 1].Option2, 1);
        }

        return false;
    }

    /// <summary>CRoomEvent::RunEvent — returns true when the room clears.</summary>
    public bool RunEvent(int eventNum)
    {
        switch (eventNum)
        {
            // Spawn another monster.
            case 1:
            {
                Npc? npc = GetNpcPtr(Exec[_logicNumber - 1].Option1);
                if (npc is not null)
                {
                    npc.ChangeType = 3;
                    npc.SetLive();
                }

                if (Check == _logicNumber)
                    return true;

                _logicNumber++;
                break;
            }

            // A door opens (the C++ only looks the NPC up and logs on failure).
            case 2:
                GetNpcPtr(Exec[_logicNumber - 1].Option1);

                if (Check == _logicNumber)
                    return true;

                _logicNumber++;
                break;

            // Transform into another monster.
            case 3:
                if (Check == _logicNumber)
                    return true;
                break;

            // Spawn Option2 monsters of type Option1.
            case 4:
                CheckMonsterCount(
                    Exec[_logicNumber - 1].Option1,
                    Exec[_logicNumber - 1].Option2, 2);

                if (Check == _logicNumber)
                    return true;

                _logicNumber++;
                break;

            // Clear announcement to the clients.
            case 100:
            {
                short option1 = Exec[_logicNumber - 1].Option1;
                short option2 = Exec[_logicNumber - 1].Option2;
                if (option1 != 0)
                    EndEventSay(option1, option2);

                if (Check == _logicNumber)
                    return true;

                _logicNumber++;
                break;
            }
        }

        return false;
    }

    /// <summary>CRoomEvent::GetNpcPtr — find the room NPC with the given table id (m_sSid).</summary>
    public Npc? GetNpcPtr(int sid)
    {
        if (RoomNpcs.Count == 0)
            return null;

        foreach (int nid in RoomNpcs)
        {
            if (nid < 0)
                continue;

            Npc? npc = World?.Npcs.GetValueOrDefault(nid);
            if (npc is null)
                continue;

            if (npc.Sid == sid)
                return npc;
        }

        return null;
    }

    /// <summary>
    /// CRoomEvent::CheckMonsterCount. Quirk kept verbatim: the C++ shadows the
    /// outer <c>nMonster</c> inside the snapshot block, so the scan loop runs
    /// over the OUTER variable which is still 0 — the function only ever takes
    /// the empty-map early-out and otherwise returns false without visiting a
    /// single NPC (types 1/2/3/4 are all dead code upstream).
    /// </summary>
    public bool CheckMonsterCount(int sid, int count, int type)
    {
        if (RoomNpcs.Count == 0)
            return false;

        int monsterTotal = 0; // the OUTER nMonster the C++ loop reads — never assigned
        int[] idList = [.. RoomNpcs];
        int monsterCount = 0;
        bool retValue = false;

        for (int i = 0; i < monsterTotal; i++)
        {
            int nid = idList[i];
            if (nid < 0)
                continue;

            Npc? npc = World?.Npcs.GetValueOrDefault(nid);
            if (npc is null)
                continue;

            if (type == 4)
            {
                if (npc.RegenType == 2)
                    npc.RegenType = 0;

                npc.ChangeType = 0;
            }
            else if (type == 3)
            {
                if (npc.DeadType == 100)
                    monsterCount++;

                if (monsterCount == idList.Length)
                    retValue = true;
            }
            else if (npc.Sid == sid)
            {
                if (type == 1)
                {
                    if (npc.ChangeType == 100)
                        monsterCount++;

                    if (monsterCount == count)
                        retValue = true;
                }
                else if (type == 2)
                {
                    npc.ChangeType = 3;
                    monsterCount++;

                    if (monsterCount == count)
                        retValue = true;
                }
            }
        }

        return retValue;
    }

    /// <summary>CRoomEvent::InitializeRoom.</summary>
    public void InitializeRoom()
    {
        Status = 1;
        DelayTime = 0.0;
        _logicNumber = 1;

        CheckMonsterCount(0, 0, 4); // reset the members' ChangeType (dead code, see quirk)
    }

    /// <summary>CRoomEvent::EndEventSay — clear announcements / battle-event packets.</summary>
    public void EndEventSay(int option1, int option2)
    {
        switch (option1)
        {
            case 1:
            {
                string msg = option2 switch
                {
                    1 => "Karus' first fort was captured.",
                    2 => "Karus' second fort was captured.",
                    11 => "Elmorad's first fort was captured.",
                    12 => "Elmorad's second fort was captured.",
                    _ => string.Empty,
                };

                SendSystemMsg(msg);
                break;
            }

            case 2:
            {
                string msg = string.Empty;
                if (option2 == KarusZone)
                {
                    msg = "*** The path to Karus has been opened. ***";
                    SendBattleEvent(BattleMapEventResult, KarusZone);
                }
                else if (option2 == ElmoradZone)
                {
                    msg = "*** The path to Elmorad has been opened. ***";
                    SendBattleEvent(BattleMapEventResult, ElmoradZone);
                }

                SendSystemMsg(msg);
                break;
            }

            case 3:
                if (option2 == KarusZone)
                    SendBattleEvent(BattleEventResult, KarusZone);
                else if (option2 == ElmoradZone)
                    SendBattleEvent(BattleEventResult, ElmoradZone);
                break;
        }
    }

    private void SendBattleEvent(byte kind, byte nation)
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_BATTLE_EVENT);
        writer.SetByte(kind);
        writer.SetByte(nation);
        SendToZone?.Invoke(writer.Written.ToArray());
    }

    /// <summary>AIServerApp::SendSystemMsg(msg, zone, WAR_SYSTEM_CHAT, SEND_ALL).</summary>
    private void SendSystemMsg(string msg)
    {
        byte[] msgBytes = System.Text.Encoding.Latin1.GetBytes(msg);
        var buffer = new byte[6 + msgBytes.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_SYSTEM_MSG);
        writer.SetByte(WarSystemChat);
        writer.SetShort(SendAllTarget);
        writer.SetString2(msgBytes);
        SendToZone?.Invoke(writer.Written.ToArray());
    }
}
