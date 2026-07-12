using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser odds-and-ends slice (User.cpp): GM user counts, the server status
/// probe, the speed-hack timer, bug reports, WIZ_HOME and the GM kick.
/// </summary>
public sealed partial class GameUser
{
    /// <summary>m_fSpeedHackClientTime / m_fSpeedHackServerTime.</summary>
    public double SpeedHackClientTime;
    public double SpeedHackServerTime;

    /// <summary>CUser::CountConcurrentUser — GM-only in-game user count.</summary>
    public void CountConcurrentUser()
    {
        if (UserData is not { Authority: OpenKO.Data.GameConstants.AuthorityManager })
            return;

        int count = 0;
        foreach (GameUser? user in world.Users)
        {
            if (user is not null && user.State == ConnectionState.GameStart)
                ++count;
        }

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CONCURRENTUSER);
        writer.SetShort(count);
        Send(writer.Written);
    }

    /// <summary>CUser::ZoneConCurrentUsers — user count for a zone/nation pair.</summary>
    public void ZoneConCurrentUsers(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int zone = reader.GetShort();
        int nation = reader.GetByte();

        int count = 0;
        foreach (GameUser? user in world.Users)
        {
            if (user?.UserData is { } data && data.Zone == zone && data.Nation == nation)
                ++count;
        }

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ZONE_CONCURRENT);
        writer.SetShort(count);
        Send(writer.Written);
    }

    /// <summary>CUser::ServerStatusCheck — replies with the AI-link error count.</summary>
    public void ServerStatusCheck()
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_SERVER_CHECK);
        writer.SetShort(world.ErrorSocketCount);
        Send(writer.Written);
    }

    /// <summary>CUser::SpeedHackTime — client/server clock drift check.</summary>
    public void SpeedHackTime(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte first = reader.GetByte();
        float clientTime = reader.GetFloat();

        if (first != 0)
        {
            SpeedHackClientTime = clientTime;
            SpeedHackServerTime = world.Clock();
            return;
        }

        double serverGap = world.Clock() - SpeedHackServerTime;
        double clientGap = clientTime - SpeedHackClientTime;

        if (clientGap - serverGap > 10.0)
        {
            logger.LogDebug("SpeedHackTime: speed hack check performed on charId={CharId}", UserData?.CharId);
            Close?.Invoke();
        }
        else if (clientGap - serverGap < 0.0)
        {
            SpeedHackClientTime = clientTime;
            SpeedHackServerTime = world.Clock();
        }
    }

    /// <summary>CUser::ReportBug — logs the report text.</summary>
    public void ReportBug(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int length = reader.GetShort();
        if (length > 512 || length <= 0)
            return;

        string message = Encoding.Latin1.GetString(reader.GetString(length));

        if (UserData is not { } user || user.CharId.Length == 0)
            return;

        logger.LogInformation("ReportBug: [charId={CharId} chatMsg={Message}]", user.CharId, message);
    }

    /// <summary>CUser::Home — warps to the START_POSITION spawn box of the zone.</summary>
    public void Home()
    {
        if (!GetStartPosition(out short x, out short z))
            return;

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetShort((ushort)(x * 10));
        writer.SetShort((ushort)(z * 10));
        WarpProcess(writer.Written);
    }

    /// <summary>CUser::GetStartPosition.</summary>
    public bool GetStartPosition(out short x, out short z)
    {
        x = 0;
        z = 0;

        if (UserData is not { } user)
            return false;

        StartPosition? start = world.StartPositionTable.GetValueOrDefault(user.Zone);
        if (start is null)
            return false;

        if (user.Nation == 1)
        {
            x = (short)(start.KarusX + world.Rand(0, start.RangeX));
            z = (short)(start.KarusZ + world.Rand(0, start.RangeZ));
            return true;
        }

        if (user.Nation == 2)
        {
            x = (short)(start.ElmoX + world.Rand(0, start.RangeX));
            z = (short)(start.ElmoZ + world.Rand(0, start.RangeZ));
            return true;
        }

        return false;
    }

    /// <summary>CUser::KickOut — GM force-logout by account id.</summary>
    public void KickOut(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        int length = reader.GetShort();
        if (length > MaxIdSize || length <= 0)
            return;

        string accountId = Encoding.Latin1.GetString(reader.GetString(length));

        GameUser? target = world.GetUserByAccount(accountId);
        if (target is not null)
        {
            target.UserDataSaveToAgent();
            target.Close?.Invoke();
        }
        else
        {
            // The C++ forwards the kick to Aujard (cross-server); the port
            // calls the DB agent's account logout through the same hook.
            world.KickOutRequested?.Invoke(accountId);
        }
    }
}
