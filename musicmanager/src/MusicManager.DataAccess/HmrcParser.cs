using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.DataAccess;

/// <summary>
/// parses the home-made .hmrc word-timed format, e.g.
///
/// <code>
/// [offset:0]
/// [3631,2107]&lt;0,594&gt;Careful, &lt;594,255&gt;'cause &lt;849,380&gt;I &lt;1229,878&gt;Bite&lt;2107,0&gt;^translation
/// </code>
///
/// a line is [startMs,durMs] followed by &lt;startRelativeToLine,durMs&gt;text word segments and an optional
/// ^translation tail. The old writer added [offset:] into every time, so the offset is taken back out here
/// and the returned Lyric (offsetMs 0) carries the original timeline.
/// </summary>
public sealed class HmrcParser
{
    // times beyond an int's range are corrupt: clamp them so the arithmetic below can never overflow a long
    private const long MaxMs = int.MaxValue;
    private const char Bom = '\uFEFF';

    // Java's \s is [ \t\n\x0B\f\r], \d is [0-9] and . excludes the line terminators; .NET's are wider, so spelled out.
    private const string Space = @"[ \t\n\x0B\f\r]";
    private const string AnyChar = @"[^\n\r\u0085\u2028\u2029]";

    // "[startMs,durMs]"; the start is negative in files written with a negative [offset:]
    private static readonly Regex LineHead = new Regex(
        @"^" + Space + @"*\[" + Space + @"*(-?[0-9]+)" + Space + @"*," + Space + @"*(-?[0-9]+)" + Space + @"*\]",
        RegexOptions.CultureInvariant);
    // a word marker is exactly <int,int>; any other '<' is lyric text
    private static readonly Regex Marker = new Regex(@"<(-?[0-9]+),(-?[0-9]+)>", RegexOptions.CultureInvariant);
    // header tags such as [offset:0] or [ti:Title]; always used as a whole-row match
    private static readonly Regex Header = new Regex(
        @"^" + Space + @"*\[([A-Za-z]+):(" + AnyChar + @"*)\]" + Space + @"*\z", RegexOptions.CultureInvariant);
    private static readonly Regex NumberPattern = new Regex(@"^[+-]?[0-9]+\z", RegexOptions.CultureInvariant);
    private static readonly Regex LineBreak = new Regex(@"\r\n|\r|\n", RegexOptions.CultureInvariant);
    // the writer copied XML-escaped text from the .ttml, so lyrics read "Bonnie &amp; Clyde"
    private static readonly Regex XmlEntity = new Regex(@"&(amp|lt|gt|quot|apos);", RegexOptions.CultureInvariant);

    private long _offsetMs;
    private int _outOfRange; // numbers clamped to MaxMs
    private int _belowZero; // times that fell under 0 once the offset was removed
    private int _retimed; // words whose overlapping/reversed timing was straightened out
    private int _entities; // XML entities turned back into characters

    private HmrcParser()
    {
    }

    /// <param name="text">the file content</param>
    /// <param name="path">where it came from, stored on the Lyric</param>
    /// <exception cref="LyricParseException">only when not a single lyric line could be read</exception>
    public static Lyric Parse(string text, string path)
    {
        return new HmrcParser().Run(text ?? "", path);
    }

    private Lyric Run(string text, string path)
    {
        if (text.Length > 0 && text[0] == Bom)
        {
            text = text.Substring(1);
        }
        string[] rows = LineBreak.Split(text);

        // headers first: the offset has to be known before any line time can be restored
        OrderedDictionary<string, string> metadata = new OrderedDictionary<string, string>();
        List<string> warnings = new List<string>();
        foreach (string row in rows)
        {
            Match header = Header.Match(row);
            if (header.Success)
            {
                ReadHeader(header.Groups[1].Value.ToLowerInvariant(), Strip(header.Groups[2].Value), metadata, warnings);
            }
        }

        List<LyricLine> lines = new List<LyricLine>();
        bool anyWords = false;
        int skipped = 0;
        int firstSkipped = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            string row = rows[i];
            if (IsBlank(row))
            {
                continue;
            }
            Match head = LineHead.Match(row);
            if (head.Success)
            {
                LyricLine line = BuildLine(row.Substring(head.Index + head.Length), Number(head.Groups[1].Value),
                    Number(head.Groups[2].Value));
                anyWords |= line.IsWordSynced;
                lines.Add(line);
            }
            else if (!Header.IsMatch(row))
            {
                if (skipped++ == 0)
                {
                    firstSkipped = i + 1;
                }
            }
        }

