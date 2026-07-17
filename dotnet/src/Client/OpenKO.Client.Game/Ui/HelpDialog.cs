using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the paged help window — port of <c>CUIHelp</c> (Client/WarFare/UIHelp.cpp). The
/// shipped <c>*_help_*.uif</c> carries <c>Page0</c>..<c>Page2</c> (<see cref="MaxHelpPage"/> panels,
/// only one visible at a time), <c>Btn_Close</c>, <c>Btn_Left</c> (previous) and <c>Btn_Right</c>
/// (next). <c>Btn_Left</c> steps back (clamped at 0), <c>Btn_Right</c> steps forward and wraps to 0
/// past the last page (matching the C++), and <c>Btn_Close</c> hides. Pure/headless.
/// </summary>
public sealed class HelpDialog
{
    /// <summary>MAX_HELP_PAGE (UIHelp.h) — the number of help panels.</summary>
    public const int MaxHelpPage = 3;

    private readonly UiControl _root;
    private readonly UiControl?[] _pages = new UiControl?[MaxHelpPage];
    private readonly UiButton? _btnClose;
    private readonly UiButton? _btnPrev;
    private readonly UiButton? _btnNext;

    public HelpDialog(GameContext context, UiControl root)
    {
        _ = context; // no reply is sent from this window (kept for pattern symmetry)
        _root = root;
        for (int i = 0; i < MaxHelpPage; i++)
        {
            _pages[i] = root.GetChildById($"Page{i}");
            _pages[i]?.SetVisible(i == 0); // first page shown, rest hidden (CUIHelp::Load)
        }

        _btnClose = root.GetChildById<UiButton>("Btn_Close");
        _btnPrev = root.GetChildById<UiButton>("Btn_Left");
        _btnNext = root.GetChildById<UiButton>("Btn_Right");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The zero-based index of the currently visible page (0 when none resolved).</summary>
    public int CurrentPage
    {
        get
        {
            for (int i = 0; i < MaxHelpPage; i++)
                if (_pages[i] is { Visible: true })
                    return i;
            return 0;
        }
    }

    /// <summary>Show/hide the help window.</summary>
    public void Toggle() => _root.SetVisible(!_root.Visible);

    private void ShowPage(int page)
    {
        for (int i = 0; i < MaxHelpPage; i++)
            _pages[i]?.SetVisible(i == page);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) == 0)
            return;

        int page = CurrentPage;
        if (ReferenceEquals(sender, _btnPrev))
        {
            page = Math.Max(0, page - 1);
            ShowPage(page);
        }
        else if (ReferenceEquals(sender, _btnNext))
        {
            page = page + 1 >= MaxHelpPage ? 0 : page + 1;
            ShowPage(page);
        }
        else if (ReferenceEquals(sender, _btnClose))
        {
            _root.SetVisible(false);
        }
    }
}
