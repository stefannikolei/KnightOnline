using System.Text;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>Stage-7.3 pins: the in-world entry handshake + entity stream parsing.</summary>
public class InGameWorldTests
{
    private sealed class CaptureClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public bool CryptionEnabled => true;

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public void EnableCryption(ulong publicKey) { }

        public byte LastOpcode => Sent.Count > 0 ? Sent[^1][0] : (byte)0;
    }

    private static (GameContext Ctx, CaptureClient Client) EnterGame()
    {
        var client = new CaptureClient();
        var ctx = new GameContext(client)
        {
            Spawn = new SelectCharResult(1, Zone: 21, X: 6500, Z: 5300, Y: 120, VictoryNation: 1),
        };
        ctx.Machine.SetActive(ctx.InGame);
        ctx.Machine.TickActive(); // InGame.Init → places local + sends GAMESTART phase 1
        return (ctx, client);
    }

    [Fact]
    public void Init_PlacesLocalPlayerFromSpawn_AndSendsGameStart()
    {
        (GameContext ctx, CaptureClient client) = EnterGame();

        Assert.True(ctx.InGame.Entered);
        Assert.Equal(650f, ctx.InGame.World.Local.X, 3);   // 6500 / 10
        Assert.Equal(530f, ctx.InGame.World.Local.Z, 3);
        Assert.Equal(12f, ctx.InGame.World.Local.Y, 3);
        Assert.Equal([(byte)GameOpcode.WIZ_GAMESTART, 0x01], client.Sent[0]);
    }

    [Fact]
    public void GameStart_ServerAck_RepliesPhaseTwo()
    {
        (GameContext ctx, CaptureClient client) = EnterGame();

        ctx.Machine.DispatchPacket([(byte)GameOpcode.WIZ_GAMESTART]);
        Assert.Equal([(byte)GameOpcode.WIZ_GAMESTART, 0x02], client.Sent[^1]);
    }

    [Fact]
    public void MyInfo_SetsFullStatBlockSkillsAndInventory()
    {
        (GameContext ctx, _) = EnterGame();
        bool raised = false;
        ctx.InGame.MyInfoReceived = _ => raised = true;

        ctx.Machine.DispatchPacket(MyInfoPacket());

        LocalPlayer local = ctx.InGame.World.Local;
        Assert.True(raised);
        Assert.Equal((short)77, local.SocketId);
        Assert.Equal("Hero", local.Name);
        Assert.Equal(660f, local.X, 3);
        Assert.Equal((short)105, local.Class);
        Assert.Equal((byte)72, local.Level);

        // Full stat block.
        Assert.Equal((short)1500, local.MaxHp);
        Assert.Equal((short)1490, local.Hp);
        Assert.Equal((short)800, local.MaxMp);
        Assert.Equal((byte)120, local.Str);
        Assert.Equal((byte)15, local.ItemStr);
        Assert.Equal((short)350, local.TotalHit);
        Assert.Equal((short)420, local.TotalAc);
        Assert.Equal(1_234_567, local.Gold);
        Assert.Equal(50u, local.Loyalty);
        Assert.Equal((byte)7, local.PoisonResist);

        // Skills [9].
        Assert.Equal((byte)9, local.Skills[0]);
        Assert.Equal((byte)1, local.Skills[8]);

        // Inventory: right-hand weapon in equip slot 6, one backpack stack.
        InventoryItem? weapon = ctx.InGame.Inventory.Get(6);
        Assert.NotNull(weapon);
        Assert.Equal(379001000, weapon!.ItemId);
        InventoryItem? potion = ctx.InGame.Inventory.Get(14);
        Assert.NotNull(potion);
        Assert.Equal(99, potion!.Count);
    }

    private static byte[] MyInfoPacket()
    {
        var buffer = new byte[1024];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_MYINFO);
        w.SetShort(77);                                    // socket id
        w.SetString1(Encoding.Latin1.GetBytes("Hero"));
        w.SetShort(6600);                                  // x*10
        w.SetShort(5400);                                  // z*10
        w.SetShort(130);                                   // y*10
        w.SetByte(1);                                      // nation
        w.SetByte(3);                                      // race
        w.SetShort(105);                                   // class
        w.SetByte(2);                                      // face
        w.SetByte(4);                                      // hair
        w.SetByte(0);                                      // rank
        w.SetByte(0);                                      // title
        w.SetByte(72);                                     // level
        w.SetByte(5);                                      // points
        w.SetDWord(9_999_999);                             // max exp
        w.SetDWord(1_000_000);                             // exp
        w.SetDWord(50);                                    // loyalty
        w.SetDWord(20);                                    // loyalty monthly
        w.SetByte(0);                                      // city
        w.SetShort(0);                                     // knights
        w.SetByte(0);                                      // fame
        // clan block (clan-less form)
        w.SetShort(0);                                     // alliance knights
        w.SetByte(0);                                      // flag
        w.SetString1([]);                                  // clan name
        w.SetByte(0);                                      // grade
        w.SetByte(0);                                      // ranking
        w.SetShort(0);                                     // mark version
        w.SetShort(-1);                                    // cape
        w.SetShort(1500);                                  // max hp
        w.SetShort(1490);                                  // hp
        w.SetShort(800);                                   // max mp
        w.SetShort(790);                                   // mp
        w.SetShort(3000);                                  // max weight
        w.SetShort(1200);                                  // cur weight
        w.SetByte(120); w.SetByte(15);                     // str, item str
        w.SetByte(110); w.SetByte(0);                      // sta, item sta
        w.SetByte(90); w.SetByte(0);                       // dex, item dex
        w.SetByte(80); w.SetByte(0);                       // int, item int
        w.SetByte(70); w.SetByte(0);                       // cha, item cha
        w.SetShort(350);                                   // total hit
        w.SetShort(420);                                   // total ac
        w.SetByte(1); w.SetByte(2); w.SetByte(3);          // fire, cold, lightning
        w.SetByte(4); w.SetByte(5); w.SetByte(7);          // magic, disease, poison
        w.SetDWord(1_234_567);                             // gold
        w.SetByte(1);                                      // authority
        w.SetByte(0); w.SetByte(0);                        // knights rank, personal rank
        byte[] skills = [9, 8, 7, 6, 5, 4, 3, 2, 1];
        foreach (byte s in skills)
            w.SetByte(s);
        for (int i = 0; i < WorldProtocol.InventorySlotCount; i++)
        {
            uint num = i switch { 6 => 379001000u, 14 => 810004000u, _ => 0u };
            short count = i == 14 ? (short)99 : (short)0;
            w.SetDWord(num);
            w.SetShort(0);                                 // duration
            w.SetShort(count);                             // count
            w.SetByte(0);                                  // flag
            w.SetShort(0);                                 // time remaining
        }

        w.SetByte(0);                                      // account status
        w.SetByte(1);                                      // premium type
        w.SetShort(300);                                   // premium time
        w.SetByte(0);                                      // is chicken
        w.SetDWord(1000);                                  // manner point
        return w.Written.ToArray();
    }

    [Fact]
    public void UserInOut_AddsThenRemovesRemotePlayer()
    {
        (GameContext ctx, _) = EnterGame();
        RemotePlayer? entered = null;
        short? left = null;
        ctx.InGame.PlayerEntered = p => entered = p;
        ctx.InGame.PlayerLeft = id => left = id;

        ctx.Machine.DispatchPacket(UserInPacket(42, "Rival", x: 6000, z: 5000, y: 100, direction: 90));
        Assert.Single(ctx.InGame.World.Players);
        Assert.NotNull(entered);
        Assert.Equal("Rival", entered!.Name);
        Assert.Equal(600f, entered.X, 3);
        Assert.Equal((short)90, entered.Direction);

        // USER_OUT drops it.
        ctx.Machine.DispatchPacket([(byte)GameOpcode.WIZ_USER_INOUT, 0x02, 42, 0]);
        Assert.Empty(ctx.InGame.World.Players);
        Assert.Equal((short)42, left);
    }

    [Fact]
    public void UserIn_CapturesVisibleEquipmentIds()
    {
        (GameContext ctx, _) = EnterGame();
        uint[] worn = [379001000, 379002000, 0, 379003000, 379004000, 0, 200100100, 600100000];
        ctx.Machine.DispatchPacket(UserInPacket(42, "Rival", 6000, 5000, 100, 0, worn));

        Assert.True(ctx.InGame.World.TryGet(42, out RemotePlayer rival));
        Assert.Equal(worn, rival.Items); // upper, lower, head, hands, feet, cloak, RH, LH
    }

    [Fact]
    public void Move_UpdatesRemoteAndLocalPositions()
    {
        (GameContext ctx, _) = EnterGame();
        ctx.InGame.World.Local.SocketId = 77;
        ctx.Machine.DispatchPacket(UserInPacket(42, "Rival", 6000, 5000, 100, 0));

        // Remote move.
        ctx.Machine.DispatchPacket(MovePacket(42, x: 6100, z: 5100, y: 110, speed: 40));
        Assert.True(ctx.InGame.World.TryGet(42, out RemotePlayer rival));
        Assert.Equal(610f, rival.X, 3);
        Assert.Equal(510f, rival.Z, 3);

        // Local move.
        ctx.Machine.DispatchPacket(MovePacket(77, x: 7000, z: 5500, y: 90, speed: 40));
        Assert.Equal(700f, ctx.InGame.World.Local.X, 3);
    }

    [Fact]
    public void Chat_RaisesMessage_AndBuildRequestLayout()
    {
        (GameContext ctx, CaptureClient client) = EnterGame();
        ChatMessage? received = null;
        ctx.InGame.ChatReceived = m => received = m;

        var buffer = new byte[64];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_CHAT);
        w.SetByte(2);   // type
        w.SetByte(1);   // nation
        w.SetShort(42); // id
        w.SetString1(Encoding.Latin1.GetBytes("Rival"));
        w.SetString2(Encoding.Latin1.GetBytes("hello there"));
        ctx.Machine.DispatchPacket(w.Written.ToArray());

        Assert.NotNull(received);
        Assert.Equal("Rival", received!.Value.Name);
        Assert.Equal("hello there", received.Value.Text);

        // Outgoing chat layout.
        ctx.InGame.SendChat(2, "hi");
        var r = new PacketReader(client.Sent[^1]);
        Assert.Equal((byte)GameOpcode.WIZ_CHAT, r.GetByte());
        Assert.Equal(2, r.GetByte());
        Assert.Equal("hi", Encoding.Latin1.GetString(r.GetVarString(2)));
    }

    private static byte[] MovePacket(short id, int x, int z, int y, int speed)
    {
        var buffer = new byte[16];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_MOVE);
        w.SetShort(id);
        w.SetShort(x);
        w.SetShort(z);
        w.SetShort(y);
        w.SetShort(speed);
        w.SetByte(0); // echo
        return w.Written.ToArray();
    }

    private static byte[] UserInPacket(short id, string name, int x, int z, int y, short direction, uint[]? items = null)
    {
        var buffer = new byte[256];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_USER_INOUT);
        w.SetByte(0x01); // USER_IN
        w.SetShort(id);
        // CUser::GetUserInfo blob (clan-less form).
        w.SetString1(Encoding.Latin1.GetBytes(name));
        w.SetByte(1);    // nation
        w.SetShort(0);   // knights
        w.SetByte(0);    // fame
        w.SetShort(0);   // alliance knights
        w.SetString1([]); // clan name
        w.SetByte(0);    // grade
        w.SetByte(0);    // ranking
        w.SetShort(0);   // mark version
        w.SetShort(-1);  // cape
        w.SetByte(60);   // level
        w.SetByte(3);    // race
        w.SetShort(105); // class
        w.SetShort(x);
        w.SetShort(z);
        w.SetShort(y);
        w.SetByte(2);    // face
        w.SetByte(4);    // hair
        w.SetByte(0);    // res hp type
        w.SetDWord(0);   // abnormal
        w.SetByte(0);    // need party
        w.SetByte(1);    // authority
        w.SetByte(0);    // party leader
        w.SetByte(0);    // invisibility
        w.SetShort(direction);
        w.SetByte(0);    // chicken
        w.SetByte(0);    // rank
        w.SetByte(0);    // knights rank
        w.SetByte(0);    // personal rank
        for (int i = 0; i < 8; i++)
        {
            w.SetDWord(items != null && i < items.Length ? items[i] : 0);
            w.SetShort(0);
            w.SetByte(0);
        }

        return w.Written.ToArray();
    }
}
