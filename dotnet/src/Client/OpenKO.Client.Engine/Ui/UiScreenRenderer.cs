using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Rendering;

namespace OpenKO.Client.Engine.Ui;

/// <summary>
/// Device-side drawing of a <see cref="UiManager"/>: builds state-aware plans per
/// dialog (back-to-front), draws quads through the <see cref="UiQuadBatcher"/> +
/// <see cref="TextureCache"/> and text through FontStashSharp, then the blinking
/// caret of the focused edit and the hover tooltip on top — the CUIManager::Render
/// equivalent over the runtime control tree.
/// </summary>
public sealed class UiScreenRenderer(GraphicsDevice device, TextureCache textures, FontService fonts) : IDisposable
{
    private readonly UiQuadBatcher _batcher = new(device);
    private readonly SpriteBatch _spriteBatch = new(device);
    private Texture2D? _white;

    /// <summary>Caret blink period in seconds (on for the first half).</summary>
    public const double CaretBlinkPeriod = 1.0;

    public void Draw(UiManager manager, double timeSeconds)
    {
        var quads = new List<UiQuadPlan>();
        var texts = new List<UiTextPlan>();
        foreach (UiControl dialog in manager.DialogsInDrawOrder())
        {
            (List<UiQuadPlan> q, List<UiTextPlan> t) = UiRenderer.BuildPlans(dialog);
            quads.AddRange(q);
            texts.AddRange(t);
        }

        if (quads.Count > 0)
        {
            _batcher.Begin();
            foreach (UiQuadPlan quad in quads)
            {
                _batcher.Draw(
                    textures.Get(quad.TexFileName),
                    quad.Screen.Left, quad.Screen.Top, quad.Screen.Right, quad.Screen.Bottom,
                    quad.Uv.Left, quad.Uv.Top, quad.Uv.Right, quad.Uv.Bottom,
                    ColorInterop.FromArgb(quad.ColorArgb));
            }

            _batcher.End();
        }

        bool caretOn = manager.FocusedEdit is { } edit
            && timeSeconds % CaretBlinkPeriod < CaretBlinkPeriod / 2;

        // A live IME composition overlays the underlined preview at the caret; draw it
        // (and its underline) regardless of the caret blink.
        UiEditControl.CompositionLayout? composition = manager.FocusedEdit is { } focus
            ? focus.GetCompositionLayout(SdlImeComposition.CompositionText, SdlImeComposition.CompositionCursor)
            : null;
        bool hasComposition = composition is { HasComposition: true };

        if (texts.Count > 0 || caretOn || hasComposition)
        {
            _spriteBatch.Begin();
            foreach (UiTextPlan text in texts)
            {
                DynamicSpriteFont font = fonts.GetUiFont(text.FontHeight == 0 ? 9 : text.FontHeight);
                _spriteBatch.DrawString(
                    font, text.Text,
                    new Vector2(text.Region.Left, text.Region.Top),
                    ColorInterop.FromArgb(text.ColorArgb));
            }

            if (hasComposition)
                DrawComposition(manager.FocusedEdit!, composition!.Value, caretOn);
            else if (caretOn)
                DrawCaret(manager.FocusedEdit!);

            _spriteBatch.End();
        }
    }

    /// <summary>1px-wide caret after the text up to the caret position.</summary>
    private void DrawCaret(UiEditControl edit)
    {
        _white ??= CreateWhite();
        DynamicSpriteFont font = fonts.GetUiFont(12);
        string upToCaret = edit.DisplayText[..Math.Min(edit.CaretPos, edit.DisplayText.Length)];
        float x = edit.Region.Left + (upToCaret.Length > 0 ? font.MeasureString(upToCaret).X : 0f);
        int height = Math.Max(12, edit.Region.Bottom - edit.Region.Top - 4);
        _spriteBatch.Draw(
            _white,
            new Rectangle((int)x, edit.Region.Top + 2, 1, height),
            Color.White);
    }

    /// <summary>
    /// Draws the in-progress IME composition (N3UIEdit's IMM32 look): the composition
    /// string spliced in at the caret, a flat underline beneath it, and the
    /// intra-composition caret. Clause-segment (thick) underlines need SDL attribute
    /// data SDL2 does not surface, so the whole span is a single underline.
    /// </summary>
    private void DrawComposition(UiEditControl edit, UiEditControl.CompositionLayout layout, bool caretOn)
    {
        _white ??= CreateWhite();
        DynamicSpriteFont font = fonts.GetUiFont(12);

        float Measure(int end) => end > 0 ? font.MeasureString(layout.Text[..end]).X : 0f;

        int top = edit.Region.Top;
        // Overlay the composition glyphs (unmasked) starting at the caret base.
        float startX = edit.Region.Left + Measure(layout.UnderlineStart);
        string compo = layout.Text.Substring(layout.UnderlineStart, layout.UnderlineLength);
        _spriteBatch.DrawString(font, compo, new Vector2(startX, top), Color.White);

        // Flat underline spanning the composition.
        float endX = edit.Region.Left + Measure(layout.UnderlineStart + layout.UnderlineLength);
        int underlineY = Math.Max(top + 2, edit.Region.Bottom - 2);
        _spriteBatch.Draw(
            _white,
            new Rectangle((int)startX, underlineY, Math.Max(1, (int)(endX - startX)), 1),
            Color.White);

        // Intra-composition caret.
        if (caretOn)
        {
            float caretX = edit.Region.Left + Measure(layout.CaretIndex);
            int height = Math.Max(12, edit.Region.Bottom - top - 4);
            _spriteBatch.Draw(
                _white,
                new Rectangle((int)caretX, top + 2, 1, height),
                Color.White);
        }
    }

    private Texture2D CreateWhite()
    {
        var tex = new Texture2D(device, 1, 1);
        tex.SetData([Color.White]);
        return tex;
    }

    public void Dispose()
    {
        _batcher.Dispose();
        _spriteBatch.Dispose();
        _white?.Dispose();
    }
}
