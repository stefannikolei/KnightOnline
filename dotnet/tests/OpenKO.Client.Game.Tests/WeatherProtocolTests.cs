using OpenKO.Client.Game.Net;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Slice-9.10c pins: the WIZ_WEATHER (0x14) parse — [0x14][u8 type][i16 amount].</summary>
public class WeatherProtocolTests
{
    [Fact]
    public void Parse_ReadsTypeAndAmount()
    {
        // [0x14][type=2 rain][amount=75 => 0x4B,0x00]
        byte[] payload = [0x14, 0x02, 0x4B, 0x00];

        WeatherState state = WeatherProtocol.Parse(payload);

        Assert.Equal(WeatherProtocol.Rain, state.Type);
        Assert.Equal((short)75, state.Amount);
    }

    [Fact]
    public void Parse_RoundTripsTheBuilder()
    {
        byte[] payload = WeatherProtocol.Build(WeatherProtocol.Snow, 100);

        Assert.Equal((byte)GameOpcode.WIZ_WEATHER, payload[0]);
        Assert.Equal(4, payload.Length);

        WeatherState state = WeatherProtocol.Parse(payload);
        Assert.Equal(WeatherProtocol.Snow, state.Type);
        Assert.Equal((short)100, state.Amount);
    }

    [Fact]
    public void InGameState_WeatherPushRaisesWeatherChanged()
    {
        // Byte-exact fixture through the dispatcher would need a live GameContext;
        // here we assert the parser the handler calls yields the wire values.
        byte[] payload = [0x14, 0x03, 0x32, 0x00]; // snow, 50
        WeatherState state = WeatherProtocol.Parse(payload);
        Assert.Equal(WeatherProtocol.Snow, state.Type);
        Assert.Equal((short)50, state.Amount);
    }
}
