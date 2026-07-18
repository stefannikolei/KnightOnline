using OpenKO.Client.Game.Net;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-10.5 pin: the WIZ_TIME (0x13) game-clock packet.</summary>
public class TimeProtocolTests
{
    [Fact]
    public void Parse_ReadsFiveInt16AfterOpcode()
    {
        // [0x13][year][month][day][hour][minute], all i16 LE.
        byte[] packet = TimeProtocol.Build(2024, 3, 15, 13, 45);

        GameDateTime t = TimeProtocol.Parse(packet);

        Assert.Equal(2024, t.Year);
        Assert.Equal(3, t.Month);
        Assert.Equal(15, t.Day);
        Assert.Equal(13, t.Hour);
        Assert.Equal(45, t.Minute);
    }

    [Fact]
    public void Build_IsByteExact()
    {
        byte[] packet = TimeProtocol.Build(1, 2, 3, 4, 5);

        Assert.Equal(11, packet.Length);
        Assert.Equal(0x13, packet[0]); // WIZ_TIME
        // year=1 at [1..2], month=2 at [3..4], day=3 [5..6], hour=4 [7..8], minute=5 [9..10]
        Assert.Equal(new byte[] { 0x13, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0 }, packet);
    }
}
