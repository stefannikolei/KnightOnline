using System.Text;

namespace OpenKO.Client.Engine.Input;

/// <summary>
/// Pure Dubeolsik (두벌식) Hangul input automaton: the standard 2-set Korean
/// keyboard as a Jamo→syllable composer. It is the documented FALLBACK for when
/// the OS IME under Linux (IBus/Fcitx behind SDL) is unreliable — a standalone,
/// headless-testable state machine with no engine or device dependencies.
///
/// <para>QWERTY keys map to initial (choseong) / medial (jungseong) / final
/// (jongseong) jamo which compose into precomposed Hangul syllables from the
/// U+AC00 block:</para>
/// <code>syllable = 0xAC00 + (choseong*21 + jungseong)*28 + jongseong</code>
/// <para>It handles consonant+vowel→syllable, double finals (겹받침), the
/// final-consonant reassignment when a vowel follows (각+ㅏ → 가+가), and
/// step-by-step backspace decomposition. Non-jamo keys flush the current
/// composition. Nothing here touches SDL or MonoGame.</para>
/// </summary>
public sealed class HangulAutomaton
{
    private const int SBase = 0xAC00;   // U+AC00 '가', start of the syllable block
    private const int JungBase = 0x314F; // U+314F 'ㅏ', compatibility-jamo vowels (same order)

    /// <summary>Compatibility-jamo codepoint for a lone leading consonant, by choseong index.</summary>
    private static readonly char[] ChoCompat =
    [
        'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ',
        'ㅂ', 'ㅃ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ',
        'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ',
    ];

    private enum Kind { None, Consonant, Vowel }

    private readonly record struct Jamo(Kind Kind, int Cho, int Jong, int Jung);

    private readonly StringBuilder _committed = new();
    private int _cho = -1;   // choseong index 0..18, -1 = none
    private int _jung = -1;  // jungseong index 0..20, -1 = none
    private int _jong = -1;  // jongseong index 1..27, -1 = none (0 in the formula)

    /// <summary>The settled text (syllables the composer has finished with).</summary>
    public string CommittedText => _committed.ToString();

    /// <summary>The in-progress syllable being composed (for the underlined caret preview), or "".</summary>
    public string Composition
    {
        get
        {
            if (_cho >= 0 && _jung >= 0)
            {
                int code = SBase + (_cho * 21 + _jung) * 28 + (_jong < 0 ? 0 : _jong);
                return ((char)code).ToString();
            }

            if (_cho >= 0)
                return ChoCompat[_cho].ToString();
            if (_jung >= 0)
                return ((char)(JungBase + _jung)).ToString();
            return string.Empty;
        }
    }

    /// <summary>Full current text: settled output plus the live composition.</summary>
    public string Text => _committed.ToString() + Composition;

    /// <summary>Commit the in-progress syllable into the settled text and clear the composer.</summary>
    public void Flush()
    {
        _committed.Append(Composition);
        _cho = _jung = _jong = -1;
    }

    /// <summary>Drop everything (settled text and the live composition).</summary>
    public void Reset()
    {
        _committed.Clear();
        _cho = _jung = _jong = -1;
    }

    /// <summary>
    /// Feed one QWERTY character. Returns true if it was a mapped jamo and consumed;
    /// false if it was a non-Hangul key — in which case the current composition is
    /// flushed and the caller handles the literal character itself.
    /// </summary>
    public bool ProcessKey(char key)
    {
        Jamo j = MapKey(key);
        switch (j.Kind)
        {
            case Kind.Consonant:
                AddConsonant(j.Cho, j.Jong);
                return true;
            case Kind.Vowel:
                AddVowel(j.Jung);
                return true;
            default:
                Flush();
                return false;
        }
    }

    /// <summary>
    /// Backspace: peel one jamo off the live syllable (double final → base final →
    /// no final → base vowel → no vowel → no initial), and only once the composition
    /// is empty delete the last settled character.
    /// </summary>
    public void Backspace()
    {
        if (_jong >= 0)
        {
            _jong = SplitJong(_jong).First; // double → base single; single → -1 (removed)
            return;
        }

        if (_jung >= 0)
        {
            _jung = SplitJung(_jung); // compound → base; simple → -1 (removed)
            return;
        }

        if (_cho >= 0)
        {
            _cho = -1;
            return;
        }

        if (_committed.Length > 0)
            _committed.Remove(_committed.Length - 1, 1);
    }

