using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;
using Vector2 = System.Numerics.Vector2;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the player state bar HUD — port of <c>CUIStateBar</c>
/// (Client/WarFare/UIStateBar.cpp). Resolves the HP/MP/EXP progress bars and their
/// text labels plus the position readout, and mirrors the original UpdateHP/UpdateMSP/
/// UpdateExp/UpdatePosition formatting.
///
/// The runtime has no progress-bar widget yet (N3UiProgress maps to a plain
/// <see cref="UiControl"/>), so <see cref="SetProgress"/> records the 0..100 percent
/// against the control rather than driving a fill; the percentages are exposed for the
/// renderer/tests.
///
/// The minimap (Group_MiniMap: Img_MiniMap + Btn_ZoomIn/Btn_ZoomOut) is a device-level
/// port of the <c>CUIStateBar</c> minimap: the whole-zone texture is UV-scrolled around
/// the player, roster dots + a rotated player arrow are drawn on top, and a duration-buff
/// icon strip decays/blinks. The pure geometry lives in <see cref="MinimapLayout"/>; this
/// class holds the runtime state, wires the zoom buttons and drives the batchers. It stays
/// hidden until <see cref="EnableMinimap"/> hands it a zone texture (the executable does so
/// once the zone is loaded).
/// </summary>
public sealed class StateBarDialog : IDisposable
{
    private readonly UiControl _root;
    private readonly UiControl? _progressHp;
    private readonly UiControl? _progressMsp;
    private readonly UiControl? _progressExpC;
    private readonly UiControl? _progressExpP;
    private readonly UiStringControl? _textHp;
    private readonly UiStringControl? _textMsp;
    private readonly UiStringControl? _textExpP;
    private readonly UiStringControl? _textPosition;
    private readonly UiControl? _groupMiniMap;
    private readonly UiControl? _imgMap;
    private readonly UiControl? _btnZoomIn;
    private readonly UiControl? _btnZoomOut;

    // N3UiProgress has no runtime fill API — record the last percent per bar so the
    // renderer/tests can read it. Stubbed pending a real progress widget.
    private readonly Dictionary<UiControl, int> _progress = new();

    // ---- Minimap runtime state (CUIStateBar minimap) -----------------------
    private GraphicsDevice? _device;
    private UiQuadBatcher? _quads;   // map + dots (textured/colored quads)
    private UiPrimitiveBatcher? _prims; // player arrow (colored triangles)
    private Texture2D? _mapTex;
    private float _mapSizeX;
    private float _mapSizeZ;
    private float _zoom = MinimapLayout.DefaultZoom;
    private float _playerX;
    private float _playerZ;
    private float _yaw;
    private IReadOnlyList<MinimapDot> _dots = [];
    private readonly List<BuffIcon> _buffs = [];

    /// <summary>Optional skill-icon texture resolver for the duration-buff strip (UI\skillicon_*.dxt).</summary>
    public Func<uint, Texture2D?>? BuffIconResolver { get; set; }

    /// <summary>One duration-buff icon in the top strip (__DurationMagicImg).</summary>
    private sealed class BuffIcon
    {
        public uint SkillId;
        public float Duration;
        public bool Visible = true;
    }

    public UiControl Root => _root;

    /// <summary>The current minimap zoom (m_fZoom, clamped 1..6).</summary>
    public float MinimapZoom => _zoom;

    /// <summary>True once <see cref="EnableMinimap"/> has been called and the group is visible.</summary>
    public bool MinimapEnabled => _mapTex != null && _groupMiniMap is { Visible: true };

    /// <summary>Active duration-buff icon count (test/inspection hook).</summary>
    public int BuffCount => _buffs.Count;

    /// <summary>Last HP fill percentage (0..100).</summary>
    public int HpPercent { get; private set; }

    /// <summary>Last MP fill percentage (0..100).</summary>
    public int MpPercent { get; private set; }

    /// <summary>Last "current level segment" EXP fill percentage (Progress_ExpC).</summary>
    public int ExpCPercent { get; private set; }

