using System.Buffers.Binary;
using System.Globalization;
using OpenKO.Client.Assets;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller + state machine for the player-to-player TRADE window — port of
/// <c>CUIPerTradeDlg</c> (Client/WarFare/UIPerTradeDlg.cpp) and <c>CSubProcPerTrade</c>
/// (Client/WarFare/SubProcPerTrade.cpp), driving the WIZ_EXCHANGE (0x30) sub-protocol built by
/// <see cref="ExchangeProtocol"/>.
///
/// <para>Three icon grids share the window: my offer (<see cref="UiAreaType.PerTradeMy"/>, 12
/// slots), the other player's offer (<see cref="UiAreaType.PerTradeOther"/>, 12 slots) and a
/// mirror of my backpack (<see cref="UiAreaType.PerTradeInv"/>, 28 slots) that the trade window
/// borrows while open. Dragging an inv-mirror icon onto a my-offer slot adds the item
/// (N3_SP_PER_TRADE_ADD → <see cref="ExchangeProtocol.BuildAdd"/>); a countable stack first opens
/// the shared <see cref="CountableItemEditDialog"/> for the amount. <c>btn_gold</c> opens the same
/// popup and offers gold (dwGold pseudo-item at position 0xFF). <c>btn_trade_my</c> readies the
/// trade (N3_SP_PER_TRADE_DECIDE), <c>btn_close</c> cancels (N3_SP_PER_TRADE_CANCEL).</para>
///
/// <para>The state machine (<see cref="State"/>) mirrors <c>e_PerTradeState</c>. I initiate via
/// <see cref="RequestTrade"/> (NONE→WaitForReq); an incoming request raises
/// <see cref="PermitRequested"/> (NONE→WaitForMyDecision) which the host answers with
/// <see cref="AcceptRequest"/>/<see cref="RejectRequest"/>. Both AGREE(1) paths enter Normal and
/// pull the backpack into the inv-mirror. <see cref="OnExchange"/> is the single receive entry
/// point. Pure/headless; the host feeds <see cref="Cursor"/> and optionally binds a
/// <see cref="MessageBoxDialog"/> for the prompts.</para>
///
/// <para>Scope notes: (1) the C++ uses a separate <c>CUITradeEditDlg</c> for the gold amount but
/// the popup mechanism is identical, so the shared <see cref="CountableItemEditDialog"/> is reused
/// for both the item split and the gold entry. (2) The OTHER grid placement is dead in the upstream
/// C++ (icons are built but the <c>m_pPerTradeOther[i] = spItem</c> store lines are commented out,
/// and the destination <c>GetChildAreaByiOrder(UI_AREA_TYPE_PER_TRADE_OTHER, i)</c> is left null);
/// this port stores them into <see cref="OtherItems"/> faithfully so the offer is actually visible.
/// (3) "remove item / lock" has no opcode — the server recomputes on each ADD and readiness is the
/// single DECIDE — so none is invented.</para>
/// </summary>
public sealed class PerTradeDialog
{
    /// <summary>MAX_ITEM_PER_TRADE — my/other offer slot counts.</summary>
    public const int MaxItemPerTrade = 12;

    /// <summary>MAX_ITEM_INVENTORY — the trade window's backpack mirror.</summary>
    public const int MaxItemInventory = Inventory.BackpackSlotCount;

    /// <summary>dwGold (SubProcPerTrade.h:43) — the gold pseudo-item id.</summary>
    public const uint GoldSentinel = 900000000;

    /// <summary>The 0xFF slot position the ADD packet carries for gold.</summary>
    public const byte GoldPosition = 0xFF;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly ItemTableSet _items;
    private readonly IconDragState _drag;
    private readonly CountableItemEditDialog? _countableEdit;
    private readonly MessageBoxDialog? _messageBox;

    private readonly UiStringControl? _strInvGold;   // string_money_inv — my total gold
    private readonly UiStringControl? _strMyGold;     // string_money_my — gold I offered
    private readonly UiStringControl? _strOtherGold;  // string_money_other — gold they offered
    private readonly UiControl? _btnTradeMy;
    private readonly UiControl? _btnTradeOther;

