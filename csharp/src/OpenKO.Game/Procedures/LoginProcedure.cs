using OpenKO.Common;
using OpenKO.Game.Rendering;
using OpenKO.N3;
using OpenKO.Net;
using OpenKO.Numerics;

namespace OpenKO.Game.Procedures;

/// <summary>
/// Cross-platform port of the login screen state (C++ <c>CGameProcLogIn_1298</c>). It owns the login
/// dialog's UI tree, connects to the login server and drives the account-login handshake.
///
/// The original loads the dialog from a <c>.uif</c> asset (<c>login.uif</c>) selected at random
/// between the Karus and El Morad intros. Since those binary assets are not part of this repository,
/// <see cref="BuildLayout"/> constructs an equivalent dialog tree in code from the already-ported
/// N3 UI controls (the same <see cref="N3UIBase"/> types a real <c>.uif</c> would deserialise into),
/// so the screen-space render path and layout are exercised end-to-end. When real assets are present
/// the tree can instead be produced by <see cref="N3UIBase.Load"/> with no change to rendering.
/// </summary>
public sealed class LoginProcedure : GameProcedure
{
    // Logical design size of the dialog; it is centred within the actual screen at render time.
    private const int DialogWidth = 360;
    private const int DialogHeight = 220;

    /// <summary>Login-server endpoint to connect to on <see cref="Init"/>. If null, no connection is attempted.</summary>
    public (string Host, int Port)? LoginServer { get; set; }

    /// <summary>The dialog's UI tree (root = full-screen panel). Rebuilt on every <see cref="Init"/>.</summary>
    public N3UIBase Root { get; private set; } = new();

    public N3UIImage Panel { get; private set; } = new();
    public N3UIString Title { get; private set; } = new();
    public N3UIEdit AccountField { get; private set; } = new();
    public N3UIEdit PasswordField { get; private set; } = new();
    public N3UIButton LoginButton { get; private set; } = new();

    public override void Init()
    {
        BuildLayout(Context.UiRenderer?.ScreenWidth ?? 1024, Context.UiRenderer?.ScreenHeight ?? 768);

        if (LoginServer is { } ep && Context.MainSocket is { } socket)
        {
            // Connect and immediately request the game-server list, mirroring the original Init.
            bool connected = socket.ConnectAsync(ep.Host, ep.Port).GetAwaiter().GetResult();
            if (connected)
                socket.Send(LoginProtocol.BuildServerListRequest());
        }
    }

    /// <summary>(Re)build the centred login dialog tree for the given screen size.</summary>
    public void BuildLayout(int screenWidth, int screenHeight)
    {
        int dx = (screenWidth - DialogWidth) / 2;
        int dy = (screenHeight - DialogHeight) / 2;

        Root = new N3UIBase { Id = "login_dialog", Region = new Rect(0, 0, screenWidth, screenHeight) };

        Panel = new N3UIImage { Id = "panel", Region = new Rect(dx, dy, dx + DialogWidth, dy + DialogHeight) };
        Title = new N3UIString { Id = "title", Region = new Rect(dx + 20, dy + 16, dx + DialogWidth - 20, dy + 44) };
        AccountField = new N3UIEdit { Id = "edit_id", Region = new Rect(dx + 30, dy + 70, dx + DialogWidth - 30, dy + 96) };
        PasswordField = new N3UIEdit { Id = "edit_pw", Region = new Rect(dx + 30, dy + 110, dx + DialogWidth - 30, dy + 136) };
        LoginButton = new N3UIButton { Id = "btn_login", Region = new Rect(dx + (DialogWidth - 120) / 2, dy + DialogHeight - 56, dx + (DialogWidth + 120) / 2, dy + DialogHeight - 24) };

        // push_front semantics (AddChild inserts at 0), matching the original control ordering.
        Root.AddChild(LoginButton);
        Root.AddChild(PasswordField);
        Root.AddChild(AccountField);
        Root.AddChild(Title);
        Root.AddChild(Panel);
    }

    public override void Render()
    {
        IUiRenderer? r = Context.UiRenderer;
        if (r == null)
            return;

        r.Begin();
        // Dimmed full-screen backdrop, then the dialog tree.
        r.DrawQuad(Root.Region, new UiColor(10, 14, 28));
        RenderControl(r, Root);
        r.End();
    }

    /// <summary>
    /// Recursively draw a control subtree. With no texture asset pipeline yet, each control is drawn
    /// as a solid placeholder quad coloured by its type, giving a recognisable login layout; once a
    /// control carries a resolved texture this is where the textured <see cref="IUiRenderer.DrawImage"/>
    /// path would be taken instead.
    /// </summary>
    private static void RenderControl(IUiRenderer r, N3UIBase control)
    {
        if (control.Width > 0 && control.Height > 0)
        {
            UiColor color = control switch
            {
                N3UIButton => new UiColor(196, 152, 64),   // gold button
                N3UIEdit => new UiColor(28, 32, 44),       // dark input field
                N3UIString => new UiColor(70, 90, 130),    // title bar
                N3UIImage => new UiColor(46, 52, 70),       // dialog panel
                _ => UiColor.Transparent,
            };

            if (color.A > 0)
                r.DrawQuad(control.Region, color);
        }

        // Children are stored push_front; draw them after the parent so they layer on top.
        for (int i = control.Children.Count - 1; i >= 0; i--)
            RenderControl(r, control.Children[i]);
    }

    /// <summary>
    /// Build and send the account-login packet from the current credentials (port of
    /// <c>MsgSend_AccountLogIn</c>). Returns false if the credentials are missing/too long or there is
    /// no connected socket.
    /// </summary>
    public bool TrySendAccountLogin()
    {
        if (Context.MainSocket is not { IsConnected: true } socket)
            return false;

        Packet pkt;
        try
        {
            pkt = LoginProtocol.BuildAccountLogin(Context.Account, Context.Password);
        }
        catch (ArgumentException)
        {
            return false;
        }

        socket.Send(pkt);
        return true;
    }
}