    /// <summary>Last overall EXP fill percentage (Progress_ExpP).</summary>
    public int ExpPercent { get; private set; }

    public StateBarDialog(GameContext context, UiControl root)
    {
        _root = root;
        _progressHp = root.GetChildById("Progress_HP");
        _progressMsp = root.GetChildById("Progress_MSP");
        _progressExpC = root.GetChildById("Progress_ExpC");
        _progressExpP = root.GetChildById("Progress_ExpP");
        _textHp = root.GetChildById<UiStringControl>("Text_HP");
        _textMsp = root.GetChildById<UiStringControl>("Text_MSP");
        _textExpP = root.GetChildById<UiStringControl>("Text_ExpP");
        _textPosition = root.GetChildById<UiStringControl>("Text_Position");

        // Minimap group: stay hidden until EnableMinimap supplies a zone texture.
        _groupMiniMap = root.GetChildById("Group_MiniMap");
        _imgMap = _groupMiniMap?.GetChildById("Img_MiniMap");
        _btnZoomIn = _groupMiniMap?.GetChildById("Btn_ZoomIn");
        _btnZoomOut = _groupMiniMap?.GetChildById("Btn_ZoomOut");
        _groupMiniMap?.SetVisible(false);

        // Zoom buttons post UIMSG_BUTTON_CLICK up to the dialog root (CUIStateBar::ReceiveMessage).
        root.Message += OnMessage;
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;
        if (ReferenceEquals(sender, _btnZoomIn))
            ZoomIn();
        else if (ReferenceEquals(sender, _btnZoomOut))
            ZoomOut();
    }

    /// <summary>
    /// Convenience wiring: assign the in-game hooks so the bars refresh from the world
    /// state. The glue task may instead call the public Update* / <see cref="Fill"/>
    /// methods directly (these hooks are single-assignment on <see cref="InGameState"/>).
    /// </summary>
    public void Bind(InGameState inGame)
    {
        inGame.MyInfoReceived = Fill;
        inGame.HpChanged = (max, hp) => UpdateHp(hp, max);
    }

    /// <summary>WIZ_MYINFO — populate every bar/label from the local player block.</summary>
    public void Fill(LocalPlayer player)
    {
        UpdateHp(player.Hp, player.MaxHp);
        UpdateMp(player.Mp, player.MaxMp);
        UpdateExp(player.Exp, player.MaxExp);
        UpdatePosition(player.X, player.Z);
    }

    /// <summary>CUIStateBar::UpdateHP.</summary>
    public void UpdateHp(int hp, int max)
    {
        if (max <= 0)
            return;
        HpPercent = 100 * hp / max;
        SetProgress(_progressHp, HpPercent);
        if (_textHp != null)
            _textHp.Text = $"{hp} / {max}";
    }

    /// <summary>CUIStateBar::UpdateMSP (the label id is Text_MSP).</summary>
    public void UpdateMp(int mp, int max)
    {
        if (max <= 0)
            return;
        MpPercent = 100 * mp / max;
        SetProgress(_progressMsp, MpPercent);
        if (_textMsp != null)
            _textMsp.Text = $"{mp} / {max}";
    }

    /// <summary>CUIStateBar::UpdateExp — the overall (ExpP) and current-segment (ExpC) bars.</summary>
    public void UpdateExp(uint exp, uint max)
    {
        if (max == 0)
            return;

        ExpPercent = (int)(100.0 * exp / max);
        SetProgress(_progressExpP, ExpPercent);

        if (max > 10)
        {
            uint segment = max / 10;
            uint within = exp % segment;
            ExpCPercent = (int)(100 * within / segment);
        }
        else
        {
            ExpCPercent = 0;
        }

        SetProgress(_progressExpC, ExpCPercent);

        if (_textExpP != null)
        {
            double percent = 100.0 * exp / max;
            _textExpP.Text = string.Format(CultureInfo.InvariantCulture, "{0:F2} %", percent);
        }
    }

