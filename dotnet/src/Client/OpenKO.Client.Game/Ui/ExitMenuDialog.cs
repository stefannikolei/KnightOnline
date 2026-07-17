using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the in-game exit menu — port of <c>CUIExitMenu</c> (Client/WarFare/UIExitMenu.cpp).
/// The shipped <c>*_exitmenu_*.uif</c> carries <c>btn_chr</c> (return to character selection),
/// <c>btn_option</c> (launch the option app), <c>btn_exit</c> (quit) and <c>btn_cancel</c>.
///
/// The original's actions are Windows-specific — <c>ReturnToCharacterSelection</c> disconnects and
/// reconnects the socket, <c>btn_option</c> runs <c>ShellExecute("Option.exe")</c> then
/// <c>PostQuitMessage</c>, and <c>btn_exit</c> calls <c>PostQuitMessage</c>. None of that ports, so the
/// buttons raise <see cref="CharSelectRequested"/> / <see cref="OptionRequested"/> /
/// <see cref="ExitRequested"/> and let the host decide; <c>btn_cancel</c> hides. Pure/headless.
/// </summary>
public sealed class ExitMenuDialog
{
    private readonly UiControl _root;
    private readonly UiButton? _btnChr;
    private readonly UiButton? _btnOption;
    private readonly UiButton? _btnExit;
    private readonly UiButton? _btnCancel;

    public ExitMenuDialog(GameContext context, UiControl root)
    {
        _ = context; // no reply is sent from this window (kept for pattern symmetry)
        _root = root;
        _btnChr = root.GetChildById<UiButton>("btn_chr");
        _btnOption = root.GetChildById<UiButton>("btn_option");
        _btnExit = root.GetChildById<UiButton>("btn_exit");
        _btnCancel = root.GetChildById<UiButton>("btn_cancel");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>Raised on <c>btn_chr</c> — return to character selection (host reconnects the socket).</summary>
    public event Action? CharSelectRequested;

    /// <summary>Raised on <c>btn_option</c> — open the options app (host decides; Windows-only in the C++).</summary>
    public event Action? OptionRequested;

    /// <summary>Raised on <c>btn_exit</c> — quit the client (host decides; <c>PostQuitMessage</c> in the C++).</summary>
    public event Action? ExitRequested;

    /// <summary>Show/hide the exit menu (the ESC toggle in-game).</summary>
    public void Toggle() => _root.SetVisible(!_root.Visible);

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.ButtonClick) == 0)
            return;

        if (ReferenceEquals(sender, _btnChr))
        {
            _root.SetVisible(false);
            CharSelectRequested?.Invoke();
        }
        else if (ReferenceEquals(sender, _btnOption))
        {
            _root.SetVisible(false);
            OptionRequested?.Invoke();
        }
        else if (ReferenceEquals(sender, _btnExit))
        {
            _root.SetVisible(false);
            ExitRequested?.Invoke();
        }
        else if (ReferenceEquals(sender, _btnCancel))
        {
            _root.SetVisible(false);
        }
    }
}
