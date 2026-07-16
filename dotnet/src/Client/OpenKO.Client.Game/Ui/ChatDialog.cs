using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>e_ChatMode (Client/WarFare/PacketDef.h) — the chat channel wire type.</summary>
public enum ChatChannel : byte
{
    Normal = 1,
    Private = 2,
    Party = 3,
    Force = 4,
    Shout = 5,
    Clan = 6,
}

/// <summary>One rendered scrollback line (text + ARGB color).</summary>
public readonly record struct ChatLine(string Text, uint Color);

/// <summary>
/// Controller for the chat window — port of <c>CUIChat</c> (Client/WarFare/UIChat.cpp).
/// Resolves the input edit (<c>edit0</c>, max 256 CP949 bytes), the five channel buttons,
/// the fold button (<c>btn_off</c>) and the scrollbar. In the original the scrollback text
/// (<c>text0</c>) is a plain <c>CN3UIString</c> the dialog paints line by line — there is no
/// list widget — so this controller keeps an in-memory <see cref="Lines"/> buffer for the
/// renderer to draw, matching that design.
///
/// On Enter the edit text is prefix-parsed (@id → private, # → party, $ → clan, ! → shout,
/// / → command/stub, else the active channel) and sent via
/// <see cref="InGameState.SendChat"/>.
/// </summary>
public sealed class ChatDialog
{
    /// <summary>MAX_CHAT_LINES analog — cap the scrollback buffer.</summary>
    public const int MaxLines = 500;

    private const uint White = 0xFFFFFFFF;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiEditControl? _edit;
    private readonly UiButton? _btnNormal;
    private readonly UiButton? _btnPrivate;
    private readonly UiButton? _btnPartyForce;
    private readonly UiButton? _btnKnights;
    private readonly UiButton? _btnShout;
    private readonly UiButton? _btnFold;
    private readonly UiScrollBarControl? _scroll;

    private readonly List<ChatLine> _lines = [];

    /// <summary>Raised when the fold button (btn_off) is pressed.</summary>
    public event Action? FoldRequested;

    public UiControl Root => _root;

    /// <summary>The current send channel (channel buttons change it).</summary>
    public ChatChannel Channel { get; private set; } = ChatChannel.Normal;

    /// <summary>The scrollback lines (the renderer paints these into text0).</summary>
    public IReadOnlyList<ChatLine> Lines => _lines;

    /// <summary>The whisper target parsed from the last "@id msg" line (glue selects it).</summary>
    public string? LastWhisperTarget { get; private set; }

    public ChatDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;

        _edit = root.GetChildById<UiEditControl>("edit0");
        if (_edit != null)
            _edit.MaxLength = 256; // CUIChat::Load — SetMaxString(256)

        _btnNormal = root.GetChildById<UiButton>("btn_normal");
        _btnPrivate = root.GetChildById<UiButton>("btn_private");
        _btnPartyForce = root.GetChildById<UiButton>("btn_party_force");
        _btnKnights = root.GetChildById<UiButton>("btn_knights");
        _btnShout = root.GetChildById<UiButton>("btn_shout");
        _btnFold = root.GetChildById<UiButton>("btn_off");
        _scroll = root.GetChildById<UiScrollBarControl>("scroll");

        root.Message += OnMessage;
    }

    /// <summary>
    /// Convenience wiring: route WIZ_CHAT into <see cref="AddChatMsg"/>. The glue task may
    /// instead call <see cref="AddChatMsg"/> directly (ChatReceived is single-assignment on
    /// <see cref="InGameState"/>).
    /// </summary>
    public void Bind(InGameState inGame)
    {
        inGame.ChatReceived = AddChatMsg;
    }

    /// <summary>CUIChat::ChangeChattingMode — set the active send channel.</summary>
    public void ChangeChannel(ChatChannel channel) => Channel = channel;

    /// <summary>CUIChat::AddChatMsg — append "Name: Text" (or just Text) to the scrollback.</summary>
    public void AddChatMsg(ChatMessage msg)
    {
        string line = string.IsNullOrEmpty(msg.Name) ? msg.Text : $"{msg.Name}: {msg.Text}";
        AddLine(line, White);
    }

    /// <summary>Append a raw colored line to the scrollback (trims to MaxLines).</summary>
    public void AddLine(string text, uint color = White)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _lines.Add(new ChatLine(text, color));
        while (_lines.Count > MaxLines)
            _lines.RemoveAt(0);
        _scroll?.SetRange(0, Math.Max(0, _lines.Count - 1));
    }

    /// <summary>
    /// CUIChat::ReceiveMessage(UIMSG_EDIT_RETURN) — prefix-parse the edit and send. Returns
    /// the channel actually sent, or null when nothing was sent (empty / bare command /
    /// malformed whisper). The edit is cleared either way.
    /// </summary>
    public ChatChannel? SubmitInput()
    {
        if (_edit == null)
            return null;

        string input = _edit.Text;
        _edit.Clear();

        if (input.Length == 0)
            return null;

        // "/cmd" — client command; parsing is deferred (stub).
        if (input.Length > 1 && input[0] == '/')
            return null;

        LastWhisperTarget = null;

        if (input.Length > 1 && input[0] == '@')
        {
            int space = input.IndexOf(' ');
            if (space <= 0)
                return null; // no target/message split — CUIChat ignores it
            string id = input[1..space];
            string msg = input[space..].TrimStart(' ');
            LastWhisperTarget = id;
            _context.InGame.SendChat((byte)ChatChannel.Private, msg);
            return ChatChannel.Private;
        }

        if (input.Length > 1 && input[0] == '#')
            return Send(ChatChannel.Party, input[1..]);
        if (input.Length > 1 && input[0] == '$')
            return Send(ChatChannel.Clan, input[1..]);
        if (input.Length > 1 && input[0] == '!')
            return Send(ChatChannel.Shout, input[1..]);

        return Send(Channel, input);
    }

    private ChatChannel Send(ChatChannel channel, string text)
    {
        _context.InGame.SendChat((byte)channel, text);
        return channel;
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg == UiMsg.EditReturn)
        {
            SubmitInput();
        }
        else if (msg == UiMsg.ButtonClick)
        {
            if (ReferenceEquals(sender, _btnNormal))
                ChangeChannel(ChatChannel.Normal);
            else if (ReferenceEquals(sender, _btnPrivate))
                ChangeChannel(ChatChannel.Private);
            else if (ReferenceEquals(sender, _btnPartyForce))
                ChangeChannel(ChatChannel.Party);
            else if (ReferenceEquals(sender, _btnKnights))
                ChangeChannel(ChatChannel.Clan);
            else if (ReferenceEquals(sender, _btnShout))
                ChangeChannel(ChatChannel.Shout);
            else if (ReferenceEquals(sender, _btnFold))
                FoldRequested?.Invoke();
        }
    }
}