        if (lines.Count == 0)
        {
            throw new LyricParseException("No lyric lines found in HMRC data" + (path == null ? "" : ": " + path));
        }

        if (_offsetMs != 0)
        {
            warnings.Add("[offset:" + _offsetMs.ToString(CultureInfo.InvariantCulture)
                + "] was already added to every time in the file; removed it");
        }
        if (skipped > 0)
        {
            warnings.Add(skipped.ToString(CultureInfo.InvariantCulture) + " unreadable line(s) skipped (first at line "
                + firstSkipped.ToString(CultureInfo.InvariantCulture) + ")");
        }
        if (_outOfRange > 0)
        {
            warnings.Add(_outOfRange.ToString(CultureInfo.InvariantCulture) + " out-of-range number(s) clamped to "
                + MaxMs.ToString(CultureInfo.InvariantCulture));
        }
        if (_belowZero > 0)
        {
            warnings.Add(_belowZero.ToString(CultureInfo.InvariantCulture) + " time(s) fell below 0 and were clamped to 0");
        }
        if (_retimed > 0)
        {
            warnings.Add(_retimed.ToString(CultureInfo.InvariantCulture)
                + " word(s) with overlapping or reversed timing were adjusted");
        }
        if (_entities > 0)
        {
            warnings.Add(_entities.ToString(CultureInfo.InvariantCulture)
                + " XML entity(ies) such as &amp; turned back into characters");
        }

