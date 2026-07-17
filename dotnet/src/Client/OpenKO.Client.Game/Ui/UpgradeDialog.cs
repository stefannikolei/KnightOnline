using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the anvil upgrade-select window — port of <c>CUIUpgradeSelect</c>
/// (Client/WarFare/UIUpgradeSelect.cpp). The shipped <c>*_upgradeselect_*.uif</c> carries
/// <c>upgrade_1</c> (item upgrade), <c>upgrade_2</c> (accessory/ring upgrade) and
/// <c>btn_close</c>. The window is pushed open by the WIZ_ITEM_UPGRADE / ITEM_UPGRADE_REQ
/// reply (routed via <see cref="InGameState.ItemUpgradeReceived"/>), which also carries the
/// anvil NPC id.
///
/// <para>The two upgrade buttons open the item / ring upgrade dialogs — which are unimplemented
/// stubs in the upstream C++ (CUIUpgradeSelect only pops a "needs to be implemented" message
/// box). Faithfully, they raise <see cref="ItemUpgradeRequested"/> / <see cref="RingUpgradeRequested"/>
/// with the NPC id and the actual item-upgrade SEND is deferred (see <see cref="UpgradeProtocol"/>).</para>
///
/// <para>The blacksmith repair half of the anvil flow <em>is</em> ported: <see cref="RequestRepair"/>
/// sends the byte-exact WIZ_ITEM_REPAIR packet (CItemRepairMgr::Tick →
/// <see cref="RepairProtocol.BuildRepair"/>).</para>
/// </summary>
public sealed class UpgradeDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;

    public UpgradeDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The anvil NPC id from the last ITEM_UPGRADE_REQ (CUIUpgradeSelect::SetNpcID).</summary>
    public short NpcId { get; private set; }

    /// <summary>Raised by upgrade_1 (item upgrade — the anvil dialog is deferred).</summary>
    public event Action<short>? ItemUpgradeRequested;

    /// <summary>Raised by upgrade_2 (ring/accessory upgrade — the anvil dialog is deferred).</summary>
    public event Action<short>? RingUpgradeRequested;

    /// <summary>Wire the WIZ_ITEM_UPGRADE reply.</summary>
    public void Bind(InGameState inGame) => inGame.ItemUpgradeReceived += OnUpgrade;

    /// <summary>CGameProcMain::MsgRecv_ItemUpgrade — ITEM_UPGRADE_REQ opens the select window.</summary>
    public void OnUpgrade(byte sub, byte[] payload)
    {
        if (sub != (byte)UpgradeProtocol.Opcode.Req)
            return;

        UpgradeRequest req = UpgradeProtocol.ParseRequest(payload);
        NpcId = req.NpcId;
        _root.SetVisible(true);
    }

    /// <summary>
    /// CItemRepairMgr::Tick — request the NPC repair of an item. <paramref name="arm"/> is
    /// <see cref="RepairProtocol.ArmEquip"/> (equipped) or <see cref="RepairProtocol.ArmInventory"/>
    /// (backpack), <paramref name="order"/> the slot index, <paramref name="itemId"/> the full id.
    /// </summary>
    public byte[] RequestRepair(byte arm, byte order, uint itemId)
    {
        byte[] packet = RepairProtocol.BuildRepair(arm, order, itemId);
        _context.Client.Send(packet);
        return packet;
    }

    public void Close() => _root.SetVisible(false);

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) == 0)
            return;

        switch (sender.Id.ToLowerInvariant())
        {
            case "upgrade_1":
                ItemUpgradeRequested?.Invoke(NpcId);
                _root.SetVisible(false);
                break;

            case "upgrade_2":
                RingUpgradeRequested?.Invoke(NpcId);
                _root.SetVisible(false);
                break;

            case "btn_close":
                _root.SetVisible(false);
                break;
        }
    }
}
