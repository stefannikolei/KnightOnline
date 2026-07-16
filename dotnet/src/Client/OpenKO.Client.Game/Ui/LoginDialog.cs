using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the login intro dialog — port of <c>CUILogIn_1298</c>
/// (Client/WarFare/UILogin_1298.cpp). Wraps the runtime control tree built from
/// <c>*_login_intro_*.uif</c>: the account panel (Group_LogIn: Edit_ID/Edit_PW/
/// btn_ok/btn_cancel), the notice boxes (Group_Notice_1..3 filled from LS_NEWS),
/// and the server list (Group_ServerList_01: server_1..20 rows + Btn_Connect).
/// Wires clicks to <see cref="LoginState.SubmitAccountLogin"/> /
/// <see cref="LoginState.ConnectToGameServer"/>.
/// </summary>
public sealed class LoginDialog
{
    public const int MaxServers = 20; // CUILogIn_1298::MAX_SERVERS

    private const uint White = 0xFFFFFFFF;
    private const uint Green = 0xFF00FF00;

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiControl? _groupLogin;
    private readonly UiControl?[] _groupNotice = new UiControl?[3];
    private readonly UiControl? _groupServerList;
    private readonly UiEditControl? _editId;
    private readonly UiEditControl? _editPw;
    private readonly UiButton? _btnLogin;
    private readonly UiButton? _btnCancel;
    private readonly UiButton? _btnConnect;
    private readonly UiButton?[] _btnNoticeOk = new UiButton?[3];
    private readonly UiControl?[] _serverGroups = new UiControl?[MaxServers];
    private readonly UiStringControl?[] _serverRows = new UiStringControl?[MaxServers];
    private readonly UiControl?[] _serverArrows = new UiControl?[MaxServers];

    private IReadOnlyList<ServerListEntry> _servers = [];
    private bool _loggedIn;

    /// <summary>Raised when the user asked to quit (btn_cancel).</summary>
    public event Action? QuitRequested;

    public UiControl Root => _root;

    public int SelectedServerIndex { get; private set; }

    public LoginDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;

        _groupLogin = root.GetChildById("Group_LogIn");
        _groupNotice[0] = root.GetChildById("Group_Notice_1");
        _groupNotice[1] = root.GetChildById("Group_Notice_2");
        _groupNotice[2] = root.GetChildById("Group_Notice_3");
        _groupServerList = root.GetChildById("Group_ServerList_01");

        if (_groupLogin != null)
        {
            _btnLogin = _groupLogin.GetChildById<UiButton>("btn_ok");
            _btnCancel = _groupLogin.GetChildById<UiButton>("btn_cancel");
            _editId = _groupLogin.GetChildById<UiEditControl>("Edit_ID");
            _editPw = _groupLogin.GetChildById<UiEditControl>("Edit_PW");
        }

        for (int i = 0; i < 3; i++)
            _btnNoticeOk[i] = _groupNotice[i]?.GetChildById<UiButton>("btn_ok");

        if (_groupServerList != null)
        {
            _btnConnect = _groupServerList.GetChildById<UiButton>("Btn_Connect");
            for (int i = 0; i < MaxServers; i++)
            {
                _serverGroups[i] = _groupServerList.GetChildById($"server_{i + 1}");
                _serverArrows[i] = _groupServerList.GetChildById($"img_arrow{i + 1}");
                _serverRows[i] = _serverGroups[i]?.GetChildById<UiStringControl>("List_Server");
            }
        }

