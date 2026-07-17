using OpenKO.Client.Engine.Input;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>
/// Stage-9.11c pins: the pure Dubeolsik (두벌식) Hangul input automaton — the
/// documented fallback composer for when the Linux OS IME is unreliable.
/// </summary>
public class HangulAutomatonTests
{
    private static HangulAutomaton Type(string keys)
    {
        var a = new HangulAutomaton();
        foreach (char c in keys)
            a.ProcessKey(c);
        return a;
    }

    [Theory]
    [InlineData("gksrmf", "한글")]  // ㅎㅏㄴ ㄱㅡㄹ
    [InlineData("dkssud", "안녕")]  // ㅇㅏㄴ ㄴㅕㅇ
    [InlineData("dksssud", "안ㄴ녕")] // stray consonant flushes as a bare jamo
    public void TypingSequence_ComposesSyllables(string keys, string expected)
    {
        Assert.Equal(expected, Type(keys).Text);
    }

    [Fact]
    public void FinalConsonant_ReassignsToNextSyllable_WhenVowelFollows()
    {
        // ㄱㅏㄱ = 각, then ㅏ: the final ㄱ detaches to the next initial → 가 + 가.
        Assert.Equal("가가", Type("rkrk").Text);

        // ㄱㅏㅁ = 감, then ㅣ → 가 + 미.
        Assert.Equal("가미", Type("rkal").Text);
    }

    [Fact]
    public void DoubleFinal_Composes_AndSplits_WhenVowelFollows()
    {
        // ㄱ+ㅏ+ㄱ+ㅅ → 갃 (double final ㄳ).
        Assert.Equal("갃", Type("rkrt").Text);

        // A vowel after the double final splits it: ㄱ stays (각), ㅅ starts 사.
        Assert.Equal("각사", Type("rkrtk").Text);
    }

    [Fact]
    public void Backspace_DecomposesCurrentSyllable_StepByStep()
    {
        var a = Type("gks"); // 한 (ㅎ+ㅏ+ㄴ)
        Assert.Equal("한", a.Text);

        a.Backspace();
        Assert.Equal("하", a.Text); // final ㄴ removed

        a.Backspace();
        Assert.Equal("ㅎ", a.Text); // vowel ㅏ removed → bare initial jamo

        a.Backspace();
        Assert.Equal(string.Empty, a.Text); // initial removed → empty
    }

    [Fact]
    public void Backspace_ReducesDoubleFinalToBase_ThenDeletesCommitted()
    {
        var a = Type("rkrt"); // 갃 (double final ㄳ)
        Assert.Equal("갃", a.Text);

        a.Backspace();
        Assert.Equal("각", a.Text); // ㄳ → ㄱ

        a.Backspace();
        Assert.Equal("가", a.Text); // final removed

        // Peel the rest of the syllable, then confirm committed text also deletes.
        var b = Type("rkfk"); // 가 + ㄹ→가... actually ㄱㅏ 가, ㄹ, ㅏ → 가 + 라
        Assert.Equal("가라", b.Text);
        b.Backspace(); // 라 → 라 minus vowel → ㄹ... step within live syllable
        Assert.Equal("가ㄹ", b.Text);
        b.Backspace(); // ㄹ initial removed → composition empty
        Assert.Equal("가", b.Text);
        b.Backspace(); // now deletes the committed 가
        Assert.Equal(string.Empty, b.Text);
    }

    [Fact]
    public void NonHangulKey_FlushesComposition_AndIsNotConsumed()
    {
        var a = new HangulAutomaton();
        a.ProcessKey('r');
        a.ProcessKey('k'); // 가 in progress
        Assert.Equal("가", a.Text);
        Assert.Equal(string.Empty, a.CommittedText); // still live, not settled

        bool consumed = a.ProcessKey(' '); // non-jamo → flush
        Assert.False(consumed);
        Assert.Equal("가", a.CommittedText); // now committed
        Assert.Equal(string.Empty, a.Composition);
    }

    [Fact]
    public void CompoundVowel_ComposesFromTwoVowels()
    {
        // ㄱ+ㅗ+ㅏ → ㅘ → 과.
        Assert.Equal("과", Type("rhk").Text);

        // Backspace reduces the compound vowel to its base: 과 → 고.
        var a = Type("rhk");
        a.Backspace();
        Assert.Equal("고", a.Text);
    }
}
