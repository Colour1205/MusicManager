using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// A lyric file written the LDDC way has a Korean or Japanese line preceded by a transliteration of it, at the same
/// time ("[00:12.591]&lt;..&gt;门的 &lt;..&gt;塔里" above "[00:12.591]&lt;..&gt;문득 &lt;..&gt;달이"). A file reader only sees two lines (or one
/// line and its "translation"); this folds the transliteration back into the original line, where the entity model
/// keeps it (<see cref="LyricLine.RomanizedWords"/>), so that it is not mistaken for a lyric of its own. Two shapes:
///
/// <list type="bullet">
/// <item><b>word-synced file</b>: two word-synced lines that start together, the first without any Hangul or kana and
/// the second with some. Both need two or more timed words.</item>
/// <item><b>line-level file</b> (no line has two or more timed words: LDDC's "[t]text[t]" output or a plain "[t]text"
/// file): <c>LrcParser</c> turns a second line at the same time into a <i>translation</i> of the first, so the
/// transliteration arrives as a line with no Hangul or kana whose first translation has some. The translation is the
/// original and the line is its romaji. An English original with a Korean translation under it has the same shape and
/// is read the same way: the English line counts as the Korean line's (Latin) romaji and nothing is added for it.</item>
/// </list>
///
/// A line that is the only one at its time, or whose neighbour is the same language, is never touched.
/// </summary>
public static class RomajiGrouper
{
    /// <summary>merges every romanization line into the line it belongs to, in place; returns the lyric for chaining</summary>
    public static Lyric Group(Lyric lyric)
    {
        if (lyric == null || lyric.Lines == null || lyric.Lines.Count == 0)
        {
            return lyric;
        }
        List<LyricLine> lines = lyric.Lines;
        bool lineLevel = RomajiScripts.IsLineLevel(lyric);
        List<LyricLine> merged = new List<LyricLine>(lines.Count);
        int merges = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            LyricLine line = lines[i];
            LyricLine next = i + 1 < lines.Count ? lines[i + 1] : null;
            if (IsRomanizationOf(line, next))
            {
                next.RomanizedWords = line.WordTimeStamps;
                merges++;
                continue; // the original line (next) is added on the next iteration
            }
            LyricLine original = lineLevel ? FoldTranslation(line) : null;
            if (original != null)
            {
                merged.Add(original);
                merges++;
                continue;
            }
            merged.Add(line);
        }
        if (merges > 0)
        {
            lyric.Lines = merged;
        }
        return lyric;
    }

    private static bool IsRomanizationOf(LyricLine candidate, LyricLine original)
    {
        if (candidate == null || original == null || original.HasRomanization())
        {
            return false;
        }
        if (candidate.StartTimeMs != original.StartTimeMs)
        {
            return false;
        }
        if (!RomajiScripts.NeedsRomaji(original))
        {
            return false;
        }
        return candidate.WordTimeStamps != null
                && candidate.WordTimeStamps.Length >= RomajiScripts.MinWords
                && !RomajiScripts.HasHangulOrKana(candidate.Line)
                && candidate.Line != null && JavaText.HasLetter(candidate.Line);
    }

    /// <summary>
    /// the line a "romaji line with the original as its first translation" really is: the original, carrying the
    /// romaji, or null when the line is not that shape
    /// </summary>
    private static LyricLine FoldTranslation(LyricLine candidate)
    {
        if (candidate == null || candidate.HasRomanization() || candidate.Translations == null
                || candidate.Translations.Count == 0)
        {
            return null;
        }
        string text = candidate.Line;
        string originalText = candidate.Translations[0];
        if (text == null || originalText == null || RomajiScripts.HasHangulOrKana(text)
                || !JavaText.HasLetter(text) || !RomajiScripts.HasHangulOrKana(originalText))
        {
            return null;
        }

        long start = candidate.StartTimeMs;
        long end = Math.Max(start, candidate.EndTimeMs);
        WordTimeStamp[] ownWords = candidate.WordTimeStamps;
        bool timedWords = ownWords != null && ownWords.Length > 0;

        // the romaji keeps the timing it had; a line without word timing gets the whole line as its one word
        WordTimeStamp[] romaji = timedWords ? ownWords : new WordTimeStamp[] { new WordTimeStamp(text, start, end) };

        LyricLine original = new LyricLine(JavaText.StripTrailing(originalText), start, end);
        original.WordTimeStamps = timedWords
                ? new WordTimeStamp[] { new WordTimeStamp(originalText, ownWords[0].StartTimeMs, end) }
                : new WordTimeStamp[0];
        original.Translations = candidate.Translations.GetRange(1, candidate.Translations.Count - 1);
        original.RomanizedWords = romaji;
        return original;
    }
}
