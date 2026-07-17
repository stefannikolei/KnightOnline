using System.Globalization;
using OpenKO.Client.Engine.Ui;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// The reusable stack-split / quantity popup — port of <c>CCountableItemEditDlg</c>
/// (Client/WarFare/CountableItemEditDlg.cpp). The shipped <c>*_personaltradeedit_*.uif</c>
/// (root id <c>base_tradeedit</c>) carries the <c>edit_trade</c> number box, <c>btn_ok</c>/
/// <c>btn_cancel</c> and the <c>String_PersonTradeEdit_Msg</c> prompt. It is a pure input
/// collector: <see cref="Open"/> locks the UI to this dialog (<see cref="UiManager.ModalId"/>
/// = <c>base_tradeedit</c>, mirroring the C++ MouseProc "only base_tradeedit works" gate) and
/// remembers a callback; <see cref="Ok"/> clamps the entered quantity to the caller's max
/// (and <c>UIITEM_COUNT_MANY</c> = 9999), invokes the callback and closes; <see cref="Cancel"/>
/// just closes. The transaction / warehouse (slice 9.8) callers drive it and build their own
/// packets from the returned value.
/// </summary>
public sealed class CountableItemEditDialog
{
    /// <summary>The .uif root id and the modal-lock key.</summary>
    public const string RootId = "base_tradeedit";

    /// <summary>UIITEM_COUNT_MANY — the hard quantity ceiling.</summary>
    public const int MaxCount = 9999;

    private readonly UiManager _manager;
    private readonly UiControl _root;
    private readonly UiEditControl? _edit;
    private readonly UiButton? _btnOk;
    private readonly UiButton? _btnCancel;
    private readonly UiStringControl? _prompt;

    private Action<int>? _onOk;
    private int _max = MaxCount;

    public CountableItemEditDialog(UiManager manager, UiControl root)
    {
        _manager = manager;
        _root = root;
        _edit = root.GetChildById<UiEditControl>("edit_trade");
        _btnOk = root.GetChildById<UiButton>("btn_ok");
        _btnCancel = root.GetChildById<UiButton>("btn_cancel");
        _prompt = root.GetChildById<UiStringControl>("String_PersonTradeEdit_Msg");

        root.Message += OnMessage;
        if (_edit != null)
            _edit.Returned += _ => Ok();     // Enter → OK (CN3UIEdit EDIT_RETURN)
        _root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>True while the popup owns the modal lock (CCountableItemEditDlg::IsLocked).</summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// Open the popup for a quantity in <c>[0, min(max, 9999)]</c>, focusing the edit box and
    /// locking input to this dialog. <paramref name="onOk"/> receives the clamped quantity.
    /// <paramref name="prompt"/> overrides the message line (gold vs count) when provided.
    /// </summary>
    public void Open(int max, Action<int> onOk, string? prompt = null)
    {
        _max = max < 0 ? MaxCount : Math.Min(max, MaxCount);
        _onOk = onOk;
        IsLocked = true;
        _manager.ModalId = RootId;

        if (prompt != null && _prompt != null)
            _prompt.Text = prompt;

        _edit?.Clear();       // SetQuantity(-1) → empty box
        _root.SetVisible(true);
        _manager.SetFocusedUi(_root);
        _edit?.SetFocus();
    }

    /// <summary>The current entered quantity (atoi of edit_trade), unclamped.</summary>
    public int Quantity => int.TryParse(_edit?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>btn_ok / Enter — clamp, dispatch to the caller and close.</summary>
    public void Ok()
    {
        Action<int>? cb = _onOk;
        int value = Math.Clamp(Quantity, 0, _max);
        Close();
        cb?.Invoke(value);
    }

    /// <summary>btn_cancel / Esc — close without dispatching.</summary>
    public void Cancel() => Close();

    /// <summary>CCountableItemEditDlg::Close — drop the lock and hide.</summary>
    public void Close()
    {
        IsLocked = false;
        _onOk = null;
        if (_manager.ModalId == RootId)
            _manager.ModalId = null;
        _edit?.KillFocus();
        _root.SetVisible(false);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;
        if (ReferenceEquals(sender, _btnOk))
            Ok();
        else if (ReferenceEquals(sender, _btnCancel))
            Cancel();
    }
}
