using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// renders a Lyric as the one standard lyric file: Enhanced LRC as written by LDDC, e.g.
///
/// <code>
/// [00:09.740]&lt;00:09.740&gt;(Yeah) &lt;00:09.963&gt;We &lt;00:10.292&gt;&lt;00:10.519&gt;do &lt;00:10.837&gt;
/// [00:09.740]&lt;00:09.740&gt;translation&lt;00:10.837&gt;
/// </code>
///
/// a &lt;t&gt; tag sits right before each word; a tag with no text after it ends the previous word
/// where it stops short of the next one (a gap). Lines end with CRLF like LDDC's files.
///
/// A romanization is written as a line of its own just above the original, at the same time: word-synced like the
/// original in a word-synced file ("[t]&lt;t&gt;romaji&lt;t&gt;"), and as plain "[t]romaji" in a line-synced one.
/// </summary>
public static class StandardLrcFormatter
{
    private const string Eol = "\r\n";
    private const string Tool = "Music Manager";
    private const char Bom = (char)0xFEFF; // spelled as a number so the invisible character cannot get lost from the source

    // ti/ar/al are written first, the other three are regenerated; anything else (e.g. by) survives
    private static readonly HashSet<string> ReservedKeys = new HashSet<string> { "ti", "ar", "al", "offset", "tool", "re", "ve" };
    private static readonly Regex HeaderKey = new Regex(@"\A[A-Za-z][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex LineBreak = new Regex(@"\r\n|\r|\n", RegexOptions.CultureInvariant);

    // Java's MULTILINE ^ matches at the start and after CR, LF, NEL (x85), LINE SEPARATOR (Zl) or PARAGRAPH SEPARATOR (Zp); \d is ASCII only
    private static readonly Regex StandardWordLine = new Regex(
            @"(?<![^\r\n\x85\p{Zl}\p{Zp}])\[[0-9]{2,}:[0-9]{2}\.[0-9]{3}\]<[0-9]{2,}:[0-9]{2}\.[0-9]{3}>",
            RegexOptions.CultureInvariant);

    private enum Mode
    {
        Word, Line, Plain
    }

    /// <summary>the complete file text for a lyric; never throws on odd data</summary>
    public static string Format(Lyric lyric)
    {
        StringBuilder output = new StringBuilder();
        if (lyric == null)
        {
            return output.ToString();
        }
        AppendHeader(output, lyric);
        output.Append(Eol);

        List<LyricLine> lines = lyric.Lines;
        if (lines == null)
        {
            return output.ToString();
        }
        Mode mode = ModeOf(lyric, lines);
        foreach (LyricLine line in lines)
        {
            if (line != null)
            {
                AppendLine(output, line, mode);
            }
        }
        return output.ToString();
    }

    /// <summary>mm:ss.SSS; minutes keep counting past 99 and a negative time is 0</summary>
    public static string FormatTimestamp(long ms)
    {
        long t = Math.Max(0, ms);
        StringBuilder sb = new StringBuilder(9);
        Pad(sb, t / 60000, 2).Append(':');
        Pad(sb, t / 1000 % 60, 2).Append('.');
        return Pad(sb, t % 1000, 3).ToString();
    }

    /// <summary>true when some line starts with [mm:ss.SSS]&lt;mm:ss.SSS&gt; (3-digit fractions)</summary>
    public static bool IsStandardWordSynced(string lrcText)
    {
        if (lrcText == null)
        {
            return false;
        }
        string text = lrcText.Length > 0 && lrcText[0] == Bom ? lrcText.Substring(1) : lrcText;
        return StandardWordLine.IsMatch(text);
    }

    private static StringBuilder Pad(StringBuilder sb, long value, int width)
    {
        string digits = value.ToString(CultureInfo.InvariantCulture);
        for (int i = digits.Length; i < width; i++)
        {
            sb.Append('0');
        }
        return sb.Append(digits);
    }

    private static Mode ModeOf(Lyric lyric, List<LyricLine> lines)
    {
        if (lyric.GetSyncType() == SyncType.WordSync)
        {
            return Mode.Word;
        }
        foreach (LyricLine line in lines)
        {
            if (line != null && line.IsWordSynced)
            {
                return Mode.Word;
            }
        }
        return lyric.GetSyncType() == SyncType.NoSync ? Mode.Plain : Mode.Line;
    }

    private static void AppendHeader(StringBuilder output, Lyric lyric)
    {
        OrderedDictionary<string, string> metadata = lyric.Metadata;
        foreach (string key in new string[] { "ti", "ar", "al" })
        {
            foreach (KeyValuePair<string, string> entry in metadata)
            {
                if (string.Equals(key, entry.Key, StringComparison.OrdinalIgnoreCase))
                {
                    AppendTag(output, key, entry.Value);
                    break;
                }
            }
        }
        foreach (KeyValuePair<string, string> entry in metadata)
        {
            string key = entry.Key;
            if (key != null && HeaderKey.IsMatch(key)
                    && !ReservedKeys.Contains(key.ToLowerInvariant()))
            {
                AppendTag(output, key, entry.Value);
            }
        }
        output.Append("[offset:").Append(lyric.OffsetMs.ToString(CultureInfo.InvariantCulture)).Append(']').Append(Eol);
        output.Append("[tool:").Append(Tool).Append(']').Append(Eol);
    }

    private static void AppendTag(StringBuilder output, string key, string value)
    {
        string clean = JavaString.Trim(OneLine(value));
        if (clean.Length > 0)
        {
            output.Append('[').Append(key).Append(':').Append(clean).Append(']').Append(Eol);
        }
    }

    private static void AppendLine(StringBuilder output, LyricLine line, Mode mode)
    {
        long start = Math.Max(0, line.StartTimeMs);
        long end = Math.Max(start, line.EndTimeMs);
        string text = OneLine(line.Line);
        List<string> translations = TranslationsOf(line);

        if (mode == Mode.Plain)
        {
            output.Append(text).Append(Eol);
            foreach (string translation in translations)
            {
                output.Append(translation).Append(Eol);
            }
            return;
        }

        string stamp = FormatTimestamp(start);
        if (mode == Mode.Line)
        {
            // a romanization is a line of its own, just above the original at the same time
            string romaji = LineRomaji(line);
            if (romaji.Length > 0)
            {
                output.Append('[').Append(stamp).Append(']').Append(romaji).Append(Eol);
            }
            output.Append('[').Append(stamp).Append(']').Append(text).Append(Eol);
            foreach (string translation in translations)
            {
                output.Append('[').Append(stamp).Append(']').Append(translation).Append(Eol);
            }
            return;
        }

        // a romanization is a word-synced line of its own, written just above the original at the same time
        if (line.HasRomanization())
        {
            List<WordTimeStamp> romanized = WordsOf(line.RomanizedWords);
            if (romanized.Count > 0)
            {
                output.Append('[').Append(stamp).Append(']');
                AppendWords(output, romanized, start);
                output.Append(Eol);
            }
        }

        output.Append('[').Append(stamp).Append(']');
        List<WordTimeStamp> words = WordsOf(line);
        if (words.Count > 0)
        {
            AppendWords(output, words, start);
        }
        else if (!JavaText.IsBlank(text))
        {
            // a line with no words inside a word-synced lyric becomes one word covering the line
            output.Append('<').Append(stamp).Append('>').Append(text).Append('<').Append(FormatTimestamp(end)).Append('>');
        }
        output.Append(Eol);
        foreach (string translation in translations)
        {
            output.Append('[').Append(stamp).Append("]<").Append(stamp).Append('>').Append(translation)
                    .Append('<').Append(FormatTimestamp(end)).Append('>').Append(Eol);
        }
    }

    /// <summary>
    /// a tag before every word and, where a word stops short of the next one (or is the last), a tag for its end.
    /// Times are first made non-decreasing so no tag can ever go backwards.
    /// </summary>
    private static void AppendWords(StringBuilder output, List<WordTimeStamp> words, long lineStart)
    {
        int n = words.Count;
        long[] starts = new long[n];
        long floor = lineStart;
        for (int i = 0; i < n; i++)
        {
            starts[i] = Math.Max(words[i].StartTimeMs, floor);
            floor = starts[i];
        }
        for (int i = 0; i < n; i++)
        {
            long end = Math.Max(words[i].EndTimeMs, starts[i]);
            if (i + 1 < n)
            {
                end = Math.Min(end, starts[i + 1]);
            }
            output.Append('<').Append(FormatTimestamp(starts[i])).Append('>').Append(OneLine(words[i].Word));
            if (i + 1 == n || starts[i + 1] != end)
            {
                output.Append('<').Append(FormatTimestamp(end)).Append('>');
            }
        }
    }

    /// <summary>the romanization of a line-synced line as the plain text it is written as; empty when it has none</summary>
    private static string LineRomaji(LyricLine line)
    {
        StringBuilder romaji = new StringBuilder();
        if (line.HasRomanization())
        {
            foreach (WordTimeStamp word in WordsOf(line.RomanizedWords))
            {
                romaji.Append(word.Word);
            }
        }
        return JavaText.Strip(OneLine(romaji.ToString()));
    }

    /// <summary>the words that can be written: not null and not empty</summary>
    private static List<WordTimeStamp> WordsOf(LyricLine line)
    {
        return WordsOf(line.WordTimeStamps);
    }

    private static List<WordTimeStamp> WordsOf(WordTimeStamp[] stamps)
    {
        List<WordTimeStamp> words = new List<WordTimeStamp>();
        if (stamps != null)
        {
            foreach (WordTimeStamp word in stamps)
            {
                if (word != null && word.Word != null && word.Word.Length > 0)
                {
                    words.Add(word);
                }
            }
        }
        return words;
    }

    private static List<string> TranslationsOf(LyricLine line)
    {
        List<string> translations = new List<string>();
        if (line.Translations != null)
        {
            foreach (string translation in line.Translations)
            {
                if (translation != null && !JavaText.IsBlank(translation))
                {
                    translations.Add(OneLine(translation));
                }
            }
        }
        return translations;
    }

    /// <summary>a lyric line must stay on one physical line</summary>
    private static string OneLine(string s)
    {
        return s == null ? "" : LineBreak.Replace(s, " ");
    }
}
