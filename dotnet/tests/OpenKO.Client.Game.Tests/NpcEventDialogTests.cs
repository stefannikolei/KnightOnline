using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.9 pins: the NPC event/vendor entry menu (CUINPCEvent) — the repair row visibility per
/// event kind and the Sale/Repair/close button events. Fully headless.
/// </summary>
public class NpcEventDialogTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public void Send(ReadOnlySpan<byte> payload) { }
        public void Connect(string host, int port) { }
        public bool CryptionEnabled => true;
        public void EnableCryption(ulong publicKey) { }
    }

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static N3UiButton Btn(string id) => new() { Id = id, Region = Rect(0, 0, 20, 20), ClickRect = Rect(0, 0, 20, 20) };

    private static N3UiString Str(string id) => new() { Id = id, Region = Rect(0, 0, 60, 16) };

    private static void Msg(UiControl root, UiControl sender, uint msg) => root.ReceiveMessage(sender, msg);

    private static (NpcEventDialog Dialog, UiControl Root) Build()
    {
        var context = new GameContext(new FakeGameClient());
        var node = new N3UiBase { Id = "npcevent", Region = Rect(0, 0, 200, 200) };
        node.Children.Add(Str("Text_Title"));
        node.Children.Add(Str("Text_Repair"));
        node.Children.Add(Btn("Btn_Sale"));
        node.Children.Add(Btn("Btn_Repair"));
        node.Children.Add(Btn("btn_close"));
        UiControl root = UiControlFactory.Build(node);
        return (new NpcEventDialog(context, root), root);
    }

    [Fact]
    public void Open_ItemTrade_HidesRepairRow()
    {
        (NpcEventDialog dialog, UiControl root) = Build();
        dialog.Open(NpcEventKind.ItemTrade, tradeId: 42, targetId: 7);

        Assert.True(root.Visible);
        Assert.Equal(42, dialog.TradeId);
        Assert.Equal(7, dialog.TargetId);
        Assert.False(dialog.RepairVisible);
        Assert.False(root.GetChildById<UiStringControl>("Text_Repair")!.Visible);
    }

    [Fact]
    public void Open_TradeRepair_ShowsRepairRow()
    {
        (NpcEventDialog dialog, _) = Build();
        dialog.Open(NpcEventKind.TradeRepair, tradeId: 1, targetId: 2);
        Assert.True(dialog.RepairVisible);
    }

    [Fact]
    public void SaleButton_RaisesSaleWithTradeIdAndHides()
    {
        (NpcEventDialog dialog, UiControl root) = Build();
        dialog.Open(NpcEventKind.ItemTrade, tradeId: 99, targetId: 3);

        int? sale = null;
        dialog.SaleRequested += t => sale = t;
        Msg(root, root.GetChildById<UiButton>("Btn_Sale")!, UiMsg.ButtonClick);

        Assert.Equal(99, sale);
        Assert.False(root.Visible);
    }

    [Fact]
    public void RepairButton_RaisesRepairAndHides()
    {
        (NpcEventDialog dialog, UiControl root) = Build();
        dialog.Open(NpcEventKind.TradeRepair, tradeId: 1, targetId: 2);

        bool repair = false;
        dialog.RepairRequested += () => repair = true;
        Msg(root, root.GetChildById<UiButton>("Btn_Repair")!, UiMsg.ButtonClick);

        Assert.True(repair);
        Assert.False(root.Visible);
    }

    [Fact]
    public void CloseButton_Hides()
    {
        (NpcEventDialog dialog, UiControl root) = Build();
        dialog.Open(NpcEventKind.ItemTrade, tradeId: 1, targetId: 2);
        Msg(root, root.GetChildById<UiButton>("btn_close")!, UiMsg.ButtonClick);
        Assert.False(root.Visible);
    }
}
