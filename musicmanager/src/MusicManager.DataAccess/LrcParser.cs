using System.Globalization;
using System.Text;
using MusicManager.Common;
using MusicManager.Entity;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.DataAccess;

/// <summary>
/// very tolerant .lrc text parser. It never throws: anything it has to repair or
/// drop is recorded as a warning on the returned Lyric.
///
/// Three line syntaxes are understood, even mixed in one file:
/// - line synced:        [00:03.631]Careful, 'cause I Bite
/// - legacy bracket word: [00:03.631]Careful, [00:04.225]'cause [00:04.860]Bite[00:05.738]
/// - enhanced (LDDC):     [00:03.631]&lt;00:03.631&gt;Careful, &lt;00:04.225&gt;'cause &lt;00:04.860&gt;Bite&lt;00:05.738&gt;
/// In the word syntaxes a tag opens the word that follows it, a tag with no text
/// after it closes the previous word (two in a row = "previous word ends, next one
/// starts later", i.e. a gap) and the last one closes the line.
///
/// Decisions worth knowing:
/// - word text is kept exactly (a space-only word is a word too); only the space between
///   "[line]" and an untagged first word is dropped
/// - the [offset:] header is only stored, never applied to the times
/// - a line with the same start time as the line before it is that line's translation,
///   unless it carries real word timing (2+ timed words); an empty one is dropped
/// - untimed text after a timed line becomes a translation of it (most often a
///   translation or romanization that lost its tag); untimed text before the first
///   timed line is kept as an untimed line at 0
/// </summary>
public sealed class LrcParser
{
    // anything longer is garbage; also keeps every time inside an int for downstream code
    private const long MaxTimeMs = 100L * 60 * 60 * 1000;
    private const int MaxWarnings = 100;

    private readonly OrderedDictionary<string, string> _metadata = new();
    private readonly List<string> _warnings = new();
    private long _offsetMs;
    private int _lineNo;
    private int _warningCount;

    private readonly List<Draft> _preamble = new(); // untimed lines before the first timed one
    private List<Draft> _timed = new();
    private List<Draft> _lastGroup; // drafts made from the latest timed source line (several for repeat tags)
    private bool _hadRepeatTags;
    private int _untimedAttached;

    private LrcParser()
    {
    }

    /// <summary>parses lrc text of any dialect; null or empty text gives an empty NO_SYNC lyric</summary>
    public static Lyric Parse(string text, string path)
    {
        try
        {
            return new LrcParser().Run(text ?? "", path);
        }
        catch (Exception e)
        {
            // the contract is "never throws"; a bug here must not take a whole library scan down
            Lyric lyric = new Lyric(SyncType.NoSync, new List<LyricLine>(), path);
            lyric.AddWarning("lyric text could not be parsed: " + e.GetType().FullName + ": " + e.Message);
            return lyric;
        }
    }

