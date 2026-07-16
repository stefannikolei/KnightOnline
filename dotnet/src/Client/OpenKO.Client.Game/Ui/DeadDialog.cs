using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the death / revival dialog — port of <c>CUIDead</c>
/// (Client/WarFare/UIDead.cpp). Two clickable string rows: <c>Text_Town</c> returns to
/// town (WIZ_REGENE type 1) and <c>Text_Alive</c> revives at the death spot with a life
/// stone (WIZ_REGENE type 2). Both go through <see cref="InGameState.SendRevival"/>.
/// The executable shows the dialog on local death; nothing auto-shows here.
/// </summary>
public sealed class DeadDialog
{
    /// <summary>MsgSend_Revival type — return to town.</summary>
    public const byte RevivalReturnTown = 1;

    /// <summary>MsgSend_Revival type — revive at the death spot (life stone).</summary>
    public const byte RevivalLifeStone = 2;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiStringControl? _textAlive;
    private readonly UiStringControl? _textTown;

    /// <summary>Raised with the revival type sent (1 = town, 2 = life stone).</summary>
    public event Action<byte>? RevivalRequested;

    public UiControl Root => _root;

    public DeadDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        _textAlive = root.GetChildById<UiStringControl>("Text_Alive");
        _textTown = root.GetChildById<UiStringControl>("Text_Town");
        root.Message += OnMessage;
        _root.SetVisible(false);
    }

    public void Show() => _root.SetVisible(true);

    public void Hide() => _root.SetVisible(false);

    private void Revive(byte type)
    {
        _context.InGame.SendRevival(type);
        RevivalRequested?.Invoke(type);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.StringLClick)
            return;

        if (ReferenceEquals(sender, _textTown))
        {
            Revive(RevivalReturnTown);
        }
        else if (ReferenceEquals(sender, _textAlive))
        {
            // TODO: life-stone count check (CUIDead gates this on level/inventory and
            // pops a confirm box before sending). Skipped for this slice.
            Revive(RevivalLifeStone);
        }
    }
}
