using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the notice banner — port of <c>CUINotice</c> (Client/WarFare/UINotice.cpp). The
/// shipped <c>*_notice_*.uif</c> carries <c>Text_Notice</c> (the joined body), a <c>ScrollBar</c> and
/// <c>Btn_Quit</c>. <see cref="Open"/> stores the lines, joins them into <c>Text_Notice</c> like
/// <c>CUINotice::GenerateText</c> (one line each, newline-separated) and shows; <c>Btn_Quit</c> clears
/// the text and hides. Pushed open by the WIZ_NOTICE (0x2E) reply
/// (<see cref="InGameState.NoticeReceived"/>). Pure/headless.
/// </summary>
public sealed class NoticeDialog
{
    private readonly UiControl _root;
    private readonly UiStringControl? _textNotice;
    private readonly UiButton? _btnQuit;

    private IReadOnlyList<string> _lines = [];

    public NoticeDialog(GameContext context, UiControl root)
    {
        _ = context; // no reply is sent from this window (kept for pattern symmetry)
        _root = root;
        _textNotice = root.GetChildById<UiStringControl>("Text_Notice");
        _btnQuit = root.GetChildById<UiButton>("Btn_Quit");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The notice lines from the last <see cref="Open"/>.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Wire the WIZ_NOTICE reply that pushes the notice open.</summary>
    public void Bind(InGameState inGame) => inGame.NoticeReceived += Open;

    /// <summary>CUINotice::GenerateText — join the lines into <c>Text_Notice</c> and show.</summary>
    public void Open(IReadOnlyList<string> lines)
    {
        _lines = lines;
        if (_textNotice != null)
            _textNotice.Text = string.Join("\n", lines);
        _root.SetVisible(true);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) != 0 && ReferenceEquals(sender, _btnQuit))
        {
            if (_textNotice != null)
                _textNotice.Text = string.Empty;
            _root.SetVisible(false);
        }
    }
}
