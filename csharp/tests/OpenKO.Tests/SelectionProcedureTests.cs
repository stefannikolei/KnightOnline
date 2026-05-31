using OpenKO.Game;
using OpenKO.Game.Procedures;
using OpenKO.Net;
using Xunit;

namespace OpenKO.Tests;

public class SelectionProcedureTests
{
    [Fact]
    public void ServerSelectionMovesAndTransitionsToCharacterSelect()
    {
        var ctx = new GameContext();
        ctx.Servers.Add(new GameServerInfo("1.1.1.1", "Ares", 10));
        ctx.Servers.Add(new GameServerInfo("2.2.2.2", "Dies", 20));
        var proc = new ServerSelectProcedure();

        ctx.Procedures.SetActive(proc);
        ctx.Procedures.TickActive(0.016f);
        proc.MoveSelection(1);
        Assert.True(proc.TrySelectCurrentServer());
        Assert.Equal("Dies", ctx.ServerName);

        ctx.Procedures.TickActive(0.016f);
        Assert.IsType<CharacterSelectProcedure>(ctx.Procedures.Active);
    }

    [Fact]
    public void CharacterSelectConsumesCharacterInfoPacket()
    {
        var ctx = new GameContext();
        var proc = new CharacterSelectProcedure();
        ctx.Procedures.SetActive(proc);
        ctx.Procedures.TickActive(0.016f);

        var pkt = new OpenKO.Common.Packet(OpenKO.Common.GameOpcode.AllCharInfoReq);
        pkt.Append((byte)0x01);
        AppendCharacter(pkt, "KnightA");
        AppendCharacter(pkt, "");
        AppendCharacter(pkt, "KnightC");

        Assert.True(ctx.Procedures.DispatchPacket(pkt));
        Assert.Equal(3, ctx.Characters.Count);
        Assert.Equal("KnightA", ctx.Characters[0].Name);
        Assert.Equal("KnightC", ctx.Characters[2].Name);
    }

    private static void AppendCharacter(OpenKO.Common.Packet pkt, string name)
    {
        pkt.Append((short)name.Length);
        if (name.Length > 0)
            pkt.Append(System.Text.Encoding.Latin1.GetBytes(name));
        pkt.Append((byte)1);    // race
        pkt.Append((short)101); // class
        pkt.Append((byte)10);   // level
        pkt.Append((byte)2);    // face
        pkt.Append((byte)3);    // hair
        pkt.Append((byte)5);    // zone
        for (int i = 0; i < 8; i++)
        {
            pkt.Append((uint)0);
            pkt.Append((short)0);
        }
    }
}