    private readonly UiAreaControl?[] _myArea = new UiAreaControl?[MaxItemPerTrade];
    private readonly UiAreaControl?[] _otherArea = new UiAreaControl?[MaxItemPerTrade];
    private readonly UiAreaControl?[] _invArea = new UiAreaControl?[MaxItemInventory];
    private readonly UiIconControl?[] _myIcons = new UiIconControl?[MaxItemPerTrade];
    private readonly UiIconControl?[] _otherIcons = new UiIconControl?[MaxItemPerTrade];
    private readonly UiIconControl?[] _invIcons = new UiIconControl?[MaxItemInventory];
    private readonly UiStringControl?[] _myCountStr = new UiStringControl?[MaxItemPerTrade];
    private readonly UiStringControl?[] _otherCountStr = new UiStringControl?[MaxItemPerTrade];
    private readonly UiStringControl?[] _invCountStr = new UiStringControl?[MaxItemInventory];

    private readonly PerTradeIconItem?[] _my = new PerTradeIconItem?[MaxItemPerTrade];
    private readonly PerTradeIconItem?[] _other = new PerTradeIconItem?[MaxItemPerTrade];
    private readonly PerTradeIconItem?[] _inv = new PerTradeIconItem?[MaxItemInventory];

    private Inventory? _inventory;
    private LocalPlayer? _local;
    private int _myGold;
    private int _otherGold;
    private short _otherId = -1;

    // The in-flight item ADD (inv-mirror source order → my slot) awaiting the ADD result byte.
    private PendingAdd? _pendingAdd;

    private sealed record PendingAdd(int InvOrder, int MyOrder, PerTradeIconItem Item, int Count, bool CreatedMySlot);

    /// <summary>The opaque per-icon payload (__IconItemSkill analog).</summary>
    public sealed record PerTradeIconItem(int ItemId, int Count, short Durability, ItemBasicRow? Basic, ItemExtRow? Ext);