        Lyric lyric = new Lyric(anyWords ? SyncType.WordSync : SyncType.LineSync, lines, path);
        foreach (KeyValuePair<string, string> entry in metadata)
        {
            lyric.Metadata[entry.Key] = entry.Value;
        }
        warnings.ForEach(lyric.AddWarning);
        return lyric;
    }

    private void ReadHeader(string key, string value, OrderedDictionary<string, string> metadata, List<string> warnings)
    {
        if (key != "offset")
        {
            metadata[key] = value;
        }
        else if (NumberPattern.IsMatch(value))
        {
            _offsetMs = Number(value);
        }
        else
        {
            warnings.Add("unreadable [offset:" + value + "] ignored");
        }
    }

    /// <summary>one lyric line; rest is everything after the [start,dur] head</summary>
    private LyricLine BuildLine(string rest, long writtenStart, long writtenDur)
    {
        long start = Restore(writtenStart);
        long end = Math.Max(start, Restore(writtenStart + writtenDur));

        // only the first '^' splits; the translation itself may contain more
        int caret = rest.IndexOf('^');
        string body = caret < 0 ? rest : JavaString.Substring(rest, 0, caret);
        string translation = caret < 0 ? "" : Strip(Decode(rest.Substring(caret + 1)));

        // cut the body into the text before the first marker and one text per marker
        List<long[]> markers = new List<long[]>(); // {relStart, dur}
        List<string> texts = new List<string>();
        string lead = body;
        int textFrom = -1;
        for (Match m = Marker.Match(body); m.Success; m = m.NextMatch())
        {
            if (textFrom < 0)
            {
                lead = JavaString.Substring(body, 0, m.Index);
            }
            else
            {
                texts.Add(JavaString.Substring(body, textFrom, m.Index));
            }
            markers.Add(new[] { Number(m.Groups[1].Value), Number(m.Groups[2].Value) });
            textFrom = m.Index + m.Length;
        }
        if (textFrom >= 0)
        {
            texts.Add(body.Substring(textFrom));
        }

        // a marker with no text (e.g. the <lineDur,0> before '^') is not a word
        List<WordTimeStamp> words = new List<WordTimeStamp>();
        for (int i = 0; i < markers.Count; i++)
        {
            if (texts[i].Length == 0)
            {
                continue;
            }
            long wordStart = Restore(writtenStart + markers[i][0]);
            long wordEnd = Restore(writtenStart + markers[i][0] + Math.Max(0L, markers[i][1]));
            words.Add(new WordTimeStamp(Decode(texts[i]), wordStart, wordEnd));
        }

        LyricLine line;
        if (words.Count == 0)
        {
            // no word segments: plain text (possibly empty) makes a line-synced line
            line = new LyricLine(Strip(Decode(lead)), start, end);
            line.WordTimeStamps = new WordTimeStamp[0];
        }
        else
        {
            if (!IsBlank(lead))
            {
                words.Insert(0, new WordTimeStamp(Decode(lead), start, words[0].StartTimeMs));
            }
            line = new LyricLine(StripTrailing(Join(words)), start, end);
            line.WordTimeStamps = Straighten(words, start);
        }
        line.AddTranslation(translation);
        return line;
    }

    /// <summary>
    /// makes word times non-decreasing and inside [lineStart, ...): real files have the odd word that starts
    /// before the previous one ended or before its own line
    /// </summary>
    private WordTimeStamp[] Straighten(List<WordTimeStamp> words, long lineStart)
    {
        int n = words.Count;
        long[] starts = new long[n];
        long[] ends = new long[n];
        long floor = lineStart;
        for (int i = 0; i < n; i++)
        {
            starts[i] = Math.Max(words[i].StartTimeMs, floor);
            floor = starts[i];
        }
        WordTimeStamp[] result = new WordTimeStamp[n];
        for (int i = 0; i < n; i++)
        {
            ends[i] = Math.Max(words[i].EndTimeMs, starts[i]);
            if (i + 1 < n)
            {
                ends[i] = Math.Min(ends[i], starts[i + 1]);
            }
            if (starts[i] != words[i].StartTimeMs || ends[i] != words[i].EndTimeMs)
            {
                _retimed++;
            }
            result[i] = new WordTimeStamp(words[i].Word, starts[i], ends[i]);
        }
        return result;
    }

    /// <summary>only the five predefined XML entities ever show up; a single pass so "&amp;lt;" becomes "&lt;"</summary>
    private string Decode(string text)
    {
        if (text.IndexOf('&') < 0)
        {
            return text;
        }
        return XmlEntity.Replace(text, m =>
        {
            _entities++;
            switch (m.Groups[1].Value)
            {
                case "amp": return "&";
                case "lt": return "<";
                case "gt": return ">";
                case "quot": return "\"";
                default: return "'";
            }
        });
    }

    private static string Join(List<WordTimeStamp> words)
    {
        StringBuilder sb = new StringBuilder();
        foreach (WordTimeStamp word in words)
        {
            sb.Append(word.Word);
        }
        return sb.ToString();
    }

    /// <summary>takes the offset back out of a written time; nothing can be earlier than the start of the song</summary>
    private long Restore(long writtenMs)
    {
        long ms = writtenMs - _offsetMs;
        if (ms < 0)
        {
            _belowZero++;
            return 0;
        }
        return ms;
    }

    /// <summary>reads an optionally signed run of digits, clamping to +-MaxMs instead of overflowing</summary>
    private long Number(string s)
    {
        bool negative = s.StartsWith("-", StringComparison.Ordinal);
        int from = negative || s.StartsWith("+", StringComparison.Ordinal) ? 1 : 0;
        while (from < s.Length - 1 && s[from] == '0')
        {
            from++; // leading zeros must not count towards the length below
        }
        string digits = s.Substring(from);
        long value = digits.Length > 10 ? MaxMs + 1 : long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture); // 10 digits always fit a long
        if (value > MaxMs)
        {
            value = MaxMs;
            _outOfRange++;
        }
        return negative ? -value : value;
    }

    // ---- Java's strip()/stripTrailing()/isBlank() use Character.isWhitespace, which is not the set char.IsWhiteSpace
    // (and so string.Trim) uses: it excludes the no-break spaces U+00A0, U+2007, U+202F and U+0085, and includes
    // U+001C..U+001F. Spelled out so a lyric that ends in a no-break space keeps it, as it did in Java.

    private static bool IsJavaWhitespace(char c)
    {
        if (c == '\t' || c == '\n' || c == '\u000B' || c == '\f' || c == '\r' || (c >= '\u001C' && c <= '\u001F'))
        {
            return true;
        }
        if (c == '\u00A0' || c == '\u2007' || c == '\u202F')
        {
            return false;
        }
        switch (char.GetUnicodeCategory(c))
        {
            case UnicodeCategory.SpaceSeparator:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
                return true;
            default:
                return false;
        }
    }

    private static bool IsBlank(string s)
    {
        foreach (char c in s)
        {
            if (!IsJavaWhitespace(c))
            {
                return false;
            }
        }
        return true;
    }

    private static string StripTrailing(string s)
    {
        int end = s.Length;
        while (end > 0 && IsJavaWhitespace(s[end - 1]))
        {
            end--;
        }
        return JavaString.Substring(s, 0, end);
    }

    private static string Strip(string s)
    {
        int start = 0;
        while (start < s.Length && IsJavaWhitespace(s[start]))
        {
            start++;
        }
        return StripTrailing(s.Substring(start));
    }
}