    /// <summary>CUIStateBar::UpdatePosition — "x, z" (one decimal, invariant).</summary>
    public void UpdatePosition(float x, float z)
    {
        if (_textPosition != null)
            _textPosition.Text = string.Format(CultureInfo.InvariantCulture, "{0:F1}, {1:F1}", x, z);
    }

    /// <summary>
    /// Record a bar's fill percentage. There is no runtime progress widget yet, so this
    /// stores the 0..100 value against the control instead of resizing a fill region.
    /// </summary>
    public void SetProgress(UiControl? bar, int percent)
    {
        if (bar == null)
            return;
        _progress[bar] = Math.Clamp(percent, 0, 100);
    }

    /// <summary>The recorded fill percentage for a bar (0 when never set).</summary>
    public int GetProgress(UiControl? bar) =>
        bar != null && _progress.TryGetValue(bar, out int v) ? v : 0;

    // ---- Minimap (CUIStateBar minimap) -------------------------------------

    /// <summary>
    /// CUIStateBar::LoadMap + ToggleMiniMap — hand the minimap the zone's whole-map texture and
    /// world size, create the device batchers and reveal the group. A null texture leaves it
    /// hidden (a zone with no minimap).
    /// </summary>
    public void EnableMinimap(GraphicsDevice device, Texture2D? mapTexture, float mapWorldSizeX, float mapWorldSizeZ)
    {
        _device = device;
        _mapTex = mapTexture;
        _mapSizeX = mapWorldSizeX;
        _mapSizeZ = mapWorldSizeZ;
        _quads ??= new UiQuadBatcher(device);
        _prims ??= new UiPrimitiveBatcher(device);
        _groupMiniMap?.SetVisible(mapTexture != null && _imgMap != null);
    }

    /// <summary>CUIStateBar::ToggleMiniMap — flip the group visibility (bound to the M key).</summary>
    public bool ToggleMinimap()
    {
        if (_groupMiniMap == null)
            return false;
        bool visible = !_groupMiniMap.Visible;
        _groupMiniMap.SetVisible(visible);
        return visible;
    }

    /// <summary>
    /// CUIStateBar::UpdatePosition + PositionInfo feed — the local player position/yaw and the
    /// current roster dots (party/NPC/enemy by colour). Called each frame by the executable.
    /// </summary>
    public void UpdateMinimap(float playerX, float playerZ, float yaw, IReadOnlyList<MinimapDot> dots)
    {
        _playerX = playerX;
        _playerZ = playerZ;
        _yaw = yaw;
        _dots = dots;
    }

    /// <summary>Btn_ZoomIn — ZoomSet(m_fZoom * 1.1), clamped 1..6.</summary>
    public void ZoomIn() => _zoom = MinimapLayout.ZoomIn(_zoom);

    /// <summary>Btn_ZoomOut — ZoomSet(m_fZoom * 0.9), clamped 1..6.</summary>
    public void ZoomOut() => _zoom = MinimapLayout.ZoomOut(_zoom);

    /// <summary>CUIStateBar::AddMagic — push a duration-buff icon onto the top strip.</summary>
    public void AddBuff(uint skillId, float duration) =>
        _buffs.Add(new BuffIcon { SkillId = skillId, Duration = duration });

    /// <summary>
    /// CUIStateBar::TickMagicIcon — decay each buff, blink in the final 10 s (toggle visibility
    /// every 0.5 s) and drop it at ≤ 0. Called each frame with the frame delta (s_fSecPerFrm).
    /// </summary>
    public void TickBuffs(float deltaSeconds)
    {
        for (int i = _buffs.Count - 1; i >= 0; i--)
        {
            BuffIcon b = _buffs[i];
            b.Duration -= deltaSeconds;
            if (b.Duration <= 0f)
            {
                _buffs.RemoveAt(i);
                continue;
            }

            if (b.Duration <= 10f)
                b.Visible = b.Duration - (int)b.Duration < 0.5f;
            else
                b.Visible = true;
        }
    }

