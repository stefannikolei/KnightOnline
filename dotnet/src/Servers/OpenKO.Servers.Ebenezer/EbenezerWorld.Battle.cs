using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The EbenezerApp battle-event slice: the war open/close state machine
/// (BattleZoneOpenTimer), the game-time tick and the weather roll.
/// </summary>
public sealed partial class EbenezerWorld
{
    private const int ZoneIdBattleZone = 101;   // ZONE_BATTLE
    private const int ZoneIdFrontierZone = 201; // ZONE_FRONTIER

    private const byte NoBattleState = 0;    // NO_BATTLE
    private const byte NationBattleState = 1;
    private const byte SnowBattleState = 2;

    private const byte WeatherFine = 1; // WEATHER_FINE
    private const byte WeatherRain = 2;
    private const byte WeatherSnow = 3;

    /// <summary>m_byOldBattleOpen (survives the close for the snow scoring).</summary>
    public byte OldBattleOpen;

    /// <summary>m_byBattleSave — the victory row was written to the DB.</summary>
    public byte BattleSave;

    /// <summary>m_byBanishFlag / m_sBanishDelay — the war-end countdown.</summary>
    public byte BanishFlag;
    public short BanishDelay;

    /// <summary>m_sKarusCount / m_sElmoradCount (battle-zone headcount).</summary>
    public short KarusCount;
    public short ElmoradCount;

    /// <summary>m_bySanta (1 santa, 2 angel).</summary>
    public byte Santa;

    /// <summary>m_bPermanentChatMode / m_bPermanentChatFlag ('+permanent').</summary>
    public bool PermanentChatMode;
    public bool PermanentChatFlag;

    /// <summary>
    /// Send_UDP_All — the C++ server-to-server UDP channel. The port keeps it
    /// as an optional hook; the single-process topology does not need it.
    /// </summary>
    public Action<byte[]>? SendUdpAll;

    /// <summary>
    /// The WIZ_BATTLE_EVENT save to Aujard (UPDATE_BATTLE_EVENT proc): wired to
    /// IDbAgent.UpdateBattleEventAsync by the host.
    /// </summary>
    public Action<string, byte>? SaveBattleResult;

