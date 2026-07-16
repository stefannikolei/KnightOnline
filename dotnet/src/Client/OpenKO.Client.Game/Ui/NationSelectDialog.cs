using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the nation-select dialog — port of <c>CUINationSelectDlg</c>
/// (Client/WarFare/UINationSelectDlg.cpp): btn_karus_selection / btn_elmo_selection
/// send WIZ_SEL_NATION; btn_back returns to the login procedure.
/// </summary>
public sealed class NationSelectDialog
{
    private readonly GameContext _context;
    private readonly UiControl? _btnKarus;
    private readonly UiControl? _btnElmorad;
    private readonly UiControl? _btnBack;

    /// <summary>Raised when the user wants back to the login screen.</summary>
    public event Action? BackRequested;

    public UiControl Root { get; }

    public NationSelectDialog(GameContext context, UiControl root)
    {
        _context = context;
        Root = root;
        _btnKarus = root.GetChildById("btn_karus_selection");
        _btnElmorad = root.GetChildById("btn_elmo_selection");
        _btnBack = root.GetChildById("btn_back");
        root.Message += OnMessage;
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        if (ReferenceEquals(sender, _btnKarus))
            _context.NationSelect.SelectNation(NationSelectState.Karus);
        else if (ReferenceEquals(sender, _btnElmorad))
            _context.NationSelect.SelectNation(NationSelectState.ElMorad);
        else if (ReferenceEquals(sender, _btnBack))
            BackRequested?.Invoke();
    }
}