    /// <summary>Whether the buff at <paramref name="index"/> is currently shown (test hook).</summary>
    public bool IsBuffVisible(int index) => index >= 0 && index < _buffs.Count && _buffs[index].Visible;

    /// <summary>
    /// Draw the UV-scrolled minimap: the whole-zone texture window, the roster dots (2 px black
    /// outline behind a filled marker), the rotated green player arrow, then the buff strip.
    /// A no-op until <see cref="EnableMinimap"/> and while the group is hidden. Called by the
    /// executable after the HUD dialogs are drawn (the C++ Render draws the map over the frame).
    /// </summary>
    public void DrawMinimap()
    {
        if (_device == null || _quads == null || _prims == null || _mapTex == null || _imgMap == null)
            return;
        if (_groupMiniMap is not { Visible: true })
            return;
        if (_mapSizeX <= 0f || _mapSizeZ <= 0f)
            return;

        N3UiRect rc = _imgMap.Region;
        int left = rc.Left, top = rc.Top, right = rc.Right, bottom = rc.Bottom;

        Vector2 view = MinimapLayout.ClampView(_playerX, _playerZ, _mapSizeX, _mapSizeZ, _zoom, left, top, right, bottom);
        MinimapUv uv = MinimapLayout.ComputeUv(view, _mapSizeX, _mapSizeZ, _zoom);

        _quads.Begin();

        // The whole-zone texture, UV-scrolled to the player window.
        _quads.Draw(_mapTex, left, top, right, bottom, uv.U0, uv.V0, uv.U1, uv.V1, Color.White);

        // Roster dots: a 2 px black outline quad behind, a filled colour quad in front.
        foreach (MinimapDot dot in _dots)
        {
            if (!MinimapLayout.TryDotScreen(view, _mapSizeX, _mapSizeZ, _zoom, left, top, right, bottom, dot.Position, out Vector2 p))
                continue;

            DrawColorQuad(p.X, p.Y, 2f, Color.Black);
            DrawColorQuad(p.X, p.Y, 1f, ColorInterop.FromArgb(dot.ColorArgb));
        }

        _quads.End();

        // The rotated green player arrow (two triangles).
        Vector2[] arrow = MinimapLayout.ArrowTriangles(
            view, _playerX, _playerZ, _yaw, _mapSizeX, _mapSizeZ, _zoom, left, top, right, bottom);
        _prims.Begin();
        _prims.FillTriangleList(arrow, ColorInterop.FromArgb(0xFF00FF00));
        _prims.End();

        DrawBuffStrip();
    }

    private void DrawColorQuad(float cx, float cy, float halfExtent, Color color) =>
        _quads!.Draw(null, cx - halfExtent, cy - halfExtent, cx + halfExtent, cy + halfExtent, 0f, 0f, 1f, 1f, color);

    /// <summary>
    /// The top duration-buff strip (CUIStateBar::AddMagic layout): right-aligned icons across the
    /// top edge. Only drawn when a <see cref="BuffIconResolver"/> is supplied; otherwise the buff
    /// model still decays/blinks but the icons are deferred (they need the skillicon textures).
    /// </summary>
    private void DrawBuffStrip()
    {
        if (BuffIconResolver == null || _buffs.Count == 0 || _device == null || _quads == null)
            return;

        const int iconSize = 32;
        int vpWidth = _device.Viewport.Width;

        _quads.Begin();
        for (int i = 0; i < _buffs.Count; i++)
        {
            BuffIcon b = _buffs[i];
            if (!b.Visible)
                continue;
            Texture2D? tex = BuffIconResolver(b.SkillId);
            if (tex == null)
                continue;
            int x = vpWidth - iconSize * (i + 1);
            _quads.Draw(tex, x, 0, x + iconSize, iconSize, 0f, 0f, 1f, 1f, Color.White);
        }

        _quads.End();
    }

    public void Dispose()
    {
        _quads?.Dispose();
        _prims?.Dispose();
    }
}
