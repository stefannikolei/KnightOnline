using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the NPC/object teleport menu — port of <c>CUIWarp</c>
/// (Client/WarFare/UIWarp.cpp). The shipped <c>*_zonechangeorwarp_*.uif</c> carries
/// <c>List_Infos</c> (the destinations), <c>Text_Agreement</c> (the per-destination blurb),
/// <c>Btn_Ok</c> and <c>Btn_Cancel</c>. The list is filled from the WIZ_WARP_LIST reply
/// (routed via <see cref="InGameState.WarpListReceived"/>); selecting a row refreshes the
/// agreement text; <c>Btn_Ok</c> / a list double-click confirm the highlighted destination
/// (<c>MsgSend_Warp</c> → <see cref="WarpProtocol.BuildWarp"/>) and hide; <c>Btn_Cancel</c>
/// just hides. Pure/headless.
/// </summary>
public sealed class WarpDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiListControl? _list;
    private readonly UiStringControl? _agreement;

    private IReadOnlyList<WarpInfo> _warps = [];

    public WarpDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        _list = root.GetChildById<UiListControl>("List_Infos");
        _agreement = root.GetChildById<UiStringControl>("Text_Agreement");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The destinations currently listed.</summary>
    public IReadOnlyList<WarpInfo> Warps => _warps;

    /// <summary>Wire the WIZ_WARP_LIST reply.</summary>
    public void Bind(InGameState inGame) => inGame.WarpListReceived += OnWarpList;

    /// <summary>
    /// CGameProcMain::MsgRecv_WarpList — a real (kind 1) list with rows shows the window; an error
    /// kind or an empty "same zone" list leaves it hidden.
    /// </summary>
    public void OnWarpList(WarpListReply reply)
    {
        if (reply.Kind != WarpProtocol.KindList || reply.Warps.Count == 0)
            return;

        _warps = reply.Warps;
        UpdateList();
        _root.SetVisible(true);
    }

    /// <summary>CUIWarp::UpdateList — repopulate the list and select the first row.</summary>
    public void UpdateList()
    {
        if (_list == null)
            return;

        _list.ResetContent();
        foreach (WarpInfo w in _warps)
            _list.AddString(w.Name);

        _list.SetCurSel(0);
        UpdateAgreement();
    }

    /// <summary>CUIWarp::MsgSend_Warp — confirm the selected destination and hide.</summary>
    public byte[]? Confirm()
    {
        int sel = _list?.CurSel ?? -1;
        if (sel < 0 || sel >= _warps.Count || _warps[sel].Name.Length == 0)
        {
            _root.SetVisible(false);
            return null;
        }

        byte[] packet = WarpProtocol.BuildWarp(_warps[sel].Id);
        _context.Client.Send(packet);
        _root.SetVisible(false);
        return packet;
    }

    public void Cancel() => _root.SetVisible(false);

    private void UpdateAgreement()
    {
        if (_list == null || _agreement == null)
            return;
        int sel = _list.CurSel;
        _agreement.Text = sel >= 0 && sel < _warps.Count ? _warps[sel].Agreement : string.Empty;
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) != 0)
        {
            if (sender.Id.Equals("Btn_Ok", StringComparison.OrdinalIgnoreCase))
                Confirm();
            else if (sender.Id.Equals("Btn_Cancel", StringComparison.OrdinalIgnoreCase))
                Cancel();
        }
        else if ((msg & UiMsg.ListSelChange) != 0)
        {
            UpdateAgreement();
        }
        else if ((msg & UiMsg.ListDblClk) != 0)
        {
            Confirm();
        }
    }
}