    public PerTradeDialog(
        GameContext context, UiControl root, ItemTableSet items, IconDragState drag,
        CountableItemEditDialog? countableEdit = null, MessageBoxDialog? messageBox = null)
    {
        _context = context;
        _root = root;
        _items = items;
        _drag = drag;
        _countableEdit = countableEdit;
        _messageBox = messageBox;

        _strInvGold = root.GetChildById<UiStringControl>("string_money_inv");
        _strMyGold = root.GetChildById<UiStringControl>("string_money_my");
        _strOtherGold = root.GetChildById<UiStringControl>("string_money_other");
        _btnTradeMy = root.GetChildById("btn_trade_my");
        _btnTradeOther = root.GetChildById("btn_trade_other");

        BuildIcons();

        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The live cursor position (fed each frame; tests set it directly).</summary>
    public UiPoint Cursor { get; set; }

    /// <summary>The current trade state (e_PerTradeState).</summary>
    public PerTradeState State { get; private set; } = PerTradeState.None;

    /// <summary>The socket id of the other trader (-1 when none).</summary>
    public short OtherId => _otherId;

    /// <summary>Gold I have offered in the trade window.</summary>
    public int MyGold => _myGold;

    /// <summary>Gold the other player has offered.</summary>
    public int OtherGold => _otherGold;

    /// <summary>Whether the other player has pressed their ready button.</summary>
    public bool OtherReady { get; private set; }

    public IReadOnlyList<PerTradeIconItem?> MyItems => _my;

    public IReadOnlyList<PerTradeIconItem?> OtherItems => _other;

    public IReadOnlyList<PerTradeIconItem?> InvItems => _inv;

    public IReadOnlyList<UiIconControl?> MyIcons => _myIcons;

    public IReadOnlyList<UiIconControl?> OtherIcons => _otherIcons;

    public IReadOnlyList<UiIconControl?> InvIcons => _invIcons;

    /// <summary>
    /// Raised on an incoming trade request (recv REQ) with the requester's socket id. The host
    /// answers with <see cref="AcceptRequest"/>/<see cref="RejectRequest"/>. When a
    /// <see cref="MessageBoxDialog"/> was bound the yes/no box is shown automatically.
    /// </summary>
    public event Action<short>? PermitRequested;

    /// <summary>Raised after <see cref="RequestTrade"/> is sent (NONE→WaitForReq), for a wait prompt.</summary>
    public event Action<short>? WaitingForResponse;

    /// <summary>Raised on the final trade result (true = DONE success, false = cancel/fail).</summary>
    public event Action<bool>? TradeFinished;

    // ---- Runtime icon construction ----------------------------------------

    private void BuildIcons()
    {
        for (int i = 0; i < MaxItemPerTrade; i++)
        {
            _myArea[i] = _root.GetChildAreaByOrder(UiAreaType.PerTradeMy, i);
            _otherArea[i] = _root.GetChildAreaByOrder(UiAreaType.PerTradeOther, i);
            _myIcons[i] = MakeIcon(_myArea[i]);
            _otherIcons[i] = MakeIcon(_otherArea[i]);
            _myCountStr[i] = GetChildStringByOrder(i);       // my count strings: order i
            _otherCountStr[i] = GetChildStringByOrder(i + 100); // other: i + 100
        }

        for (int i = 0; i < MaxItemInventory; i++)
        {
            _invArea[i] = _root.GetChildAreaByOrder(UiAreaType.PerTradeInv, i);
            _invIcons[i] = MakeIcon(_invArea[i]);
            _invCountStr[i] = GetChildStringByOrder(i + 200); // inv: i + 200
        }
    }

    /// <summary>CN3UIWndBase::GetChildStringByiOrder — a string child whose id is the decimal order.</summary>
    private UiStringControl? GetChildStringByOrder(int order) =>
        _root.GetChildById<UiStringControl>(order.ToString(CultureInfo.InvariantCulture));

    private UiIconControl MakeIcon(UiAreaControl? area)
    {
        UiIconControl icon = UiIconControl.CreateRuntime(area?.Region ?? default);
        icon.DragState = _drag;
        icon.SetVisible(false);
        _root.AddChild(icon);
        return icon;
    }

    // ---- Binding + initiate ------------------------------------------------

    public void Bind(InGameState inGame)
    {
        _local = inGame.World.Local;
        _inventory = inGame.Inventory;
        inGame.ExchangeReceived += OnExchange;
    }

    /// <summary>
    /// CGameProcMain::MsgSend_PerTradeReq — ask the target to trade (NONE→WaitForReq). Sends the
    /// REQ packet and raises <see cref="WaitingForResponse"/>. No-op if a trade is already active.
    /// </summary>
    public byte[]? RequestTrade(short targetId)
    {
        if (State != PerTradeState.None)
            return null;

        _otherId = targetId;
        State = PerTradeState.WaitForReq;
        byte[] packet = ExchangeProtocol.BuildRequest(targetId, ExchangeProtocol.TradeTypeNormal);
        _context.Client.Send(packet);
        WaitingForResponse?.Invoke(targetId);
        return packet;
    }

    /// <summary>
    /// ProcessProceed(PER_TRADE_RESULT_MY_AGREE) — accept an incoming request: send AGREE(1) and
    /// enter Normal immediately (the receiver's PerTradeCoreStart runs without waiting).
    /// </summary>
    public byte[]? AcceptRequest()
    {
        if (State != PerTradeState.WaitForMyDecision)
            return null;

        byte[] packet = ExchangeProtocol.BuildAgree(true);
        _context.Client.Send(packet);
        EnterNormal();
        return packet;
    }

    /// <summary>LeavePerTradeState(PER_TRADE_RESULT_MY_DISAGREE) — reject an incoming request: AGREE(0), back to NONE.</summary>
    public byte[]? RejectRequest()
    {
        if (State != PerTradeState.WaitForMyDecision)
            return null;

        byte[] packet = ExchangeProtocol.BuildAgree(false);
        _context.Client.Send(packet);
        Finalize(success: false);
        return packet;
    }

    // ---- Receive (MsgRecv_PerTrade) ---------------------------------------

    /// <summary>
    /// CGameProcMain::MsgRecv_PerTrade — the single WIZ_EXCHANGE receive entry point.
    /// <paramref name="payload"/> is the full packet ([0x30][sub]…).
    /// </summary>
    public void OnExchange(byte sub, byte[] payload)
    {
        switch (sub)
        {
            case ExchangeProtocol.Request:  // 0x01
                OnRecvRequest(BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(2)));
                break;

            case ExchangeProtocol.Agree:    // 0x02
                OnRecvAgree(payload.Length > 2 && payload[2] == 0x01);
                break;

            case ExchangeProtocol.Add:      // 0x03
                OnRecvAdd(payload.Length > 2 && payload[2] == 0x01);
                break;

            case ExchangeProtocol.OtherAdd: // 0x04
                OnRecvOtherAdd(payload);
                break;

            case ExchangeProtocol.OtherDecide: // 0x06
                OnRecvOtherDecide();
                break;

            case ExchangeProtocol.Done:     // 0x07
                OnRecvDone(payload);
                break;

            case ExchangeProtocol.Cancel:   // 0x08
                OnRecvCancel();
                break;
        }
    }

