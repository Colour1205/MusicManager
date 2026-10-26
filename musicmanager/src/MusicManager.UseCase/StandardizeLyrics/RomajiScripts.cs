using System.Text;
using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// which lines need a romanization and what counts as one. Only Korean (Hangul) and Japanese (kana) lines get a
/// romaji line; a line of Chinese characters alone cannot be told from Chinese and is left alone. Romaji is only for
/// Korean and Japanese: the Chinese characters inside a Korean line stay Chinese characters in its romaji line (a line
/// with any kana is Japanese, and its kanji are romanized as Japanese words).
/// </summary>
public static class RomajiScripts
{
    /// <summary>a romanization needs per-word timing to be worth writing, and a line must have at least this many words</summary>
    public const int MinWords = 2;

    // Character.UnicodeScript.of(codePoint) == HANGUL / HIRAGANA / KATAKANA / HAN, as ranges of code points (taken from the JDK)
    private static readonly (int From, int To)[] Hangul =
    {
        (0x1100, 0x11FF), (0x302E, 0x302F), (0x3131, 0x318E), (0x3200, 0x321E), (0x3260, 0x327E), (0xA960, 0xA97C),
        (0xAC00, 0xD7A3), (0xD7B0, 0xD7C6), (0xD7CB, 0xD7FB), (0xFFA0, 0xFFBE), (0xFFC2, 0xFFC7), (0xFFCA, 0xFFCF),
        (0xFFD2, 0xFFD7), (0xFFDA, 0xFFDC)
    };

    private static readonly (int From, int To)[] Hiragana =
    {
        (0x3041, 0x3096), (0x309D, 0x309F), (0x1B001, 0x1B11F), (0x1B132, 0x1B132), (0x1B150, 0x1B152), (0x1F200, 0x1F200)
    };

    private static readonly (int From, int To)[] Katakana =
    {
        (0x30A1, 0x30FA), (0x30FD, 0x30FF), (0x31F0, 0x31FF), (0x32D0, 0x32FE), (0x3300, 0x3357), (0xFF66, 0xFF6F),
        (0xFF71, 0xFF9D), (0x1AFF0, 0x1AFF3), (0x1AFF5, 0x1AFFB), (0x1AFFD, 0x1AFFE), (0x1B000, 0x1B000),
        (0x1B120, 0x1B122), (0x1B155, 0x1B155), (0x1B164, 0x1B167)
    };