    private void AddConsonant(int cho, int jong)
    {
        if (_cho < 0 && _jung < 0)
        {
            _cho = cho; // fresh initial
            return;
        }

        if (_jung < 0)
        {
            // A lone leading consonant is already pending: commit it, start anew.
            Flush();
            _cho = cho;
            return;
        }

        if (_cho < 0)
        {
            // A lone vowel is pending: commit it, start a new initial.
            Flush();
            _cho = cho;
            return;
        }

        if (_jong < 0)
        {
            if (jong >= 0)
                _jong = jong; // becomes the syllable's final
            else
            {
                Flush(); // ㄸ/ㅃ/ㅉ cannot be a final: start a new syllable
                _cho = cho;
            }

            return;
        }

        // A final is already present: try to combine into a double final (겹받침).
        if (jong >= 0)
        {
            int combined = JoinJong(_jong, jong);
            if (combined >= 0)
            {
                _jong = combined;
                return;
            }
        }

        Flush();
        _cho = cho;
    }

    private void AddVowel(int jung)
    {
        if (_jung < 0)
        {
            _jung = jung; // completes a cho, or a bare leading vowel when _cho < 0
            return;
        }

        if (_jong < 0)
        {
            // No final yet: try to grow a compound vowel (ㅗ+ㅏ → ㅘ), else split off.
            int combined = JoinJung(_jung, jung);
            if (combined >= 0)
                _jung = combined;
            else
            {
                Flush();
                _jung = jung; // a new bare vowel
            }

            return;
        }

        // A final is present and a vowel follows: the final reassigns to the next
        // syllable's initial. A double final splits — its first half stays behind.
        (int first, int detachCho) = SplitJong(_jong);
        if (first >= 0)
        {
            _jong = first;
        }
        else
        {
            detachCho = JongToCho(_jong);
            _jong = -1;
        }

        Flush();
        _cho = detachCho;
        _jung = jung;
    }

    /// <summary>Dubeolsik key → jamo. Unmapped keys return <see cref="Kind.None"/>.</summary>
    private static Jamo MapKey(char k) => k switch
    {
        // Consonants: (choseong index, jongseong index) — jong -1 when it cannot be a final.
        'r' => new(Kind.Consonant, 0, 1, -1),    // ㄱ
        'R' => new(Kind.Consonant, 1, 2, -1),    // ㄲ
        's' => new(Kind.Consonant, 2, 4, -1),    // ㄴ
        'e' => new(Kind.Consonant, 3, 7, -1),    // ㄷ
        'E' => new(Kind.Consonant, 4, -1, -1),   // ㄸ (never a final)
        'f' => new(Kind.Consonant, 5, 8, -1),    // ㄹ
        'a' => new(Kind.Consonant, 6, 16, -1),   // ㅁ
        'q' => new(Kind.Consonant, 7, 17, -1),   // ㅂ
        'Q' => new(Kind.Consonant, 8, -1, -1),   // ㅃ (never a final)
        't' => new(Kind.Consonant, 9, 19, -1),   // ㅅ
        'T' => new(Kind.Consonant, 10, 20, -1),  // ㅆ
        'd' => new(Kind.Consonant, 11, 21, -1),  // ㅇ
        'w' => new(Kind.Consonant, 12, 22, -1),  // ㅈ
        'W' => new(Kind.Consonant, 13, -1, -1),  // ㅉ (never a final)
        'c' => new(Kind.Consonant, 14, 23, -1),  // ㅊ
        'z' => new(Kind.Consonant, 15, 24, -1),  // ㅋ
        'x' => new(Kind.Consonant, 16, 25, -1),  // ㅌ
        'v' => new(Kind.Consonant, 17, 26, -1),  // ㅍ
        'g' => new(Kind.Consonant, 18, 27, -1),  // ㅎ

        // Vowels: (jungseong index). Compound vowels have no key; they are typed
        // as two vowels and combined by JoinJung.
        'k' => new(Kind.Vowel, -1, -1, 0),   // ㅏ
        'o' => new(Kind.Vowel, -1, -1, 1),   // ㅐ
        'i' => new(Kind.Vowel, -1, -1, 2),   // ㅑ
        'O' => new(Kind.Vowel, -1, -1, 3),   // ㅒ
        'j' => new(Kind.Vowel, -1, -1, 4),   // ㅓ
        'p' => new(Kind.Vowel, -1, -1, 5),   // ㅔ
        'u' => new(Kind.Vowel, -1, -1, 6),   // ㅕ
        'P' => new(Kind.Vowel, -1, -1, 7),   // ㅖ
        'h' => new(Kind.Vowel, -1, -1, 8),   // ㅗ
        'y' => new(Kind.Vowel, -1, -1, 12),  // ㅛ
        'n' => new(Kind.Vowel, -1, -1, 13),  // ㅜ
        'b' => new(Kind.Vowel, -1, -1, 17),  // ㅠ
        'm' => new(Kind.Vowel, -1, -1, 18),  // ㅡ
        'l' => new(Kind.Vowel, -1, -1, 20),  // ㅣ
        _ => default,
    };