    private void OnRecvRequest(short otherId)
    {
        // Auto-reject if already trading (ReceiveMsgPerTradeReq only runs from NONE).
        if (State != PerTradeState.None)
            return;

        _otherId = otherId;
        State = PerTradeState.WaitForMyDecision;
        PermitRequested?.Invoke(otherId);

        _messageBox?.Show(
            $"Accept trade request?", string.Empty, MessageBoxStyle.YesNo, r =>
            {
                if (r == MessageBoxResult.Yes)
                    AcceptRequest();
                else
                    RejectRequest();
            });
    }

    private void OnRecvAgree(bool accepted)
    {
        // Initiator side: the target answered my request.
        if (accepted)
            EnterNormal();
        else
            Finalize(success: false);
    }

    private void OnRecvAdd(bool ok)
    {
        _drag.WaitFromServer = false;

        if (ok)
        {
            _pendingAdd = null; // committed
        }
        else if (_pendingAdd is { } add)
        {
            RollbackAdd(add);
            _pendingAdd = null;
        }

        _drag.Reset();
        Populate();
    }

    private void OnRecvOtherAdd(byte[] payload)
    {
        int itemId = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2));
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(6));
        short durability = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(10));

        if ((uint)itemId == GoldSentinel)
        {
            _otherGold += count;
            UpdateGoldStrings();
            return;
        }

        (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find((uint)itemId);
        bool countable = basic?.Countable == true;

        // Stack onto an existing matching slot when countable; else find the first empty slot.
        int dest = -1;
        if (countable)
        {
            for (int i = 0; i < MaxItemPerTrade; i++)
            {
                if (_other[i] != null && _other[i]!.ItemId == itemId)
                {
                    dest = i;
                    break;
                }
            }
        }

        if (dest < 0)
        {
            for (int i = 0; i < MaxItemPerTrade; i++)
            {
                if (_other[i] == null)
                {
                    dest = i;
                    break;
                }
            }
        }

        if (dest < 0)
            return; // no free slot — matches the C++ early return

        _other[dest] = _other[dest] is { } existing && countable
            ? existing with { Count = existing.Count + count }
            : new PerTradeIconItem(itemId, count, durability, basic, ext);

        PopulateOther();
    }

    private void OnRecvOtherDecide()
    {
        OtherReady = true;
        _btnTradeOther?.SetVisible(true); // the indicator; C++ disables it (already "pressed")
    }

    private void OnRecvDone(byte[] payload)
    {
        bool ok = payload.Length > 2 && payload[2] == 0x01;
        if (!ok)
        {
            // ReceiveMsgPerTradeDoneFail → restore my offer + gold, back to NONE.
            Finalize(success: false);
            return;
        }

        int totalGold = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(3));
        short itemCount = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(7));

        if (_local != null)
            _local.Gold = totalGold; // authoritative post-trade gold

        int off = 9;
        for (int i = 0; i < itemCount; i++)
        {
            byte pos = payload[off];
            int itemId = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(off + 1));
            short count = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(off + 5));
            short durability = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(off + 7));
            off += 9;
            ApplyDoneItemMove(pos, itemId, count, durability);
        }

        CommitInvToInventory();
        Finalize(success: true);
    }

    private void OnRecvCancel()
    {
        // ReceiveMsgPerTradeCancel → restore, back to NONE.
        Finalize(success: false);
    }

    /// <summary>
    /// ReceiveMsgPerTradeDoneItemMove — settle a received/remaining item into the inv-mirror slot
    /// <paramref name="pos"/> (stacking a countable onto a matching id, else replacing the slot).
    /// </summary>
    private void ApplyDoneItemMove(byte pos, int itemId, int count, short durability)
    {
        if (pos >= MaxItemInventory)
            return;

        (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find((uint)itemId);
        PerTradeIconItem? existing = _inv[pos];

        _inv[pos] = existing != null && existing.ItemId == itemId && basic?.Countable == true
            ? existing with { Count = existing.Count + count }
            : new PerTradeIconItem(itemId, count, durability, basic, ext);
    }

    // ---- Enter / leave -----------------------------------------------------

    /// <summary>
    /// PerTradeCoreStart + CUIPerTradeDlg::EnterPerTradeState — both parties accepted. Reset the
    /// grids, pull the backpack into the inv-mirror and show the window.
    /// </summary>
    private void EnterNormal()
    {
        State = PerTradeState.Normal;
        OtherReady = false;
        _myGold = 0;
        _otherGold = 0;
        _pendingAdd = null;
        _drag.WaitFromServer = false;

        Array.Clear(_my);
        Array.Clear(_other);
        Array.Clear(_inv);

        Inventory inv = _inventory ?? _context.InGame.Inventory;
        for (int i = 0; i < MaxItemInventory; i++)
        {
            InventoryItem? item = inv.BackpackItem(i);
            if (item == null || item.ItemId == 0)
                continue;
            (ItemBasicRow? basic, ItemExtRow? ext) = _items.Find((uint)item.ItemId);
            _inv[i] = new PerTradeIconItem(item.ItemId, item.Count, item.Durability, basic, ext);
        }

        if (_messageBox?.IsOpen == true)
            _messageBox.Close();

        _btnTradeMy?.SetVisible(true);
        _btnTradeOther?.SetVisible(true);
        Populate();
        _root.SetVisible(true);
    }

    /// <summary>
    /// PerTradeCompleteCancel + FinalizePerTrade — restore my un-traded offer (items back to the
    /// inv-mirror, offered gold back to my wallet) unless the trade succeeded, then reset to NONE.
    /// </summary>
    private void Finalize(bool success)
    {
        if (!success && State >= PerTradeState.Normal)
        {
            // Return offered gold to the wallet, offered items to the inv-mirror.
            if (_local != null)
                _local.Gold += _myGold;

            for (int i = 0; i < MaxItemPerTrade; i++)
            {
                if (_my[i] is { } item)
                    ReturnToInv(item);
                _my[i] = null;
            }

            CommitInvToInventory();
        }

        State = PerTradeState.None;
        _otherId = -1;
        _myGold = 0;
        _otherGold = 0;
        OtherReady = false;
        _pendingAdd = null;
        _drag.WaitFromServer = false;
        Array.Clear(_my);
        Array.Clear(_other);
        Array.Clear(_inv);

        if (_messageBox?.IsOpen == true)
            _messageBox.Close();
        _root.SetVisible(false);
        TradeFinished?.Invoke(success);
    }

    /// <summary>Write the inv-mirror back over the backpack (the authoritative post-trade layout).</summary>
    private void CommitInvToInventory()
    {
        Inventory inv = _inventory ?? _context.InGame.Inventory;
        for (int i = 0; i < MaxItemInventory; i++)
        {
            int slot = Inventory.BackpackIndex(i);
            if (_inv[i] is { } item)
                inv.Set(slot, new InventoryItem(item.ItemId, item.Count, item.Durability));
            else
                inv.Clear(slot);
        }
    }

    private void ReturnToInv(PerTradeIconItem item)
    {
        // Prefer stacking a countable onto its origin/matching slot, else the first empty slot.
        if (item.Basic?.Countable == true)
        {
            for (int i = 0; i < MaxItemInventory; i++)
            {
                if (_inv[i] != null && _inv[i]!.ItemId == item.ItemId)
                {
                    _inv[i] = _inv[i]! with { Count = _inv[i]!.Count + item.Count };
                    return;
                }
            }
        }

        for (int i = 0; i < MaxItemInventory; i++)
        {
            if (_inv[i] == null)
            {
                _inv[i] = item;
                return;
            }
        }
    }

    // ---- Item add (drag inv-mirror → my slot) ------------------------------

    /// <summary>
    /// CUIPerTradeDlg::ReceiveIconDrop — offer the item at inv-mirror order <paramref name="invOrder"/>.
    /// A countable stack first opens the split popup; otherwise a single unit is offered. The ADD
    /// packet's position field carries the inventory source order (the server's item key).
    /// </summary>
    public void AddItem(int invOrder)
    {
        if (State != PerTradeState.Normal)
            return;
        if (invOrder < 0 || invOrder >= MaxItemInventory || _inv[invOrder] is not { } item)
            return;

        if (item.Basic?.Countable == true && _countableEdit != null)
        {
            _countableEdit.Open(item.Count, count => SendAddItem(invOrder, count));
            return;
        }

        SendAddItem(invOrder, 1);
    }

    /// <summary>
    /// SendToServerItemAddMsg — move the item (or a countable split) from the inv-mirror to a my
    /// slot optimistically, lock dragging, and send N3_SP_PER_TRADE_ADD. Returns the packet.
    /// </summary>
    public byte[]? SendAddItem(int invOrder, int count)
    {
        if (invOrder < 0 || invOrder >= MaxItemInventory || _inv[invOrder] is not { } item)
            return null;
        if (count <= 0 || count > item.Count)
            return null;

        bool countable = item.Basic?.Countable == true;

        // Locate the destination my slot: stack a countable onto a matching slot, else first empty.
        int myOrder = -1;
        if (countable)
        {
            for (int i = 0; i < MaxItemPerTrade; i++)
            {
                if (_my[i] != null && _my[i]!.ItemId == item.ItemId)
                {
                    myOrder = i;
                    break;
                }
            }
        }

        bool createdSlot = false;
        if (myOrder < 0)
        {
            for (int i = 0; i < MaxItemPerTrade; i++)
            {
                if (_my[i] == null)
                {
                    myOrder = i;
                    createdSlot = true;
                    break;
                }
            }
        }

        if (myOrder < 0)
            return null; // no free my slot

        // Optimistic move: decrement the inv-mirror stack, add to the my slot.
        int remaining = item.Count - count;
        if (countable && remaining > 0)
            _inv[invOrder] = item with { Count = remaining };
        else
            _inv[invOrder] = null;

        _my[myOrder] = _my[myOrder] is { } existing
            ? existing with { Count = existing.Count + count }
            : item with { Count = count };

        int fullItemId = item.ItemId; // basic.dwID + ext.dwID == the stored full id
        byte[] packet = ExchangeProtocol.BuildAdd((byte)invOrder, fullItemId, count);
        _context.Client.Send(packet);

        _drag.WaitFromServer = true;
        _pendingAdd = new PendingAdd(invOrder, myOrder, item, count, createdSlot);
        Populate();
        return packet;
    }

    private void RollbackAdd(PendingAdd add)
    {
        // Move the offered stack back from the my slot to the inv-mirror source.
        PerTradeIconItem? mine = _my[add.MyOrder];
        if (mine != null)
        {
            int left = mine.Count - add.Count;
            _my[add.MyOrder] = add.CreatedMySlot || left <= 0 ? null : mine with { Count = left };
        }

        _inv[add.InvOrder] = _inv[add.InvOrder] is { } existing
            ? existing with { Count = existing.Count + add.Count }
            : add.Item with { Count = add.Count };
    }

    // ---- Gold offer (btn_gold → popup → ADD 0xFF) --------------------------

    /// <summary>RequestItemCountEdit — btn_gold opens the amount popup (Normal→Editting).</summary>
    public void OpenGoldPopup()
    {
        if (State != PerTradeState.Normal || _countableEdit == null)
            return;

        State = PerTradeState.Editting;
        int max = _local?.Gold ?? 0;
        _countableEdit.Open(max, amount => AddGold(amount), "Gold");
    }

    /// <summary>
    /// ItemCountEditOK — offer <paramref name="amount"/> gold: debit the wallet, credit the trade
    /// window, send ADD(0xFF, dwGold, amount) and return to Normal. Returns the packet.
    /// </summary>
    public byte[]? AddGold(int amount)
    {
        // The popup returns even from Editting; treat non-positive / over-wallet as a no-op close.
        if (State == PerTradeState.Editting)
            State = PerTradeState.Normal;

        if (amount <= 0 || _local == null || amount > _local.Gold)
            return null;

        _local.Gold -= amount;
        _myGold += amount;
        UpdateGoldStrings();

        byte[] packet = ExchangeProtocol.BuildAdd(GoldPosition, (int)GoldSentinel, amount);
        _context.Client.Send(packet);
        _drag.WaitFromServer = true;
        return packet;
    }

    // ---- Decide / cancel ---------------------------------------------------

    /// <summary>PerTradeMyDecision — btn_trade_my: ready the trade, freeze my icons (Normal→MyTradeDecisionDone).</summary>
    public byte[]? Decide()
    {
        if (State != PerTradeState.Normal)
            return null;

        byte[] packet = ExchangeProtocol.BuildDecide();
        _context.Client.Send(packet);
        State = PerTradeState.MyTradeDecisionDone;

        // SecureJobStuffByMyDecision — freeze the inv-mirror icons (no more drags).
        foreach (UiIconControl? icon in _invIcons)
            icon?.SetIconRegion(default);
        _btnTradeMy?.SetVisible(false);
        return packet;
    }

    /// <summary>LeavePerTradeState(PER_TRADE_RESULT_MY_CANCEL) — btn_close: cancel (Normal/DecisionDone only).</summary>
    public byte[]? Cancel()
    {
        if (State is not (PerTradeState.Normal or PerTradeState.MyTradeDecisionDone))
            return null;

        byte[] packet = ExchangeProtocol.BuildCancel();
        _context.Client.Send(packet);
        Finalize(success: false);
        return packet;
    }

    // ---- Message routing ---------------------------------------------------

    private void OnMessage(UiControl sender, uint msg)
    {
        switch (msg)
        {
            case UiMsg.ButtonClick:
                OnButton(sender);
                break;

            case UiMsg.IconDownFirst:
                OnIconDownFirst(sender);
                break;

            case UiMsg.IconDown:
                _drag.SelectedIcon.Icon?.MoveToCursor(Cursor);
                break;

            case UiMsg.IconUp:
                OnIconUp();
                break;
        }
    }

    private void OnButton(UiControl sender)
    {
        switch (sender.Id.ToLowerInvariant())
        {
            case "btn_close":
                Cancel();
                break;

            case "btn_trade_my":
                Decide();
                break;

            case "btn_gold":
                OpenGoldPopup();
                break;
        }
    }

    private void OnIconDownFirst(UiControl sender)
    {
        // Only inv-mirror icons are draggable (my/other slots have zeroed move rects).
        if (State != PerTradeState.Normal || sender is not UiIconControl icon)
            return;

        int order = FindInvSlot(icon);
        if (order < 0)
            return;

        _drag.SelectedIcon.Location = new UiWndIconInfo
        {
            Wnd = UiWnd.PerTrade,
            District = UiWndDistrict.PerTradeInv,
            Order = order,
        };
        _drag.SelectedIcon.Item = _inv[order];
        _drag.SelectedIcon.Icon = icon;
        icon.MoveToCursor(Cursor);
    }

    private void OnIconUp()
    {
        SelectedIconInfo sel = _drag.SelectedIcon;
        if (!sel.IsActive || sel.Location.District != UiWndDistrict.PerTradeInv)
        {
            RestoreDraggedIcon(sel);
            sel.Clear();
            return;
        }

        int srcOrder = sel.Location.Order;

        // A drop over any my-offer area = add-item.
        if (ResolveMyDrop(out _))
            AddItem(srcOrder);
        else
            RestoreDraggedIcon(sel);

        sel.Clear();
    }

    private bool ResolveMyDrop(out int order)
    {
        for (int i = 0; i < MaxItemPerTrade; i++)
        {
            if (_myArea[i]?.IsIn(Cursor.X, Cursor.Y) == true)
            {
                order = i;
                return true;
            }
        }

        order = -1;
        return false;
    }

    private int FindInvSlot(UiIconControl icon)
    {
        for (int i = 0; i < MaxItemInventory; i++)
        {
            if (ReferenceEquals(_invIcons[i], icon))
                return i;
        }

        return -1;
    }

    private void RestoreDraggedIcon(SelectedIconInfo sel)
    {
        if (sel.Icon is { } icon && sel.Location.District == UiWndDistrict.PerTradeInv)
        {
            int order = sel.Location.Order;
            if (order >= 0 && order < MaxItemInventory && _invArea[order] is { } area)
                icon.SetIconRegion(area.Region);
        }
    }

    // ---- Population --------------------------------------------------------

    private void Populate()
    {
        PopulateGrid(_my, _myIcons, _myArea, _myCountStr);
        PopulateOther();
        PopulateGrid(_inv, _invIcons, _invArea, _invCountStr);
        UpdateGoldStrings();
    }

    private void PopulateOther() => PopulateGrid(_other, _otherIcons, _otherArea, _otherCountStr);

    private void PopulateGrid(
        PerTradeIconItem?[] model, UiIconControl?[] icons, UiAreaControl?[] areas, UiStringControl?[] counts)
    {
        for (int i = 0; i < model.Length; i++)
        {
            UiIconControl? icon = icons[i];
            if (icon != null)
            {
                if (areas[i] is { } area)
                    icon.SetIconRegion(area.Region);
                ApplyIcon(icon, model[i]);
            }

            if (counts[i] is { } str)
            {
                bool show = model[i] is { } it && it.Basic?.Countable == true;
                str.SetVisible(show);
                if (show)
                    str.Text = model[i]!.Count.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private void ApplyIcon(UiIconControl icon, PerTradeIconItem? payload)
    {
        if (payload == null)
        {
            icon.SetVisible(false);
            icon.Payload = null;
            icon.IconTexture = string.Empty;
            icon.ItemSkillId = 0;
            return;
        }

        icon.IconTexture = ItemResourceNamer.MakeResourceFileName(payload.Basic, payload.Ext).IconFileName;
        icon.ItemSkillId = payload.ItemId;
        icon.Payload = payload;
        icon.SetVisible(true);
    }

    private void UpdateGoldStrings()
    {
        if (_strMyGold != null)
            _strMyGold.Text = _myGold.ToString(CultureInfo.InvariantCulture);
        if (_strOtherGold != null)
            _strOtherGold.Text = _otherGold.ToString(CultureInfo.InvariantCulture);
        if (_strInvGold != null && _local != null)
            _strInvGold.Text = _local.Gold.ToString(CultureInfo.InvariantCulture);
    }

    public void Show() => _root.SetVisible(true);

    public void Hide() => _root.SetVisible(false);
}
