using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the NPC quest MENU — port of <c>CUIQuestMenu</c> (Client/WarFare/UIQuestMenu.cpp).
/// The shipped <c>*_questmenu_*.uif</c> carries <c>Text_Title</c> (the main blurb), <c>Text_Npcname</c>
/// (the speaker), <c>btn_close</c> and a sample <c>Text_Menu</c> string that the original clones once
/// per menu row. This port reproduces that faithfully: <see cref="Open"/> resolves the row texts via
/// <see cref="TextResolver"/> and clones the sample into one clickable <see cref="UiStringControl"/> per
/// row; a row left-click (<see cref="UiMsg.StringLClick"/>) sends the picked index
/// (<c>MsgSend_SelectMenu</c> → <see cref="QuestProtocol.BuildSelectMenu"/>) and hides; <c>btn_close</c>
/// hides. The window is pushed open by the WIZ_SELECT_MSG (0x55) reply
/// (<see cref="InGameState.QuestMenuReceived"/>). Pure/headless.
///
/// Text is looked up from <c>__TABLE_QUEST_MENU</c>/<c>__TABLE_QUEST_TALK</c> in the original; here the
/// table lookup is injected through <see cref="TextResolver"/> so the controller stays asset-free and
/// headless-testable. When it is null, resolved text falls back to <see cref="string.Empty"/>.
/// </summary>
public sealed class QuestMenuDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiStringControl? _textTitle;
    private readonly UiStringControl? _strNpcName;
    private readonly UiStringControl? _textSample;
    private readonly UiButton? _btnClose;

    private readonly List<UiStringControl> _menuRows = [];
    private readonly List<string> _menuTexts = [];

    public QuestMenuDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        _textTitle = root.GetChildById<UiStringControl>("Text_Title");
        _strNpcName = root.GetChildById<UiStringControl>("Text_Npcname");
        _textSample = root.GetChildById<UiStringControl>("Text_Menu");
        _btnClose = root.GetChildById<UiButton>("btn_close");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The speaking NPC's id from the last <see cref="Open"/>.</summary>
    public short NpcId { get; private set; }

    /// <summary>The NPC name shown in <c>Text_Npcname</c> — the roster lookup is the host's concern.</summary>
    public string NpcName
    {
        get => _strNpcName?.Text ?? string.Empty;
        set { if (_strNpcName != null) _strNpcName.Text = value; }
    }

    /// <summary>The resolved menu row texts (order matches the sent index).</summary>
    public IReadOnlyList<string> MenuTexts => _menuTexts;

    /// <summary>The cloned clickable menu-row controls (one per <see cref="MenuTexts"/> entry).</summary>
    public IReadOnlyList<UiStringControl> MenuRows => _menuRows;

    /// <summary>
    /// Resolves a quest-menu/quest-talk text id to its string (the <c>.tbl</c> lookup in the original).
    /// Null yields <see cref="string.Empty"/>.
    /// </summary>
    public Func<uint, string>? TextResolver { get; set; }

    /// <summary>Wire the WIZ_SELECT_MSG reply that pushes the menu open.</summary>
    public void Bind(InGameState inGame) => inGame.QuestMenuReceived += Open;

    /// <summary>
    /// <c>CUIQuestMenu::Open</c> — store the npc id, resolve the title and one clickable row per menu id,
    /// then show. An empty menu (no rows survived the -1 filter) leaves the window hidden, as in the C++.
    /// </summary>
    public void Open(QuestMenuData data)
    {
        ClearRows();
        NpcId = data.NpcId;

        if (_textTitle != null)
            _textTitle.Text = Resolve(data.MainTalkId);

        foreach (uint menuId in data.MenuIds)
            _menuTexts.Add(Resolve(menuId));

        if (_menuTexts.Count == 0)
        {
            _root.SetVisible(false);
            return;
        }

        BuildRows();
        _root.SetVisible(true);
    }

    /// <summary>
    /// <c>CUIQuestMenu::MsgSend_SelectMenu</c> — send the picked index and hide. Returns the sent packet
    /// (or null for an out-of-range index).
    /// </summary>
    public byte[]? SelectMenu(int index)
    {
        if (index < 0 || index >= _menuTexts.Count)
            return null;

        byte[] packet = QuestProtocol.BuildSelectMenu((byte)index);
        _context.Client.Send(packet);
        _root.SetVisible(false);
        return packet;
    }

    private string Resolve(uint id) => TextResolver?.Invoke(id) ?? string.Empty;

    private void ClearRows()
    {
        foreach (UiStringControl row in _menuRows)
            _root.RemoveChild(row);
        _menuRows.Clear();
        _menuTexts.Clear();
    }

    private void BuildRows()
    {
        if (_textSample == null)
            return; // no sample row to clone (still selectable via SelectMenu)

        var sampleNode = (N3UiString)_textSample.Node;
        int rowHeight = _textSample.Height > 0 ? _textSample.Height : 20;

        for (int i = 0; i < _menuTexts.Count; i++)
        {
            var node = new N3UiString
            {
                Id = $"Text_Menu{i}",
                Region = sampleNode.Region,
                Movable = sampleNode.Movable,
                Style = sampleNode.Style,
                FontName = sampleNode.FontName,
                FontHeight = sampleNode.FontHeight,
                FontFlags = sampleNode.FontFlags,
                Color = sampleNode.Color,
            };
            var row = new UiStringControl(node) { Text = _menuTexts[i] };
            row.MoveOffset(0, i * rowHeight);
            _root.AddChild(row);
            _menuRows.Add(row);
        }
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if ((msg & UiMsg.StringLClick) != 0 && sender is UiStringControl row)
        {
            int index = _menuRows.IndexOf(row);
            if (index >= 0)
                SelectMenu(index);
        }
        else if ((msg & UiMsg.ButtonClick) != 0 && ReferenceEquals(sender, _btnClose))
        {
            _root.SetVisible(false);
        }
    }
}
