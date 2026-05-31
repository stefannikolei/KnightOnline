using OpenKO.Common;
using OpenKO.Game;
using OpenKO.Game.Procedures;
using OpenKO.Game.Rendering;
using OpenKO.Net;
using OpenKO.N3;
using OpenKO.Numerics;
using Xunit;

namespace OpenKO.Tests;

public class LoginProcedureTests
{
    /// <summary>An <see cref="IUiRenderer"/> that records draw calls instead of touching a GPU.</summary>
    private sealed class FakeUiRenderer : IUiRenderer
    {
        public int ScreenWidth { get; }
        public int ScreenHeight { get; }
        public int BeginCount { get; private set; }
        public int EndCount { get; private set; }
        public List<Rect> Quads { get; } = new();

        public FakeUiRenderer(int w, int h) { ScreenWidth = w; ScreenHeight = h; }

        public void Begin() => BeginCount++;
        public void End() => EndCount++;
        public void DrawQuad(Rect region, UiColor color) => Quads.Add(region);
        public void DrawImage(Rect region, N3Texture texture, FloatRect uv, UiColor tint) => Quads.Add(region);
    }

    private static (GameContext ctx, LoginProcedure proc, FakeUiRenderer ui) NewLogin(int w = 800, int h = 600)
    {
        var ui = new FakeUiRenderer(w, h);
        var ctx = new GameContext { UiRenderer = ui };
        var proc = new LoginProcedure();
        ctx.Procedures.SetActive(proc);
        ctx.Procedures.TickActive(0.016f); // runs Init -> BuildLayout
        return (ctx, proc, ui);
    }

    [Fact]
    public void InitBuildsCenteredDialogTree()
    {
        var (_, proc, _) = NewLogin(800, 600);

        Assert.Equal("login_dialog", proc.Root.Id);
        Assert.Equal(800, proc.Root.Width);
        Assert.Equal(600, proc.Root.Height);

        // All five controls present in the tree.
        Assert.NotNull(proc.Root.FindById<N3UIImage>("panel"));
        Assert.NotNull(proc.Root.FindById<N3UIString>("title"));
        Assert.NotNull(proc.Root.FindById<N3UIEdit>("edit_id"));
        Assert.NotNull(proc.Root.FindById<N3UIEdit>("edit_pw"));
        Assert.NotNull(proc.Root.FindById<N3UIButton>("btn_login"));
    }

    [Fact]
    public void DialogPanelIsCenteredOnScreen()
    {
        var (_, proc, _) = NewLogin(800, 600);

        Rect panel = proc.Panel.Region;
        int panelCenterX = (panel.Left + panel.Right) / 2;
        int panelCenterY = (panel.Top + panel.Bottom) / 2;

        Assert.Equal(400, panelCenterX); // screen width / 2
        Assert.Equal(300, panelCenterY); // screen height / 2
    }

    [Fact]
    public void InputFieldsAndButtonLieWithinThePanel()
    {
        var (_, proc, _) = NewLogin();

        Rect panel = proc.Panel.Region;
        foreach (N3UIBase child in new N3UIBase[] { proc.AccountField, proc.PasswordField, proc.LoginButton })
        {
            Assert.True(child.Region.Left >= panel.Left);
            Assert.True(child.Region.Right <= panel.Right);
            Assert.True(child.Region.Top >= panel.Top);
            Assert.True(child.Region.Bottom <= panel.Bottom);
        }
    }

    [Fact]
    public void RenderEmitsBalancedBeginEndAndDrawsEveryControl()
    {
        var (ctx, _, ui) = NewLogin();

        ctx.Procedures.RenderActive();

        Assert.Equal(1, ui.BeginCount);
        Assert.Equal(1, ui.EndCount);

        // backdrop + panel + title + 2 edits + button = 6 quads
        Assert.Equal(6, ui.Quads.Count);
    }

    [Fact]
    public void TrySendAccountLoginFailsWithoutConnection()
    {
        var (ctx, proc, _) = NewLogin();
        ctx.Account = "hero";
        ctx.Password = "secret";

        // No socket attached -> cannot send.
        Assert.False(proc.TrySendAccountLogin());
    }

    [Fact]
    public void ServerListPacketUpdatesContextServers()
    {
        var (ctx, _, _) = NewLogin();
        var pkt = new Packet(LoginOpcode.ServerList);
        pkt.DByte();
        pkt.Append((byte)1);
        pkt.AppendString("127.0.0.1");
        pkt.AppendString("Ares");
        pkt.Append((short)321);

        Assert.True(ctx.Procedures.DispatchPacket(pkt));
        Assert.Single(ctx.Servers);
        Assert.Equal("Ares", ctx.Servers[0].Name);
    }

    [Fact]
    public void SuccessfulLoginTransitionsToServerSelect()
    {
        var (ctx, _, _) = NewLogin();
        var serverList = new Packet(LoginOpcode.ServerList);
        serverList.DByte();
        serverList.Append((byte)1);
        serverList.AppendString("127.0.0.1");
        serverList.AppendString("Dies");
        serverList.Append((short)100);
        ctx.Procedures.DispatchPacket(serverList);

        var loginResult = new Packet(LoginOpcode.LoginReq);
        loginResult.Append((byte)AuthResult.Ok);
        ctx.Procedures.DispatchPacket(loginResult);
        ctx.Procedures.TickActive(0.016f);

        Assert.IsType<ServerSelectProcedure>(ctx.Procedures.Active);
        Assert.Equal("Dies", ctx.ServerName);
    }
}
