using OpenKO.Common;
using OpenKO.Net;
using Xunit;

namespace OpenKO.Tests;

public class GameProtocolTests
{
    [Fact]
    public void AllCharacterInfoRequestIsJustOpcode()
    {
        Packet pkt = GameProtocol.BuildAllCharacterInfoRequest();
        Assert.Equal((byte)GameOpcode.AllCharInfoReq, pkt.Opcode);
        Assert.Equal(1, pkt.Size);
    }

    [Fact]
    public void CharacterSelectPacketHasLengthPrefixedStrings()
    {
        Packet pkt = GameProtocol.BuildCharacterSelect("hero", "Knight", 1, 21);
        Assert.Equal((byte)GameOpcode.SelChar, pkt.Opcode);

        pkt.SyncForRead();
        Assert.Equal((byte)GameOpcode.SelChar, pkt.Read<byte>());
        pkt.DByte();
        Assert.True(pkt.ReadString(out string account));
        Assert.True(pkt.ReadString(out string character));
        Assert.Equal("hero", account);
        Assert.Equal("Knight", character);
        Assert.Equal((byte)1, pkt.Read<byte>());
        Assert.Equal((byte)21, pkt.Read<byte>());
    }

    [Fact]
    public void CharacterListRoundTripsCoreFields()
    {
        var pkt = new Packet(GameOpcode.AllCharInfoReq);
        pkt.Append((byte)0x01);

        AppendCharacter(pkt, "Alpha", race: 1, classId: 101, level: 10, face: 2, hair: 3, zone: 5);
        AppendCharacter(pkt, "", race: 0, classId: 0, level: 0, face: 0, hair: 0, zone: 0);
        AppendCharacter(pkt, "Gamma", race: 2, classId: 202, level: 55, face: 4, hair: 7, zone: 48);

        CharacterListResult parsed = GameProtocol.ParseCharacterList(pkt);
        Assert.True(parsed.Success);
        Assert.Equal(3, parsed.Characters.Count);
        Assert.Equal("Alpha", parsed.Characters[0].Name);
        Assert.Equal((byte)5, parsed.Characters[0].Zone);
        Assert.Equal(string.Empty, parsed.Characters[1].Name);
        Assert.Equal("Gamma", parsed.Characters[2].Name);
        Assert.Equal((short)202, parsed.Characters[2].ClassId);
    }

    private static void AppendCharacter(Packet pkt, string name, byte race, short classId, byte level, byte face, byte hair, byte zone)
    {
        pkt.Append((short)name.Length);
        if (name.Length > 0)
            pkt.Append(System.Text.Encoding.Latin1.GetBytes(name));
        pkt.Append(race);
        pkt.Append(classId);
        pkt.Append(level);
        pkt.Append(face);
        pkt.Append(hair);
        pkt.Append(zone);

        for (int i = 0; i < 8; i++)
        {
            pkt.Append((uint)0);
            pkt.Append((short)0);
        }
    }
}
