using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Input;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Ui;

namespace OpenKO.Client.Viewer;

/// <summary>
/// Stage-6.5 scene: renders the .uif corpus — quads through the
/// UiQuadBatcher, text through FontStashSharp. Left/Right switches layouts;
/// the widget under the mouse is highlighted via the hit test.
/// </summary>
public sealed class UiBrowserScene : IScene
{
    private readonly List<string> _uifFiles = [];
    private UiQuadBatcher? _batcher;
    private SpriteBatch? _spriteBatch;
    private FontService? _fonts;
    private TextureCache? _textures;
    private N3UiBase? _ui;
    private List<UiQuadPlan> _quads = [];
    private List<UiTextPlan> _texts = [];
    private int _index;
    private string _hover = string.Empty;

    public string Name => _ui == null
        ? "UI-Browser (keine Daten)"
        : $"UI-Browser [{_index + 1}/{_uifFiles.Count}] {Path.GetFileName(_uifFiles[_index])} " +
          $"({_quads.Count} Quads, {_texts.Count} Texte){(_hover.Length > 0 ? $" — {_hover}" : "")}";

    public void Load(ViewerContext context)
    {
        _batcher = new UiQuadBatcher(context.Device);
        _spriteBatch = new SpriteBatch(context.Device);
        _fonts = FontService.FromBaseDirectory(AppContext.BaseDirectory);

        if (context.DataPath != null)
        {
            var resolver = new KoPathResolver(context.DataPath);
            _textures = new TextureCache(context.Device, resolver);
            _uifFiles.AddRange(Directory
                .EnumerateFiles(context.DataPath, "*.uif", new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                })
                .Where(f => !Path.GetFileName(f).Equals("char_select.uif", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        LoadCurrent();
    }

    private void LoadCurrent()
    {
        _ui = null;
        _quads = [];
        _texts = [];
        if (_uifFiles.Count == 0)
            return;

        try
        {
            var ui = new N3UiBase();
            ui.LoadFromFile(_uifFiles[_index]);
            _ui = ui;
            (_quads, _texts) = UiRenderer.BuildPlans(ui);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{_uifFiles[_index]}: {ex.Message}");
        }
    }

    public void Tick(ViewerContext context)
    {
        if (_uifFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_RIGHT))
        {
            _index = (_index + 1) % _uifFiles.Count;
            LoadCurrent();
        }

        if (_uifFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_LEFT))
        {
            _index = (_index - 1 + _uifFiles.Count) % _uifFiles.Count;
            LoadCurrent();
        }

        if (_ui != null)
        {
            (int mx, int my) = context.Input.MousePos;
            N3UiBase? hit = UiRenderer.HitTest(_ui, mx, my);
            _hover = hit?.Id ?? string.Empty;
        }
    }

    public void Render(ViewerContext context)
    {
        GraphicsDevice device = context.Device;
        device.Clear(new Color(16, 16, 24));
        if (_ui == null || _batcher == null || _textures == null)
            return;

        _batcher.Begin();
        foreach (UiQuadPlan quad in _quads)
        {
            Texture2D? texture = _textures.Get(quad.TexFileName);
            _batcher.Draw(
                texture,
                quad.Screen.Left, quad.Screen.Top, quad.Screen.Right, quad.Screen.Bottom,
                quad.Uv.Left, quad.Uv.Top, quad.Uv.Right, quad.Uv.Bottom,
                ColorInterop.FromArgb(quad.ColorArgb));
        }

        _batcher.End();

        if (_fonts != null && _spriteBatch != null && _texts.Count > 0)
        {
            _spriteBatch.Begin();
            foreach (UiTextPlan text in _texts)
            {
                DynamicSpriteFont font = _fonts.GetUiFont(text.FontHeight == 0 ? 9 : text.FontHeight);
                _spriteBatch.DrawString(
                    font, text.Text,
                    new Vector2(text.Region.Left, text.Region.Top),
                    ColorInterop.FromArgb(text.ColorArgb));
            }

            _spriteBatch.End();
        }
    }

    public void Unload()
    {
        _textures?.Dispose();
        _textures = null;
        _batcher?.Dispose();
        _batcher = null;
        _spriteBatch?.Dispose();
        _spriteBatch = null;
        _fonts?.Dispose();
        _fonts = null;
        _uifFiles.Clear();
        _ui = null;
        _index = 0;
    }
}