    private static readonly (int From, int To)[] Han =
    {
        (0x2E80, 0x2E99), (0x2E9B, 0x2EF3), (0x2F00, 0x2FD5), (0x3005, 0x3005), (0x3007, 0x3007), (0x3021, 0x3029),
        (0x3038, 0x303B), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xF900, 0xFA6D), (0xFA70, 0xFAD9), (0x16FE2, 0x16FE3),
        (0x16FF0, 0x16FF6), (0x20000, 0x2A6DF), (0x2A700, 0x2B81D), (0x2B820, 0x2CEAD), (0x2CEB0, 0x2EBE0),
        (0x2EBF0, 0x2EE5D), (0x2F800, 0x2FA1D), (0x30000, 0x3134A), (0x31350, 0x33479)
    };

    /// <summary>true if the text has any Hangul or kana</summary>
    public static bool HasHangulOrKana(string text)
    {
        return text != null && JavaText.AnyCodePoint(text, IsHangulOrKana);
    }

    /// <summary>true if the text has any character that is not in the Latin alphabet's world: Hangul, kana or Chinese characters</summary>
    public static bool HasCjk(string text)
    {
        return text != null && JavaText.AnyCodePoint(text, cp => IsHangulOrKana(cp) || IsHan(cp));
    }

    private static bool IsHangulOrKana(int codePoint)
    {
        return In(Hangul, codePoint) || In(Hiragana, codePoint) || In(Katakana, codePoint);
    }

    /// <summary>true for a Chinese character (a Han code point), whatever language it is used in</summary>
    public static bool IsHan(int codePoint)
    {
        return In(Han, codePoint);
    }

    /// <summary>true if the text has any hiragana or katakana: a line with some is a Japanese line</summary>
    public static bool HasKana(string text)
    {
        return text != null && JavaText.AnyCodePoint(text, cp => In(Hiragana, cp) || In(Katakana, cp));
    }

    /// <summary>
    /// The Chinese characters of the text, in order, as one string: how two texts are told to keep the same Chinese
    /// characters whatever else changed. Every character goes through compatibility normalization first, so the same
    /// character written as a CJK compatibility ideograph (U+F900..., common in Korean hanja) or a Kangxi radical is the
    /// same character. Works per code point, so a lone surrogate cannot make it throw.
    /// </summary>
    public static string HanSequence(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        StringBuilder han = new StringBuilder();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (IsHan(rune.Value))
            {
                han.Append(rune.ToString().Normalize(NormalizationForm.FormKC));
            }
        }
        return han.ToString();
    }

    /// <summary>true if the text has a letter that is not a Chinese character, a Hangul syllable or a kana</summary>
    public static bool HasNonHanLetter(string text)
    {
        return text != null && JavaText.AnyCodePoint(text, cp => JavaText.IsLetter(cp) && !IsHan(cp) && !IsHangulOrKana(cp));
    }

    private static bool In((int From, int To)[] ranges, int codePoint)
    {
        if (codePoint < 0x1100)
        {
            return false; // below every range
        }
        foreach ((int first, int last) in ranges)
        {
            if (codePoint >= first && codePoint <= last)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>a Korean/Japanese line of a word-synced file (it must have enough timed words) that should have a romaji line</summary>
    public static bool NeedsRomaji(LyricLine line)
    {
        return NeedsRomaji(line, false);
    }

    /// <summary>
    /// a Korean/Japanese line that should have a romaji line. In a word-synced file only a line with enough timed words
    /// qualifies; in a line-level file (see <see cref="IsLineLevel(Lyric)"/>) every such line does, because its one "word" is
    /// the whole line.
    /// </summary>
    public static bool NeedsRomaji(LyricLine line, bool lineLevel)
    {
        if (line == null || !HasHangulOrKana(WithoutFillers(line.Line)))
        {
            return false;
        }
        return lineLevel || (line.WordTimeStamps != null && line.WordTimeStamps.Length >= MinWords);
    }

    /// <summary>the text without the invisible Hangul fillers (a line of nothing else has nothing to romanize)</summary>
    private static string WithoutFillers(string text)
    {
        if (text == null)
        {
            return null;
        }
        StringBuilder kept = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c != (char)0x3164 && c != (char)0xFFA0 && c != (char)0x115F && c != (char)0x1160)
            {
                kept.Append(c);
            }
        }
        return kept.ToString();
    }

    /// <summary>
    /// true for a timed lyric in which no line has two or more timed words: LDDC's "[t]text[t]" output (one timed word
    /// per line) or a plain "[t]text" file. Untimed lyrics are not line-level, they have no timing at all.
    /// </summary>
    public static bool IsLineLevel(Lyric lyric)
    {
        if (lyric == null || lyric.GetSyncType() == Lyric.SyncType.NoSync || lyric.Lines == null
                || lyric.Lines.Count == 0)
        {
            return false;
        }
        foreach (LyricLine line in lyric.Lines)
        {
            if (line != null && line.WordTimeStamps != null && line.WordTimeStamps.Length >= MinWords)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// true if the line's romanization is what the standard wants: Latin romaji of the Korean or Japanese line, with the
    /// Chinese characters of a Korean line kept (see <see cref="IsLatinRomanizationOf"/>). Old romaji in Chinese
    /// characters (LDDC's) is not, and neither is romaji that turned a Korean line's Chinese characters into pinyin.
    /// </summary>
    public static bool HasLatinRomanization(LyricLine line)
    {
        return line != null && line.HasRomanization() && IsLatinRomanizationOf(OriginalText(line), line.RomanizedWords);
    }

    /// <summary>
    /// The one rule for a romanized line, shared by what Claude's answers must satisfy and by the check of a written
    /// file, so an answer that is accepted is never judged wrong when the file is read back:
    /// <list type="bullet">
    /// <item>no Hangul and no kana in it, and at least one letter that is not a Chinese character;</item>
    /// <item>a Korean line (no kana) keeps its Chinese characters: the romanization has exactly the original's Chinese
    /// characters, in the same order. Chinese characters are not romanized, only Korean and Japanese are;</item>
    /// <item>a Japanese line (some kana) has no Chinese character left in it: its kanji are Japanese words and are
    /// romanized.</item>
    /// </list>
    /// </summary>
    public static bool IsLatinRomanizationOf(string original, WordTimeStamp[] romanized)
    {
        if (romanized == null)
        {
            return false;
        }
        StringBuilder text = new StringBuilder();
        bool letter = false;
        bool japanese = HasKana(original);
        foreach (WordTimeStamp word in romanized)
        {
            string piece = word == null ? null : word.Word;
            if (piece == null)
            {
                continue;
            }
            // a Japanese line's prolonged sound mark (ー) is romanized too: it is not a Latin letter that may stay
            if (HasHangulOrKana(piece) || (japanese && (piece.Contains('ー') || piece.Contains('ｰ'))))
            {
                return false;
            }
            text.Append(piece);
            letter |= HasNonHanLetter(piece);
        }
        string wanted = japanese ? "" : HanSequence(original);
        return letter && HanSequence(text.ToString()) == wanted;
    }

    /// <summary>the text of a line as it was sung: its timed words joined, or the line itself when it has no word timing</summary>
    private static string OriginalText(LyricLine line)
    {
        WordTimeStamp[] words = line.WordTimeStamps;
        if (words == null || words.Length == 0)
        {
            return line.Line ?? "";
        }
        StringBuilder text = new StringBuilder();
        foreach (WordTimeStamp word in words)
        {
            text.Append(word == null ? null : word.Word);
        }
        return text.ToString();
    }

    /// <summary>true if no word contains Hangul, kana or Chinese characters, and some word has a letter</summary>
    public static bool IsLatin(WordTimeStamp[] words)
    {
        bool anyLetter = false;
        foreach (WordTimeStamp word in words)
        {
            string text = word == null ? null : word.Word;
            if (text == null)
            {
                continue;
            }
            if (HasCjk(text))
            {
                return false;
            }
            anyLetter |= JavaText.HasLetter(text);
        }
        return anyLetter;
    }

    /// <summary>true if the text is made of Latin letters (and digits and punctuation), with no Hangul, kana or Chinese characters</summary>
    public static bool IsLatin(string text)
    {
        return text != null && !HasCjk(text) && JavaText.HasLetter(text);
    }

    /// <summary>the words' text joined into one string, as the words would be sung</summary>
    public static string Join(WordTimeStamp[] words)
    {
        StringBuilder text = new StringBuilder();
        foreach (WordTimeStamp word in words)
        {
            text.Append(word.Word ?? "null"); // Java's StringBuilder.append((String) null) writes "null"
        }
        return text.ToString();
    }
}
