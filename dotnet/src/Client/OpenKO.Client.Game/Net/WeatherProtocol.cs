using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>
/// The parsed WIZ_WEATHER push (CGameProcMain::MsgRecv_Weather):
/// <see cref="Type"/> is e_WeatherType (1 = fine, 2 = rain, 3 = snow),
/// <see cref="Amount"/> is the intensity percentage.
/// </summary>
public readonly record struct WeatherState(byte Type, short Amount);

/// <summary>
/// The WIZ_WEATHER (0x14) server→client message
/// (<c>[0x14][u8 type][i16 amount]</c>) — the global weather change.
/// </summary>
public static class WeatherProtocol
{
    /// <summary>WEATHER_FINE — clear skies (e_WeatherType).</summary>
    public const byte Fine = 1;

    /// <summary>WEATHER_RAIN.</summary>
    public const byte Rain = 2;

    /// <summary>WEATHER_SNOW.</summary>
    public const byte Snow = 3;

    /// <summary>Parse <c>[WIZ_WEATHER=0x14][u8 type][i16 amount]</c>.</summary>
    public static WeatherState Parse(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode WIZ_WEATHER
        byte type = r.GetByte();
        short amount = r.GetShort();
        return new WeatherState(type, amount);
    }

    /// <summary>Build <c>[WIZ_WEATHER=0x14][u8 type][i16 amount]</c> (server-side / tests).</summary>
    public static byte[] Build(byte type, short amount)
    {
        var buffer = new byte[4];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_WEATHER);
        w.SetByte(type);
        w.SetShort(amount);
        return w.Written.ToArray();
    }
}
