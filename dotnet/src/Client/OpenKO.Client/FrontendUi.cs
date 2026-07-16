using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;

namespace OpenKO.Client;

/// <summary>
/// Device-level glue for the interactive frontend (login → nation → char-select →
/// char-create): loads the per-nation .uif layouts via <c>UIs_us.tbl</c>, builds the
/// dialog controllers, swaps them as the <see cref="GameStateMachine"/> moves, draws
/// them through the <see cref="UiScreenRenderer"/>, and anchors the OS IME to the
/// focused edit box. Replaces the CLI auto-login when no --account is given.
/// </summary>
public sealed class FrontendUi : IDisposable
{
    private readonly GameContext _context;
    private readonly KoPathResolver _resolver;
    private readonly UiResourceTable _table;
    private readonly N3TableFile? _newChrValues;
    private readonly TextureCache _textures;
    private readonly UiScreenRenderer _screen;
    private readonly GraphicsDevice _device;
    private readonly int _introNation; // random ka/el background like GameProcLogIn_1298

    private LoginDialog? _login;
    private CharSelectDialog? _charSelect;
    private MessageBoxDialog? _messageBox;
    private GameState? _dialogState;
    private UiEditControl? _imeEdit;

    public UiManager Manager { get; } = new();

    public event Action? QuitRequested;

    public event Action<string>? Log;

    public FrontendUi(GameContext context, GraphicsDevice device, FontService fonts, string dataPath)
    {
        _context = context;
        _device = device;
        _resolver = new KoPathResolver(dataPath);
        _textures = new TextureCache(device, _resolver);
        _screen = new UiScreenRenderer(device, _textures, fonts);
        _introNation = Random.Shared.Next(1, 3);

        string tbl = _resolver.Resolve("Data\\UIs_us.tbl")
            ?? throw new FileNotFoundException("Data\\UIs_us.tbl not found under " + dataPath);
        _table = UiResourceTable.LoadFromFile(tbl);

        string? chrValues = _resolver.Resolve("Data\\NewChrValue.tbl");
        _newChrValues = chrValues != null ? N3TableFile.LoadFromFile(chrValues) : null;

        // Server events feed the dialogs.
        context.ServerListReceived = servers => _login?.SetServers(servers);
        context.AccountLoginResult = result =>
        {
            _login?.OnAccountLoginResult(result);
            if (!result.Success)
                ShowMessage($"Login failed (result {result.Result}).");
        };
        context.NewsReceived = news => _login?.ShowNews(news);
        context.CharactersReceived = chars => _charSelect?.OnCharacters(chars);
        context.CharCreate.CreateResult = result =>
        {
            if (result == 0)
                _context.Machine.SetActive(_context.CharSelect);
            else
                ShowMessage($"Character creation failed (code 0x{result:X2}).");
        };
    }

    /// <summary>Per-frame: swap dialogs on state changes and track IME focus.</summary>
    public void Tick()
    {
        GameState? active = _context.Machine.Active;
        if (!ReferenceEquals(active, _dialogState))
        {
            _dialogState = active;
            SwapDialogs(active);
        }

        // Anchor the OS IME candidate window to the focused edit box.
        UiEditControl? focused = Manager.FocusedEdit;
        if (!ReferenceEquals(focused, _imeEdit))
        {
            _imeEdit = focused;
            if (focused != null)
            {
                SdlIme.StartTextInput();
                SdlIme.SetTextInputRect(
                    focused.Region.Left, focused.Region.Top, focused.Width, focused.Height);
            }
            else
            {
                SdlIme.StopTextInput();
            }
        }
    }

    /// <summary>Enter on char select starts the highlighted slot (DIK_RETURN path).</summary>
    public bool OnReturnKey()
    {
        if (Manager.FocusedEdit != null)
            return false;
        if (ReferenceEquals(_context.Machine.Active, _context.CharSelect) && _charSelect != null)
        {
            _charSelect.StartSelected();
            return true;
        }

        return false;
    }

    public void Draw(double timeSeconds) => _screen.Draw(Manager, timeSeconds);

    private void SwapDialogs(GameState? state)
    {
        foreach (UiControl dialog in Manager.Dialogs.ToArray())
            Manager.Remove(dialog);
        _login = null;
        _charSelect = null;

        if (ReferenceEquals(state, _context.Login))
        {
            if (LoadDialog(_table.LoginIntro(_introNation)) is { } root)
            {
                CenterToViewport(root);
                _login = new LoginDialog(_context, root);
                _login.QuitRequested += () => QuitRequested?.Invoke();
                Manager.Add(root);

                // The server list may have arrived before the dialog existed.
                if (_context.Servers.Count > 0)
                    _login.SetServers(_context.Servers);
            }
        }
        else if (ReferenceEquals(state, _context.NationSelect))
        {
            if (LoadDialog(_table.NationSelect(_introNation)) is { } root)
            {
                CenterToViewport(root);
                var dlg = new NationSelectDialog(_context, root);
                dlg.BackRequested += () => _context.Machine.SetActive(_context.Login);
                Manager.Add(root);
            }
        }
        else if (ReferenceEquals(state, _context.CharSelect))
        {
            int nation = _context.Nation is 1 or 2 ? _context.Nation : _introNation;
            if (LoadDialog(_table.CharacterSelect(nation)) is { } root)
            {
                CenterToViewport(root);
                _charSelect = new CharSelectDialog(_context, root);
                _charSelect.QuitRequested += () => QuitRequested?.Invoke();
                _charSelect.BackRequested += () => _context.Machine.SetActive(_context.Login);
                _charSelect.CreateRequested += _ => _context.Machine.SetActive(_context.CharCreate);
                Manager.Add(root);
                if (_context.Characters.Count > 0)
                    _charSelect.OnCharacters(_context.Characters);
            }
        }
        else if (ReferenceEquals(state, _context.CharCreate))
        {
            int nation = _context.Nation is 1 or 2 ? _context.Nation : _introNation;
            if (LoadDialog(_table.CharacterCreate(nation)) is { } root)
            {
                CenterToViewport(root);
                var dlg = new CharCreateDialog(_context, root, _newChrValues);
                dlg.BackRequested += () => _context.Machine.SetActive(_context.CharSelect);
                Manager.Add(root);
            }
        }

        // The shared message box floats above whatever is open.
        if (_messageBox == null && LoadDialog(_table.MessageBox(_introNation)) is { } msgRoot)
        {
            CenterToViewport(msgRoot);
            _messageBox = new MessageBoxDialog(msgRoot);
        }

        if (_messageBox != null)
            Manager.Add(_messageBox.Root);
    }

    private void ShowMessage(string text)
    {
        Log?.Invoke(text);
        if (_messageBox != null)
        {
            _messageBox.Show(text);
            Manager.SetFocusedUi(_messageBox.Root);
        }
    }

    private UiControl? LoadDialog(string uifName)
    {
        if (uifName.Length == 0)
            return null;

        string? path = _resolver.Resolve(uifName);
        if (path == null)
        {
            Log?.Invoke($"UI layout not found: {uifName}");
            return null;
        }

        try
        {
            var layout = new N3UiBase();
            layout.LoadFromFile(path);
            return UiControlFactory.Build(layout);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"UI layout failed: {uifName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>CN3UIBase::SetPosCenter against the current viewport.</summary>
    private void CenterToViewport(UiControl root)
        => root.SetPosCenter(_device.Viewport.Width, _device.Viewport.Height);

    public void Dispose()
    {
        _screen.Dispose();
        _textures.Dispose();
    }
}