        root.Message += OnMessage;
        OpenLoginPanel();
    }

    /// <summary>Initial visibility: account panel only (notices/serverlist come later).</summary>
    private void OpenLoginPanel()
    {
        _groupLogin?.SetVisible(true);
        foreach (UiControl? g in _groupNotice)
            g?.SetVisible(false);
        _groupServerList?.SetVisible(false);
    }

    // The Ebenezer news block markers (shared/packets.h) — byte strings with
    // embedded NULs that survive the ASCII decode as '\0' chars.
    private const string NewsMessageStart = "#\0\n";
    private const string NewsMessageEnd = "\0\n#\0\n\0\n";
    private const int MaxNewsCount = 3;

    /// <summary>CUILogIn_1298::AddNews block parsing: title, then #-fenced message.</summary>
    public static List<(string Title, string Message)> ParseNewsBlocks(string content)
    {
        var blocks = new List<(string, string)>();
        int searchPos = 0;
        while (blocks.Count < MaxNewsCount)
        {
            int startOfMessageBlock = content.IndexOf(NewsMessageStart, searchPos, StringComparison.Ordinal);
            if (startOfMessageBlock < 0)
                break;

            string title = content[searchPos..startOfMessageBlock];
            int startOfMessage = startOfMessageBlock + NewsMessageStart.Length;
            int endOfMessageBlock = content.IndexOf(NewsMessageEnd, startOfMessage, StringComparison.Ordinal);
            if (endOfMessageBlock < 0)
                break;

            blocks.Add((title, content[startOfMessage..endOfMessageBlock]));
            searchPos = endOfMessageBlock + NewsMessageEnd.Length;
        }

        return blocks;
    }

    /// <summary>LS_NEWS arrived: show the notice box sized for the entry count (or skip to the list).</summary>
    public void ShowNews(NewsResult news)
    {
        _groupLogin?.SetVisible(false);

        List<(string Title, string Message)> blocks = ParseNewsBlocks(news.Content);
        if (blocks.Count == 0)
        {
            OpenServerList();
            return;
        }

        int box = blocks.Count - 1; // Group_Notice_1/2/3 hold 1/2/3 entries
        for (int i = 0; i < 3; i++)
            _groupNotice[i]?.SetVisible(i == box);

        UiControl? group = _groupNotice[box];
        if (group == null)
        {
            OpenServerList();
            return;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            string suffix = $"{i + 1:00}";
            if (group.GetChildById<UiStringControl>($"text_notice_name_{suffix}") is { } name)
                name.Text = blocks[i].Title;
            if (group.GetChildById<UiStringControl>($"text_notice_{suffix}") is { } text)
                text.Text = blocks[i].Message;
        }
    }

    /// <summary>CUILogIn_1298::OpenServerList — close notices, show the list.</summary>
    public void OpenServerList()
    {
        foreach (UiControl? g in _groupNotice)
            g?.SetVisible(false);
        _groupLogin?.SetVisible(false);
        _groupServerList?.SetVisible(true);
        FillServerRows();
    }

    /// <summary>LS_SERVERLIST arrived (rows populate once the list opens).</summary>
    public void SetServers(IReadOnlyList<ServerListEntry> servers)
    {
        _servers = servers;
        if (_groupServerList?.Visible == true)
            FillServerRows();
    }

    private void FillServerRows()
    {
        for (int i = 0; i < MaxServers; i++)
        {
            bool has = i < _servers.Count;
            _serverGroups[i]?.SetVisible(has);
            _serverArrows[i]?.SetVisible(has);
            if (has && _serverRows[i] is { } row)
                row.Text = _servers[i].Name;
        }

        SelectServer(0);
    }

    /// <summary>CUILogIn_1298::SelectServer — green for selected, white otherwise.</summary>
    public void SelectServer(int index)
    {
        SelectedServerIndex = Math.Clamp(index, 0, MaxServers - 1);
        for (int i = 0; i < MaxServers; i++)
        {
            if (_serverRows[i] is { } row)
                row.ColorArgb = i == SelectedServerIndex ? Green : White;
        }
    }

    /// <summary>Account login accepted → wait for news; rejected → back to the panel.</summary>
    public void OnAccountLoginResult(AccountLoginResult result)
    {
        _loggedIn = result.Success;
        if (!result.Success)
            OpenLoginPanel();
    }

    private void SubmitLogin()
    {
        if (_editId == null || _editPw == null)
            return;
        _context.Login.SubmitAccountLogin(_editId.Text, _editPw.Text);
    }

    private void ConnectSelected()
    {
        if (SelectedServerIndex < _servers.Count)
            _context.Login.ConnectToGameServer(_servers[SelectedServerIndex]);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg == UiMsg.ButtonClick)
        {
            if (ReferenceEquals(sender, _btnLogin))
            {
                SubmitLogin();
            }
            else if (ReferenceEquals(sender, _btnConnect))
            {
                ConnectSelected();
            }
            else if (ReferenceEquals(sender, _btnCancel))
            {
                QuitRequested?.Invoke();
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    if (ReferenceEquals(sender, _btnNoticeOk[i]))
                    {
                        OpenServerList();
                        return;
                    }
                }
            }
        }
        else if (msg == UiMsg.StringLClick || msg == UiMsg.StringLDClick)
        {
            for (int i = 0; i < MaxServers; i++)
            {
                if (ReferenceEquals(sender, _serverRows[i]))
                {
                    SelectServer(i);
                    if (msg == UiMsg.StringLDClick)
                        ConnectSelected();
                    return;
                }
            }
        }
        else if (msg == UiMsg.EditReturn)
        {
            // Enter submits the login before account auth, connects afterwards.
            if (!_loggedIn)
                SubmitLogin();
            else
                ConnectSelected();
        }
    }
}