    /// <summary>EbenezerApp::BattleZoneOpen — open/close the war.</summary>
    public void BattleZoneOpen(byte type)
    {
        if (type == BattlezoneOpen)
        {
            BattleOpen = NationBattleState;
            OldBattleOpen = NationBattleState;
        }
        else if (type == SnowBattlezoneOpen)
        {
            BattleOpen = SnowBattleState;
            OldBattleOpen = SnowBattleState;
        }
        else if (type == BattlezoneClose)
        {
            BattleOpen = NoBattleState;
            Announcement(BattlezoneClose);
        }
        else
        {
            return;
        }

        Announcement(type);
        KickOutZoneUsers(ZoneIdFrontierZone);

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_BATTLE_EVENT);
        writer.SetByte(1); // BATTLE_EVENT_OPEN
        writer.SetByte(type);
        SendToAiServer?.Invoke(1000, writer.Written.ToArray());
    }

    /// <summary>EbenezerApp::KickOutZoneUsers — everyone in the zone goes home.</summary>
    public void KickOutZoneUsers(short zone)
    {
        foreach (GameUser? user in Users)
        {
            if (user?.UserData is not { } data || data.Zone != zone)
                continue;

            GameZone? map = GetZoneById(data.Nation);
            if (map is not null)
                user.ZoneChange(map.ZoneNumber, map.InitX, map.InitZ);
        }
    }

    /// <summary>EbenezerApp::BattleZoneOpenTimer — the war-end countdown stages.</summary>
    public void BattleZoneOpenTimer()
    {
        if (BattleOpen == NationBattleState)
            BattleZoneCurrentUsers();

        if (BanishFlag != 1)
            return;

        if (BanishDelay == 0)
        {
            BattleOpen = NoBattleState;
            KarusOpenFlag = 0;
            ElmoradOpenFlag = 0;
            KarusCaptain = string.Empty;
            ElmoradCaptain = string.Empty;

            if (ServerNo == 1) // KARUS
            {
                var buffer = new byte[8];
                var writer = new PacketWriter(buffer);
                writer.SetByte(0xD1); // UDP_BATTLE_EVENT_PACKET
                writer.SetByte(5);    // BATTLE_EVENT_KILL_USER
                writer.SetByte(1);
                writer.SetShort(KarusDead);
                writer.SetShort(ElmoradDead);
                SendUdpAll?.Invoke(writer.Written.ToArray());
            }
        }

        BanishDelay++;

        if (BanishDelay == 3)
        {
            int loserNation = 0;

            if (OldBattleOpen == SnowBattleState)
            {
                if (KarusDead > ElmoradDead)
                {
                    Victory = 2; // ELMORAD
                    loserNation = 1;
                }
                else if (KarusDead < ElmoradDead)
                {
                    Victory = 1; // KARUS
                    loserNation = 2;
                }
                else
                {
                    Victory = 0;
                }
            }

            if (Victory == 0)
            {
                BattleZoneOpen(BattlezoneClose);
            }
            else
            {
                if (Victory == 1)
                    loserNation = 2;
                else if (Victory == 2)
                    loserNation = 1;

                Announcement(DeclareWinner, Victory);
                Announcement(DeclareLoser, loserNation);
            }
        }
        else if (BanishDelay == 8)
        {
            Announcement(DeclareBan);
        }
        else if (BanishDelay == 10)
        {
            BanishLosers();
        }
        else if (BanishDelay == 20)
        {
            var buffer = new byte[4];
            var writer = new PacketWriter(buffer);
            writer.SetByte(AiOpcode.AG_BATTLE_EVENT);
            writer.SetByte(1); // BATTLE_EVENT_OPEN
            writer.SetByte(BattlezoneClose);
            SendToAiServer?.Invoke(1000, writer.Written.ToArray());
            ResetBattleZone();
        }
    }

    /// <summary>EbenezerApp::BattleZoneCurrentUsers — battle-zone headcount.</summary>
    public void BattleZoneCurrentUsers()
    {
        GameZone? map = GetZoneById(ZoneIdBattleZone);
        if (map is null || ServerNo != map.ServerNo)
            return;

        short karus = 0, elmorad = 0;
        foreach (GameUser? user in Users)
        {
            if (user?.UserData is not { } data || data.Zone != ZoneIdBattleZone)
                continue;

            if (data.Nation == 1)
                karus++;
            else if (data.Nation == 2)
                elmorad++;
        }

        KarusCount = karus;
        ElmoradCount = elmorad;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte(0xD3); // UDP_BATTLEZONE_CURRENT_USERS
        writer.SetShort(KarusCount);
        writer.SetShort(ElmoradCount);
        SendUdpAll?.Invoke(writer.Written.ToArray());
    }

    /// <summary>EbenezerApp::BanishLosers — commanders demoted, invaders sent home.</summary>
    public void BanishLosers()
    {
        foreach (GameUser? user in Users)
        {
            if (user?.UserData is not { } data)
                continue;

            if (data.Fame == 100) // COMMAND_CAPTAIN
            {
                data.Fame = 1; // CHIEF

                var buffer = new byte[8];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_AUTHORITY_CHANGE);
                writer.SetByte(1); // COMMAND_AUTHORITY
                writer.SetShort(user.SocketId);
                writer.SetByte(data.Fame);
                user.Send(writer.Written);
            }

            if (data.Zone != data.Nation)
                user.KickOutZoneUser(true);
        }
    }

    /// <summary>EbenezerApp::KickOutAllUsers (the C++ sleeps 1s per user; the port just closes).</summary>
    public void KickOutAllUsers()
    {
        foreach (GameUser? user in Users)
            user?.Close?.Invoke();
    }

    /// <summary>EbenezerApp::ResetBattleZone.</summary>
    public void ResetBattleZone()
    {
        Victory = 0;
        BanishFlag = 0;
        BanishDelay = 0;
        KarusFlag = 0;
        ElmoradFlag = 0;
        KarusOpenFlag = ElmoradOpenFlag = 0;
        BattleOpen = NoBattleState;
        OldBattleOpen = NoBattleState;
        KarusDead = ElmoradDead = 0;
        BattleSave = 0;
        KarusCount = 0;
        ElmoradCount = 0;
    }

    /// <summary>
    /// EbenezerApp::UpdateGameTime — one game minute per GameTimeTick (6s).
    /// The daily clan-rank refresh runs through the hook below.
    /// </summary>
    public Action? DailyKnightsRankRefresh;

    public void UpdateGameTime()
    {
        bool knights = false;

        Minute++;

        BattleZoneOpenTimer();

        if (Minute == 60)
        {
            Hour++;
            Minute = 0;

            UpdateWeather();

            if (Santa != 0)
                FlySanta();
        }

        if (Hour == 24)
        {
            Date++;
            Hour = 0;
            knights = true;
        }

        if (Date == 31)
        {
            Month++;
            Date = 1;
        }

        if (Month == 13)
        {
            Year++;
            Month = 1;
        }

        // The AI alive-check bookkeeping increments here like the C++.
        ErrorSocketCount++;

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_TIME_WEATHER);
        writer.SetShort(Year);
        writer.SetShort(Month);
        writer.SetShort(Date);
        writer.SetShort(Hour);
        writer.SetShort(Minute);
        writer.SetByte((byte)Weather);
        writer.SetShort(WeatherAmount);
        SendToAiServer?.Invoke(1000, writer.Written.ToArray());

        if (knights)
            DailyKnightsRankRefresh?.Invoke();
    }

    /// <summary>EbenezerApp::UpdateWeather — the hourly weather roll.</summary>
    public void UpdateWeather()
    {
        int result = Rand(0, 100);

        int weather;
        if (result < 2)
            weather = WeatherSnow;
        else if (result < 7)
            weather = WeatherRain;
        else
            weather = WeatherFine;

        WeatherAmount = (short)Rand(0, 100);

        // For WEATHER_FINE the amount doubles as the fog level.
        if (weather == WeatherFine)
        {
            if (WeatherAmount > 70)
                WeatherAmount /= 2;
            else
                WeatherAmount = 0;
        }

        Weather = (short)weather;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_WEATHER);
        writer.SetByte((byte)Weather);
        writer.SetShort(WeatherAmount);
        SendAll(writer.Written);
    }

    /// <summary>EbenezerApp::FlySanta.</summary>
    public void FlySanta()
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SANTA);
        writer.SetByte(Santa);
        SendAll(writer.Written);
    }

    /// <summary>CKnightsManager::RecvKnightsAllList — apply the daily rank refresh.</summary>
    public void ApplyKnightsRankUpdates(IEnumerable<(short Id, uint Points, byte Ranking)> entries)
    {
        var changed = new byte[512];
        var changedWriter = new PacketWriter(changed);
        int sendCount = 0;

        foreach ((short id, uint points, byte ranking) in entries)
        {
            KnightsClan? clan = Knights.GetValueOrDefault(id);
            if (clan is null)
                continue;

            if (clan.Points != (int)points)
            {
                clan.Points = (int)points;
                clan.Grade = GetKnightsGrade(clan.Points);

                changedWriter.SetShort(clan.Index);
                changedWriter.SetByte(clan.Grade);
                changedWriter.SetByte(clan.Ranking);
                sendCount++;
            }
            else if (clan.Ranking != ranking)
            {
                clan.Ranking = ranking;

                changedWriter.SetShort(clan.Index);
                changedWriter.SetByte(clan.Grade);
                changedWriter.SetByte(clan.Ranking);
                sendCount++;
            }
        }

        if (sendCount <= 0)
            return;

        var buffer = new byte[8 + changedWriter.Index];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(GameUser.KnightsAllListReq);
        writer.SetShort(sendCount);
        writer.SetString(changed.AsSpan(0, changedWriter.Index));
        SendAll(writer.Written);
    }
}