    /// <summary>true if some line starts with a line tag directly followed by a word tag: [mm:ss.SSS]&lt;mm:ss.SSS&gt;</summary>
    public static bool UsesEnhancedSyntax(string text)
    {
        if (text == null)
        {
            return false;
        }
        foreach (string raw in SplitLines(text))
        {
            string line = TrimStart(raw);
            if (line.Length == 0 || line[0] != '[')
            {
                continue;
            }
            int end = ScanTag(line, 0, out _);
            if (end > 0)
            {
                end = SkipSpaces(line, end);
                if (end < line.Length && line[end] == '<' && ScanTag(line, end, out _) > 0)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private Lyric Run(string text, string path)
    {
        foreach (string line in SplitLines(text))
        {
            _lineNo++;
            ReadLine(line);
        }
        return Build(path);
    }

    // ---------------------------------------------------------------- reading lines

    private void ReadLine(string raw)
    {
        string line = Trim(raw);
        if (line.Length == 0 || ReadHeader(line))
        {
            return;
        }

        List<long> starts = new List<long>();
        int bodyStart = LeadingTags(line, starts);
        if (starts.Count == 0)
        {
            AddUntimed(line);
            return;
        }

        // [t1][t2]text with nothing between the tags repeats the text at both times
        string body = line.Substring(bodyStart);
        List<Tag> tags = ScanBody(body);
        List<Draft> group = new List<Draft>();
        foreach (long start in starts)
        {
            group.Add(BuildDraft(body, tags, start, start - starts[0]));
        }
        _hadRepeatTags |= starts.Count > 1;
        _timed.AddRange(group);
        _lastGroup = group;
    }

    /// <summary>[key:value] with a letters-only key; the line is already trimmed</summary>
    private bool ReadHeader(string line)
    {
        int last = line.Length - 1;
        if (line[0] != '[' || line[last] != ']')
        {
            return false;
        }
        int colon = 1;
        while (colon < last && IsAsciiLetter(line[colon]))
        {
            colon++;
        }
        if (colon == 1 || colon >= last || line[colon] != ':')
        {
            return false;
        }
        string key = JavaString.Substring(line, 1, colon).ToLowerInvariant();
        string value = Trim(JavaString.Substring(line, colon + 1, last));
        if (key == "offset")
        {
            ReadOffset(value);
        }
        else
        {
            _metadata[key] = value;
        }
        return true;
    }

    private void ReadOffset(string value)
    {
        int digitsFrom = value.Length > 0 && (value[0] == '+' || value[0] == '-') ? 1 : 0;
        bool valid = value.Length > digitsFrom;
        for (int i = digitsFrom; i < value.Length; i++)
        {
            valid &= IsDigit(value[i]);
        }
        if (!valid)
        {
            Warn(_lineNo, "ignored unreadable offset \"" + Shorten(value) + "\"");
            _offsetMs = 0;
            return;
        }
        long magnitude = Number(value, digitsFrom, value.Length);
        if (magnitude > MaxTimeMs)
        {
            Warn(_lineNo, "offset \"" + Shorten(value) + "\" is out of range and was clamped");
            magnitude = MaxTimeMs;
        }
        _offsetMs = value[0] == '-' ? -magnitude : magnitude;
    }

    /// <summary>
    /// collects the line tags at the start of the line and returns where the text
    /// begins. A line that starts with a word tag instead of a line tag (enhanced
    /// line whose [line] tag got lost) uses that tag as its start.
    /// </summary>
    private int LeadingTags(string line, List<long> starts)
    {
        if (line[0] == '<')
        {
            int first = ScanTag(line, 0, out long firstTime);
            if (first > 0)
            {
                starts.Add(Clamp(firstTime));
                return first;
            }
            return 0;
        }

        int pos = 0;
        while (true)
        {
            int at = starts.Count == 0 ? pos : SkipSpaces(line, pos);
            if (at >= line.Length || line[at] != '[')
            {
                return pos;
            }
            int end = ScanTag(line, at, out long time);
            if (end < 0)
            {
                return pos;
            }
            starts.Add(Clamp(time));
            pos = end;
        }
    }

    private List<Tag> ScanBody(string body)
    {
        List<Tag> tags = new List<Tag>();
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '[' || c == '<')
            {
                int end = ScanTag(body, i, out long time);
                if (end > 0)
                {
                    tags.Add(new Tag(i, end, Clamp(time)));
                    i = end - 1;
                }
            }
        }
        return tags;
    }

    /// <summary>shift moves every word tag when a repeat tag puts the same text at a later time</summary>
    private Draft BuildDraft(string body, List<Tag> tags, long lineStart, long shift)
    {
        if (tags.Count == 0)
        {
            return new Draft(lineStart, Trim(body), new List<Word>(), -1, _lineNo);
        }

        List<Word> words = new List<Word>();
        long endMarker = -1;
        string lead = JavaString.Substring(body, 0, tags[0].Start);
        if (!IsBlank(lead))
        {
            // first word without a tag of its own starts with the line; the space after "[line]" is not part of it
            words.Add(new Word(TrimStart(lead), lineStart));
        }
        for (int k = 0; k < tags.Count; k++)
        {
            Tag tag = tags[k];
            string text = JavaString.Substring(body, tag.End, k + 1 < tags.Count ? tags[k + 1].Start : body.Length);
            long time = Math.Max(0L, Math.Min(MaxTimeMs, tag.TimeMs + shift));
            if (text.Length == 0)
            {
                // no word follows: closes the previous word (a gap marker, or the line end when last)
                Word previous = words.Count == 0 ? null : words[words.Count - 1];
                if (previous != null && previous.EndMs < 0)
                {
                    previous.EndMs = time;
                }
                endMarker = Math.Max(endMarker, time);
            }
            else
            {
                // a space-only word is a real timed word too (real files time the spaces between CJK words)
                words.Add(new Word(text, time));
            }
        }

        StringBuilder line = new StringBuilder();
        foreach (Word word in words)
        {
            line.Append(word.Text);
        }
        return new Draft(lineStart, TrimEnd(line.ToString()), words, endMarker, _lineNo);
    }

    private void AddUntimed(string line)
    {
        Draft draft = new Draft(0, line, new List<Word>(), -1, _lineNo);
        if (_lastGroup == null)
        {
            _preamble.Add(draft);
            return;
        }
        foreach (Draft parent in _lastGroup)
        {
            parent.Translations.Add(line);
        }
        _untimedAttached++;
    }

    // ---------------------------------------------------------------- building the lyric

    private Lyric Build(string path)
    {
        List<LyricLine> lines = new List<LyricLine>();
        SyncType syncType = SyncType.NoSync;

        if (_preamble.Count > 0 && _timed.Count > 0)
        {
            Warn(_preamble[0].LineNo, _preamble.Count + " untimed line(s) before the first timed line were kept at 00:00.000");
        }
        foreach (Draft draft in _preamble)
        {
            lines.Add(NewLine(draft.Text, 0, 0, new WordTimeStamp[0], draft.Translations));
        }
        if (_untimedAttached > 0)
        {
            Warn(0, _untimedAttached + " untimed line(s) after timed lines were attached to the line before them as translations");
        }

        if (_timed.Count > 0)
        {
            syncType = SyncType.LineSync;
            if (_hadRepeatTags)
            {
                // List.Sort is not stable but OrderBy is, so equal start times keep file order
                _timed = _timed.OrderBy(draft => draft.StartMs).ToList();
            }
            List<Draft> kept = AbsorbTranslations();
            long[] nextStart = NextDifferentStarts(kept);
            for (int i = 0; i < kept.Count; i++)
            {
                LyricLine line = ToLine(kept[i], nextStart[i]);
                if (line.IsWordSynced)
                {
                    syncType = SyncType.WordSync;
                }
                lines.Add(line);
            }
        }

        Lyric lyric = new Lyric(syncType, lines, path);
        foreach (KeyValuePair<string, string> header in _metadata)
        {
            lyric.Metadata[header.Key] = header.Value;
        }
        lyric.OffsetMs = _offsetMs;
        foreach (string warning in _warnings)
        {
            lyric.AddWarning(warning);
        }
        return lyric;
    }

    /// <summary>the old converter and the standard format both put a translation on the next line with the same time</summary>
    private List<Draft> AbsorbTranslations()
    {
        List<Draft> kept = new List<Draft>();
        foreach (Draft draft in _timed)
        {
            Draft parent = kept.Count == 0 ? null : kept[kept.Count - 1];
            if (parent != null && parent.StartMs == draft.StartMs)
            {
                if (draft.Text.Length == 0)
                {
                    parent.Translations.AddRange(draft.Translations);
                    continue;
                }
                // a parent with no text is an instrumental marker, not something to translate
                if (parent.Text.Length != 0 && draft.Words.Count < 2)
                {
                    parent.Translations.Add(draft.Translation());
                    parent.Translations.AddRange(draft.Translations);
                    continue;
                }
            }
            kept.Add(draft);
        }
        return kept;
    }

    /// <summary>start of the next line whose start differs, per line (-1 when there is none); one backward pass keeps it linear</summary>
    private static long[] NextDifferentStarts(List<Draft> drafts)
    {
        long[] next = new long[drafts.Count];
        for (int i = drafts.Count - 1; i >= 0; i--)
        {
            if (i == drafts.Count - 1)
            {
                next[i] = -1;
            }
            else
            {
                long following = drafts[i + 1].StartMs;
                next[i] = following != drafts[i].StartMs ? following : next[i + 1];
            }
        }
        return next;
    }

    private LyricLine ToLine(Draft draft, long nextStartMs)
    {
        if (draft.Words.Count == 0)
        {
            long end = draft.EndMarkerMs >= 0 ? draft.EndMarkerMs : nextStartMs;
            return NewLine(draft.Text, draft.StartMs, Math.Max(draft.StartMs, end), new WordTimeStamp[0],
                draft.Translations);
        }

        WordTimeStamp[] stamps = new WordTimeStamp[draft.Words.Count];
        long lineEnd = draft.StartMs;
        bool backwards = false;
        for (int k = 0; k < stamps.Length; k++)
        {
            Word word = draft.Words[k];
            long end = word.EndMs;
            if (end < 0)
            {
                // a word is over when the next one starts; the last one when the next line does
                end = k + 1 < stamps.Length ? draft.Words[k + 1].StartMs : nextStartMs;
            }
            if (end < word.StartMs)
            {
                backwards |= end >= 0;
                end = word.StartMs;
            }
            stamps[k] = new WordTimeStamp(word.Text, word.StartMs, end);
            lineEnd = Math.Max(lineEnd, end);
        }
        if (backwards)
        {
            Warn(draft.LineNo, "word times run backwards; ends were raised to the word's start");
        }
        return NewLine(draft.Text, draft.StartMs, lineEnd, stamps, draft.Translations);
    }

    private static LyricLine NewLine(string text, long startMs, long endMs, WordTimeStamp[] words,
        List<string> translations)
    {
        LyricLine line = new LyricLine(text, startMs, endMs);
        line.WordTimeStamps = words;
        foreach (string translation in translations)
        {
            line.AddTranslation(translation);
        }
        return line;
    }

    private long Clamp(long rawMs)
    {
        if (rawMs <= MaxTimeMs)
        {
            return rawMs;
        }
        Warn(_lineNo, "a time tag is beyond 100 hours and was clamped");
        return MaxTimeMs;
    }

    private void Warn(int line, string message)
    {
        if (_warningCount++ < MaxWarnings)
        {
            _warnings.Add(line > 0 ? "line " + line.ToString(CultureInfo.InvariantCulture) + ": " + message : message);
        }
        else if (_warningCount == MaxWarnings + 1)
        {
            _warnings.Add("further warnings suppressed");
        }
    }

    // ---------------------------------------------------------------- scanning helpers

    /// <summary>
    /// reads a time tag opening at s[from] ('[' or '&lt;'): digits ':' digits, an optional
    /// '.' ',' or ':' fraction, then the matching closer. Returns the index after the
    /// closer and puts the time in <paramref name="time"/> (saturated, not yet clamped), or returns -1
    /// when it is not a time tag. Only digits and colons qualify, so "&lt;3",
    /// "&lt;(^o^)&gt;" and "a &lt; b" stay lyric text.
    /// </summary>
    private static int ScanTag(string s, int from, out long time)
    {
        time = 0;
        int n = s.Length;
        char closer = s[from] == '[' ? ']' : '>';

        int p = from + 1;
        int minutesFrom = p;
        while (p < n && IsDigit(s[p]))
        {
            p++;
        }
        int minutesTo = p;
        if (minutesTo == minutesFrom || p >= n || s[p] != ':')
        {
            return -1;
        }

        int secondsFrom = ++p;
        while (p < n && IsDigit(s[p]))
        {
            p++;
        }
        int secondsTo = p;
        if (secondsTo == secondsFrom)
        {
            return -1;
        }

        int fractionFrom = p;
        int fractionTo = p;
        if (p < n && (s[p] == '.' || s[p] == ',' || s[p] == ':'))
        {
            fractionFrom = ++p;
            while (p < n && IsDigit(s[p]))
            {
                p++;
            }
            fractionTo = p;
        }
        if (p >= n || s[p] != closer)
        {
            return -1;
        }

        time = Number(s, minutesFrom, minutesTo) * 60_000 + Number(s, secondsFrom, secondsTo) * 1000
            + Fraction(s, fractionFrom, fractionTo);
        return p + 1;
    }

    /// <summary>decimal digits as a number, saturating at 10^9 so absurd input cannot overflow</summary>
    private static long Number(string s, int from, int to)
    {
        long value = 0;
        for (int i = from; i < to; i++)
        {
            value = Math.Min(value * 10 + (s[i] - '0'), 1_000_000_000L);
        }
        return value;
    }

    /// <summary>".5" is 500 ms, ".05" is 50 ms, ".053" is 53 ms; digits after the third are dropped</summary>
    private static long Fraction(string s, int from, int to)
    {
        long value = 0;
        for (int i = 0; i < 3; i++)
        {
            value = value * 10 + (from + i < to ? s[from + i] - '0' : 0);
        }
        return value;
    }

    /// <summary>splits on CRLF, LF or a lone CR, after dropping every BOM (also one glued mid-text by a concatenation)</summary>
    private static List<string> SplitLines(string text)
    {
        if (text.IndexOf('\uFEFF') >= 0)
        {
            text = text.Replace("\uFEFF", "");
        }
        List<string> lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n' || c == '\r')
            {
                lines.Add(JavaString.Substring(text, start, i));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            lines.Add(text.Substring(start));
        }
        return lines;
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    private static bool IsAsciiLetter(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    // wider than String#trim: also no-break and ideographic spaces and the zero-width space.
    // Java's Character.isWhitespace(c) || Character.isSpaceChar(c): char.IsWhiteSpace is not the same set (it also
    // takes U+0085 and misses U+001C..U+001F), so the sets are spelled out.
    private static bool IsSpace(char c)
    {
        if (c == '\t' || c == '\n' || c == '\u000B' || c == '\f' || c == '\r' || (c >= '\u001C' && c <= '\u001F'))
        {
            return true;
        }
        switch (char.GetUnicodeCategory(c))
        {
            case UnicodeCategory.SpaceSeparator:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
                return true;
            default:
                return c == '\u200B';
        }
    }

    private static bool IsBlank(string s)
    {
        return SkipSpaces(s, 0) == s.Length;
    }

    private static int SkipSpaces(string s, int from)
    {
        int i = from;
        while (i < s.Length && IsSpace(s[i]))
        {
            i++;
        }
        return i;
    }

    private static string Trim(string s)
    {
        return TrimEnd(TrimStart(s));
    }

    private static string TrimStart(string s)
    {
        return s.Substring(SkipSpaces(s, 0));
    }

    private static string TrimEnd(string s)
    {
        int end = s.Length;
        while (end > 0 && IsSpace(s[end - 1]))
        {
            end--;
        }
        return JavaString.Substring(s, 0, end);
    }

    private static string Shorten(string s)
    {
        return s.Length > 40 ? JavaString.Substring(s, 0, 40) + "..." : s;
    }

    // ---------------------------------------------------------------- intermediate holders

    private sealed class Tag
    {
        public readonly int Start;
        public readonly int End; // index just past the closing bracket
        public readonly long TimeMs;

        public Tag(int start, int end, long timeMs)
        {
            Start = start;
            End = end;
            TimeMs = timeMs;
        }
    }

    private sealed class Word
    {
        public string Text;
        public readonly long StartMs;
        public long EndMs = -1; // -1 until a tag closes the word

        public Word(string text, long startMs)
        {
            Text = text;
            StartMs = startMs;
        }
    }

    /// <summary>one parsed line before translations are absorbed and end times resolved</summary>
    private sealed class Draft
    {
        public readonly long StartMs;
        public readonly string Text; // trailing whitespace trimmed
        public readonly List<Word> Words; // empty when line synced
        public readonly long EndMarkerMs; // explicit end of a line without words, -1 when absent
        public readonly int LineNo;
        public readonly List<string> Translations = new();

        public Draft(long startMs, string text, List<Word> words, long endMarkerMs, int lineNo)
        {
            StartMs = startMs;
            Text = text;
            Words = words;
            EndMarkerMs = endMarkerMs;
            LineNo = lineNo;
        }

        /// <summary>a single timed word is kept exactly as written so a formatter round trip returns the same string</summary>
        public string Translation()
        {
            return Words.Count == 1 ? Words[0].Text : Text;
        }
    }
}
