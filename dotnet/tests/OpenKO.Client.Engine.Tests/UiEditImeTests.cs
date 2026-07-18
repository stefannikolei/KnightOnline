using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Stage-10.7 pins: the IME composition-overlay layout (N3UIEdit's IMM32 underlined
/// preview) — where the composition string is spliced in, the underline span, and the
/// intra-composition caret offset. All pure (no SDL / no device).
/// </summary>
public class UiEditImeTests
{
    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static UiEditControl Edit(uint style = 0) =>
        new(new N3UiEdit { Id = "E", Region = Rect(0, 0, 100, 20), Style = style });

    [Fact]
    public void NoComposition_LayoutIsPlainDisplay()
    {
        UiEditControl e = Edit();
        e.Text = "abc";

        UiEditControl.CompositionLayout layout = e.GetCompositionLayout(null, 0);

        Assert.False(layout.HasComposition);
        Assert.Equal("abc", layout.Text);
        Assert.Equal(3, layout.CaretIndex);
        Assert.Equal(0, layout.UnderlineLength);
    }

    [Fact]
    public void Composition_InsertedAtCaret_WithUnderlineAndCursor()
    {
        UiEditControl e = Edit();
        e.Text = "abcd";     // caret at end (4)
        e.MoveCaret(-2);     // caret now between "ab" and "cd"

        // A 2-char composition with the IME caret one glyph in.
        UiEditControl.CompositionLayout layout = e.GetCompositionLayout("가각", 1);

        Assert.True(layout.HasComposition);
        Assert.Equal("ab가각cd", layout.Text); // spliced at the caret
        Assert.Equal(2, layout.UnderlineStart);        // underline starts at the caret
        Assert.Equal(2, layout.UnderlineLength);       // spans the composition
        Assert.Equal(3, layout.CaretIndex);            // caretPos(2) + imeCursor(1)
    }

    [Fact]
    public void Composition_AtEnd_AppendsPreview()
    {
        UiEditControl e = Edit();
        e.Text = "hi"; // caret at 2 (end)

        UiEditControl.CompositionLayout layout = e.GetCompositionLayout("あ", 1);

        Assert.Equal("hiあ", layout.Text);
        Assert.Equal(2, layout.UnderlineStart);
        Assert.Equal(1, layout.UnderlineLength);
        Assert.Equal(3, layout.CaretIndex);
    }

    [Fact]
    public void Composition_CursorClampedToCompositionLength()
    {
        UiEditControl e = Edit();
        e.Text = "x"; // caret at 1

        // IME reports a cursor past the composition end → clamp.
        UiEditControl.CompositionLayout layout = e.GetCompositionLayout("ab", 99);

        Assert.Equal("xab", layout.Text);
        Assert.Equal(3, layout.CaretIndex); // 1 + clamp(99, 0, 2) = 3
    }

    [Fact]
    public void PasswordField_MasksCommittedButNotComposition()
    {
        UiEditControl e = Edit(UiStyle.EditPassword);
        e.Text = "pw"; // caret at 2, DisplayText = "**"

        UiEditControl.CompositionLayout layout = e.GetCompositionLayout("z", 1);

        // Committed text stays masked; the composition preview is shown as typed.
        Assert.Equal("**z", layout.Text);
        Assert.Equal(2, layout.UnderlineStart);
        Assert.Equal(1, layout.UnderlineLength);
    }
}