    /// <summary>Combine an existing single final with an incoming final into a double final, or -1.</summary>
    private static int JoinJong(int a, int b) => (a, b) switch
    {
        (1, 19) => 3,    // ㄱ+ㅅ → ㄳ
        (4, 22) => 5,    // ㄴ+ㅈ → ㄵ
        (4, 27) => 6,    // ㄴ+ㅎ → ㄶ
        (8, 1) => 9,     // ㄹ+ㄱ → ㄺ
        (8, 16) => 10,   // ㄹ+ㅁ → ㄻ
        (8, 17) => 11,   // ㄹ+ㅂ → ㄼ
        (8, 19) => 12,   // ㄹ+ㅅ → ㄽ
        (8, 25) => 13,   // ㄹ+ㅌ → ㄾ
        (8, 26) => 14,   // ㄹ+ㅍ → ㄿ
        (8, 27) => 15,   // ㄹ+ㅎ → ㅀ
        (17, 19) => 18,  // ㅂ+ㅅ → ㅄ
        _ => -1,
    };

    /// <summary>
    /// Decompose a final for backspace / reassignment. For a double final returns
    /// (base single final that stays, choseong index of the detaching consonant);
    /// for a single final returns (-1, -1) — i.e. "no remainder".
    /// </summary>
    private static (int First, int DetachCho) SplitJong(int jong) => jong switch
    {
        3 => (1, 9),     // ㄳ → ㄱ + ㅅ
        5 => (4, 12),    // ㄵ → ㄴ + ㅈ
        6 => (4, 18),    // ㄶ → ㄴ + ㅎ
        9 => (8, 0),     // ㄺ → ㄹ + ㄱ
        10 => (8, 6),    // ㄻ → ㄹ + ㅁ
        11 => (8, 7),    // ㄼ → ㄹ + ㅂ
        12 => (8, 9),    // ㄽ → ㄹ + ㅅ
        13 => (8, 16),   // ㄾ → ㄹ + ㅌ
        14 => (8, 17),   // ㄿ → ㄹ + ㅍ
        15 => (8, 18),   // ㅀ → ㄹ + ㅎ
        18 => (17, 9),   // ㅄ → ㅂ + ㅅ
        _ => (-1, -1),
    };

    /// <summary>Combine two vowels into a compound jungseong (ㅗ+ㅏ → ㅘ), or -1.</summary>
    private static int JoinJung(int a, int b) => (a, b) switch
    {
        (8, 0) => 9,     // ㅗ+ㅏ → ㅘ
        (8, 1) => 10,    // ㅗ+ㅐ → ㅙ
        (8, 20) => 11,   // ㅗ+ㅣ → ㅚ
        (13, 4) => 14,   // ㅜ+ㅓ → ㅝ
        (13, 5) => 15,   // ㅜ+ㅔ → ㅞ
        (13, 20) => 16,  // ㅜ+ㅣ → ㅟ
        (18, 20) => 19,  // ㅡ+ㅣ → ㅢ
        _ => -1,
    };

    /// <summary>Reduce a compound vowel to its first component for backspace, or -1 for a simple vowel.</summary>
    private static int SplitJung(int jung) => jung switch
    {
        9 or 10 or 11 => 8,   // ㅘ/ㅙ/ㅚ → ㅗ
        14 or 15 or 16 => 13, // ㅝ/ㅞ/ㅟ → ㅜ
        19 => 18,             // ㅢ → ㅡ
        _ => -1,
    };

    /// <summary>Choseong index of a single final consonant when it reassigns to the next initial.</summary>
    private static int JongToCho(int jong) => jong switch
    {
        1 => 0,   // ㄱ
        2 => 1,   // ㄲ
        4 => 2,   // ㄴ
        7 => 3,   // ㄷ
        8 => 5,   // ㄹ
        16 => 6,  // ㅁ
        17 => 7,  // ㅂ
        19 => 9,  // ㅅ
        20 => 10, // ㅆ
        21 => 11, // ㅇ
        22 => 12, // ㅈ
        23 => 14, // ㅊ
        24 => 15, // ㅋ
        25 => 16, // ㅌ
        26 => 17, // ㅍ
        27 => 18, // ㅎ
        _ => -1,
    };
}
