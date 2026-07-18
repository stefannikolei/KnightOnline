using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The parsed WIZ_TIME push (CGameProcMain::MsgRecv_Time): the authoritative
/// server game clock — year/month/day plus the hour+minute of the game day.
/// <see cref="Month"/> and <see cref="Day"/> drive the moon phase
/// (<c>month*30 + day</c>); <see cref="Hour"/>/<see cref="Minute"/> seed the
/// day-fraction (CN3SkyMng::SetGameTime).
/// </summary>
public readonly record struct GameDateTime(short Year, short Month, short Day, short Hour, short Minute);

/// <summary>
/// The WIZ_TIME (0x13) server→client message — five int16 after the opcode:
/// <c>[0x13][i16 year][i16 month][i16 day][i16 hour][i16 minute]</c>
/// (CUser::SendTimeStatus / CGameProcMain::MsgRecv_Time).
/// </summary>
public static class TimeProtocol
{
    /// <summary>Parse <c>[WIZ_TIME=0x13][i16 year][i16 month][i16 day][i16 hour][i16 minute]</c>.</summary>
    public static GameDateTime Parse(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_TIME
        short year = r.GetShort();
        short month = r.GetShort();
        short day = r.GetShort();
        short hour = r.GetShort();
        short minute = r.GetShort();
        return new GameDateTime(year, month, day, hour, minute);
    }

    /// <summary>Build <c>[WIZ_TIME=0x13][i16 year][i16 month][i16 day][i16 hour][i16 minute]</c> (server-side / tests).</summary>
    public static byte[] Build(short year, short month, short day, short hour, short minute)
    {
        var buffer = new byte[11];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_TIME);
        w.SetShort(year);
        w.SetShort(month);
        w.SetShort(day);
        w.SetShort(hour);
        w.SetShort(minute);
        return w.Written.ToArray();
    }
}
