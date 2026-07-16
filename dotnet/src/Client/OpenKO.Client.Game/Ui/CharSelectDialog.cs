using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the character-select dialog — port of <c>CUICharacterSelect</c> +
/// the selection logic of <c>CGameProcCharacterSelect</c>: bt_left/bt_right rotate the
/// slot (DoJobLeft/DojobRight), text00 shows the slot info, Enter/GameStart runs
/// <c>CharacterSelectOrCreate</c> — an empty slot branches to character creation,
/// an occupied one sends WIZ_SEL_CHAR via <see cref="CharSelectState.SelectCharacter"/>.
/// </summary>
public sealed class CharSelectDialog
{
    public const int SlotCount = 3;

    private readonly GameContext _context;
    private readonly UiControl? _btnLeft;
    private readonly UiControl? _btnRight;
    private readonly UiControl? _btnExit;
    private readonly UiControl? _btnDelete;
    private readonly UiControl? _btnBack;
    private readonly UiStringControl? _info;

    /// <summary>Raised on bt_exit (exit-confirm in the original).</summary>
    public event Action? QuitRequested;

    /// <summary>Raised on bt_back (disconnect + back to login).</summary>
    public event Action? BackRequested;

    /// <summary>Raised when an empty slot is started (→ char create).</summary>
    public event Action<int>? CreateRequested;

    /// <summary>Raised when the selected slot index changes (viewer rotates the model).</summary>
    public event Action<int>? SelectionChanged;

    public UiControl Root { get; }

    public int SelectedIndex { get; private set; }

    public CharSelectDialog(GameContext context, UiControl root)
    {
        _context = context;
        Root = root;
        _btnLeft = root.GetChildById("bt_left");
        _btnRight = root.GetChildById("bt_right");
        _btnExit = root.GetChildById("bt_exit");
        _btnDelete = root.GetChildById("bt_delete");
        _btnBack = root.GetChildById("bt_back");
        _info = root.GetChildById<UiStringControl>("text00");
        root.Message += OnMessage;
        UpdateInfoText();
    }

    /// <summary>WIZ_ALLCHAR_INFO arrived.</summary>
    public void OnCharacters(IReadOnlyList<CharacterSlot> slots) => UpdateInfoText();

    /// <summary>DoJobLeft / DojobRight.</summary>
    public void Rotate(int direction)
    {
        SelectedIndex = ((SelectedIndex + direction) % SlotCount + SlotCount) % SlotCount;
        _context.SelectedCharIndex = SelectedIndex;
        SelectionChanged?.Invoke(SelectedIndex);
        UpdateInfoText();
    }

    /// <summary>CGameProcCharacterSelect::CharacterSelectOrCreate (Enter / game start).</summary>
    public void StartSelected()
    {
        IReadOnlyList<CharacterSlot> slots = _context.Characters;
        bool empty = SelectedIndex >= slots.Count || slots[SelectedIndex].IsEmpty;
        if (empty)
        {
            _context.CharCreate.SlotIndex = SelectedIndex;
            CreateRequested?.Invoke(SelectedIndex);
        }
        else
        {
            _context.CharSelect.SelectCharacter(SelectedIndex);
        }
    }

    public bool OnKeyReturn()
    {
        StartSelected();
        return true;
    }

    private void UpdateInfoText()
    {
        if (_info == null)
            return;

        IReadOnlyList<CharacterSlot> slots = _context.Characters;
        if (SelectedIndex < slots.Count && !slots[SelectedIndex].IsEmpty)
        {
            CharacterSlot slot = slots[SelectedIndex];
            _info.Text = $"{slot.CharId}  Lv.{slot.Level}";
        }
        else
        {
            _info.Text = string.Empty;
        }
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        if (ReferenceEquals(sender, _btnLeft))
            Rotate(-1);
        else if (ReferenceEquals(sender, _btnRight))
            Rotate(+1);
        else if (ReferenceEquals(sender, _btnExit))
            QuitRequested?.Invoke();
        else if (ReferenceEquals(sender, _btnBack))
            BackRequested?.Invoke();
        // bt_delete: character deletion is disabled upstream (shows an info box only).
    }
}
