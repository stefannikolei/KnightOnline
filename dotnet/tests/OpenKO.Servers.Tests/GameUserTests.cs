using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Servers.Aujard;
using OpenKO.Servers.Ebenezer;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>Tests for the Ebenezer GameUser pre-game flow (version check + login).</summary>
public class GameUserTests
{
    private static (GameUser User, List<byte[]> Frames) MakeUser(
        EbenezerWorld world, FakeDbAgent db, short socketId = 0)
    {
        var frames = new List<byte[]>();
        short id = world.Register(i => new GameUser(i, world, db, NullLogger.Instance));
        GameUser user = world.Users[id]!;
        user.Transmit = frame =>
        {
            frames.Add(frame);
            return true;
        };
        return (user, frames);
    }

    /// <summary>Unwraps a plain (uncrypted) frame back to its payload.</summary>
    private static byte[] Unframe(byte[] frame)
    {
        int len = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2));
        return frame.AsSpan(4, len).ToArray();
    }

    private static byte[] LoginPacket(string account, string password)
    {
        byte[] acc = Encoding.Latin1.GetBytes(account);
        byte[] pwd = Encoding.Latin1.GetBytes(password);
        var packet = new byte[1 + 2 + acc.Length + 2 + pwd.Length];
        packet[0] = 0x01; // WIZ_LOGIN
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(1), (short)acc.Length);
        acc.CopyTo(packet, 3);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(3 + acc.Length), (short)pwd.Length);
        pwd.CopyTo(packet, 5 + acc.Length);
        return packet;
    }

    [Fact]
    public async Task VersionCheck_SendsVersionAndKeyThenEnablesCryption()
    {
        var world = new EbenezerWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        await user.ParsingAsync([0x2B]); // WIZ_VERSION_CHECK

        byte[] payload = Unframe(Assert.Single(frames));
        Assert.Equal(11, payload.Length);
        Assert.Equal(0x2B, payload[0]);
        Assert.Equal(1298, BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(1)));
        Assert.Equal(user.Core.Cryption.PublicKey, BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(3)));
        Assert.True(user.Core.CryptionEnabled);
    }

    [Fact]
    public async Task Login_Success_RepliesNationAndKeepsAccount()
    {
        var world = new EbenezerWorld();
        var db = new FakeDbAgent { AccountLogin = (_, _) => 1 }; // KARUS
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        await user.ParsingAsync(LoginPacket("tester", "secret"));

        Assert.Equal(("tester", "secret"), Assert.Single(db.LoginCalls));
        Assert.Equal(new byte[] { 0x01, 0x01 }, Unframe(Assert.Single(frames)));
        Assert.Equal("tester", user.AccountId);
    }

    [Fact]
    public async Task Login_Failure_RepliesFFAndClearsAccount()
    {
        var world = new EbenezerWorld();
        var db = new FakeDbAgent { AccountLogin = (_, _) => -1 };
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        await user.ParsingAsync(LoginPacket("tester", "wrong"));

        Assert.Equal(new byte[] { 0x01, 0xFF }, Unframe(Assert.Single(frames)));
        Assert.Equal(string.Empty, user.AccountId);
    }

    [Fact]
    public async Task Login_InvalidLengths_FailWithoutDbCall()
    {
        var world = new EbenezerWorld();
        var db = new FakeDbAgent();
        (GameUser user, List<byte[]> frames) = MakeUser(world, db);

        // 21-character account id exceeds MAX_ID_SIZE.
        await user.ParsingAsync(LoginPacket(new string('a', 21), "pw"));

        Assert.Empty(db.LoginCalls);
        Assert.Equal(new byte[] { 0x01, 0xFF }, Unframe(Assert.Single(frames)));
    }

    [Fact]
    public async Task Login_DuplicateAccount_KicksExistingSessionAndFails()
    {
        var world = new EbenezerWorld();
        var db = new FakeDbAgent { AccountLogin = (_, _) => 2 };

        (GameUser first, _) = MakeUser(world, db);
        bool firstClosed = false;
        first.Close = () => firstClosed = true;
        await first.ParsingAsync(LoginPacket("Tester", "pw"));

        (GameUser second, List<byte[]> secondFrames) = MakeUser(world, db);
        await second.ParsingAsync(LoginPacket("tester", "pw")); // case-insensitive match

        Assert.True(firstClosed);
        Assert.Equal(new byte[] { 0x01, 0xFF }, Unframe(Assert.Single(secondFrames)));
        Assert.Single(db.LoginCalls); // the duplicate never reaches the DB
    }
}
