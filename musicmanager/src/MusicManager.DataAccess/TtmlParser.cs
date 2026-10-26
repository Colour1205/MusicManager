using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.DataAccess;

/// <summary>
/// very tolerant parser for Apple-style (and generic) TTML lyric files.
///
/// The library's .ttml files come from a mix of tools and some are damaged (invalid UTF-8,
/// bare ampersands, time-tag fragments leaked into attributes), so parsing falls back step by
/// step: strict DOM -> sanitized DOM -> a hand-written lenient scanner. Whatever route is taken
/// the result is the same small element tree, so every later rule (translations, background
/// vocals, timing repair) exists exactly once. Each repair is recorded as a warning on the
/// returned Lyric; an exception is only thrown when no lyric line can be recovered at all.
/// </summary>
public static class TtmlParser
{
    private const long NoTime = -1;
    private const long MaxTimeMs = 100L * 3_600_000L; // anything longer is garbage, not a song
    private const long DefaultTickRate = 10_000_000L;
    private const int MaxDepth = 64; // deeper nesting is flattened so recursion stays bounded
    private const char Replacement = '\uFFFD';

    private static readonly BigInteger Thousand = new BigInteger(1000);
    private static readonly BigInteger MinuteMs = new BigInteger(60_000);
    private static readonly BigInteger HourMs = new BigInteger(3_600_000);

    // The Java patterns use \d, which only ever means an ASCII digit there; .NET's \d also matches other scripts' digits,
    // so every digit class is written [0-9]. matches() (the whole string) becomes \A ... \z.

    // "12.5s" "1500ms" "1.5m" "1h" "123456t"
    private static readonly Regex OffsetTime = new Regex(
        @"\A([0-9]+)(?:\.([0-9]+))?(ms|h|m|s|t)\z", RegexOptions.CultureInvariant);
    // hh:mm:ss:ff (the frame count is ignored)
    private static readonly Regex ClockFrames = new Regex(
        @"\A([0-9]+):([0-9]{1,2}):([0-9]{1,2}):([0-9]+)(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant);
    // hh:mm:ss(.fff)
    private static readonly Regex ClockHms = new Regex(
        @"\A([0-9]+):([0-9]{1,2}):([0-9]{1,2})(?:\.([0-9]+))?\z", RegexOptions.CultureInvariant);
    // mm:ss(.fff); minutes may exceed 59
    private static readonly Regex ClockMs = new Regex(
        @"\A([0-9]+):([0-9]{1,2})(?:\.([0-9]+))?\z", RegexOptions.CultureInvariant);
    // bare seconds: "ss.SSS"
    private static readonly Regex Seconds = new Regex(
        @"\A([0-9]+)(?:\.([0-9]+))?\z", RegexOptions.CultureInvariant);

    // a fragment of an old converter's time tag left at the start of a word, e.g. "0:09.118]All "
    private static readonly Regex LeakedTag = new Regex(@"\A[0-9:.\]\[]*\]", RegexOptions.CultureInvariant);

    private static readonly Regex Ampersand = new Regex(
        @"&(?:#([0-9]{1,8});|#[xX]([0-9a-fA-F]{1,8});|(?:amp|lt|gt|quot|apos);)?", RegexOptions.CultureInvariant);
    // a '<' that does not open a tag, comment, CDATA section or processing instruction
    private static readonly Regex StrayLt = new Regex(
        @"<(?!(?:[A-Za-z_][^<>]*>|/[A-Za-z_][^<>]*>|!--|!\[CDATA\[|\?))", RegexOptions.CultureInvariant);

    // String's constructor in Java replaces malformed input instead of throwing; so do these decoders
    private static readonly Encoding Utf8 = new UTF8Encoding(false, false);
    private static readonly Encoding Utf16Be = new UnicodeEncoding(true, false, false);
    private static readonly Encoding Utf16Le = new UnicodeEncoding(false, false, false);

    /// <exception cref="LyricParseException">only when nothing usable could be recovered</exception>
    public static Lyric Parse(byte[] data, string path)
    {
        if (data == null || data.Length == 0)
        {
            throw new LyricParseException("TTML file is empty");
        }

        var warnings = new List<string>();
        Elem root = ReadTree(Decode(data), warnings);

        var paragraphs = new List<Elem>();
        if (root.Name == "p")
        {
            paragraphs.Add(root);
        }
        else
        {
            FindParagraphs(root, paragraphs);
        }
        if (paragraphs.Count == 0)
        {
            throw new LyricParseException("no <p> lyric lines found, not a TTML lyric file");
        }

        Elem top = root.Name == "#document" ? FirstElement(root) : root;
        long tickRate = ParseTickRate(top?.Attr("tickRate"));

        var rawLines = new List<RawLine>();
        var byKey = new Dictionary<string, RawLine>();
        foreach (Elem p in paragraphs)
        {
            var line = new RawLine(rawLines.Count + 1, p.Attr("key"), p.Attr("begin"), p.Attr("end"));
            line.Dur = p.Attr("dur");
            Collect(p, line);
            rawLines.Add(line);
            if (line.Key != null)
            {
                byKey.TryAdd(line.Key, line);
            }
        }
        AttachHeadTexts(root, byKey);

        var drafts = new List<Draft>();
        foreach (RawLine raw in rawLines)
        {
            BuildDrafts(raw, tickRate, drafts, warnings);
        }
        if (drafts.Count == 0)
        {
            throw new LyricParseException("no usable lyric lines found in the TTML");
        }

        bool anyWords = false;
        bool anyTimed = false;
        foreach (Draft d in drafts)
        {
            anyWords |= d.Words.Count > 0;
            anyTimed |= d.Timed;
        }
        bool isTtml = top != null && top.Name == "tt";
        if (!isTtml && !anyTimed)
        {
            throw new LyricParseException("not a TTML lyric file (root element is not <tt> and no line is timed)");
        }

        ResolveMissingTimes(drafts, anyTimed, warnings);

        var lines = new List<LyricLine>(drafts.Count);
        foreach (Draft d in drafts)
        {
            var line = new LyricLine(d.Text, d.Start, d.End);
            line.WordTimeStamps = d.Words.ToArray();
            foreach (string translation in d.Translations)
            {
                line.AddTranslation(translation);
            }
            lines.Add(line);
        }

        SyncType syncType = anyWords ? SyncType.WordSync : anyTimed ? SyncType.LineSync : SyncType.NoSync;
        var lyric = new Lyric(syncType, lines, path);
        foreach (string warning in warnings)
        {
            lyric.AddWarning(warning);
        }
        return lyric;
    }

    // ------------------------------------------------------------------ decoding

    /// <summary>UTF-8 (BOM or not) or UTF-16 (BOM or BOM-less), with invalid bytes turned into U+FFFD</summary>
    private static string Decode(byte[] data)
    {
        Encoding encoding = Utf8;
        int offset = 0;
        int n = data.Length;
        int b0 = data[0];
        int b1 = n > 1 ? data[1] : -1;
        if (n >= 3 && b0 == 0xEF && b1 == 0xBB && data[2] == 0xBF)
        {
            offset = 3;
        }
        else if (b0 == 0xFE && b1 == 0xFF)
        {
            encoding = Utf16Be;
            offset = 2;
        }
        else if (b0 == 0xFF && b1 == 0xFE)
        {
            encoding = Utf16Le;
            offset = 2;
        }
        else if (b0 == 0 && b1 == '<')
        {
            encoding = Utf16Be;
        }
        else if (b0 == '<' && b1 == 0)
        {
            encoding = Utf16Le;
        }
        return StripIllegalXmlChars(encoding.GetString(data, offset, n - offset));
    }

    /// <summary>drops NUL and the other control characters XML 1.0 forbids, plus lone surrogates</summary>
    private static string StripIllegalXmlChars(string s)
    {
        int n = s.Length;
        StringBuilder sb = null;
        for (int i = 0; i < n; i++)
        {
            char c = s[i];
            bool keep;
            bool pair = false;
            if (char.IsHighSurrogate(c))
            {
                pair = i + 1 < n && char.IsLowSurrogate(s[i + 1]);
                keep = pair;
            }
            else if (char.IsLowSurrogate(c))
            {
                keep = false;
            }
            else
            {
                keep = c == 0x9 || c == 0xA || c == 0xD || (c >= 0x20 && c <= 0xFFFD);
            }
            if (!keep && sb == null)
            {
                sb = new StringBuilder(n).Append(s, 0, i);
            }
            if (keep && sb != null)
            {
                sb.Append(c);
                if (pair)
                {
                    sb.Append(s[i + 1]);
                }
            }
            if (pair)
            {
                i++;
            }
        }
        return sb == null ? s : sb.ToString();
    }

    // ------------------------------------------------------------------ building the element tree

    private static Elem ReadTree(string text, List<string> warnings)
    {
        Elem root = DomTree(text, true);
        if (root != null)
        {
            return root;
        }

        string fixedText = Sanitize(text);
        root = DomTree(fixedText, true);
        if (root == null)
        {
            root = DomTree(fixedText, false); // undeclared namespace prefixes only fail in namespace-aware mode
        }
        if (root != null)
        {
            warnings.Add("TTML was not well-formed XML; repaired it before reading");
            return root;
        }

        warnings.Add("TTML was not well-formed XML; recovered what it could with a lenient scanner");
        return ScanLenient(text);
    }

    /// <summary>
    /// the strict parse: a DOCTYPE is refused outright (so no entity can ever be declared or expanded), nothing external
    /// is resolved, and any well-formedness error gives null so the next fallback takes over
    /// </summary>
    private static Elem DomTree(string xml, bool namespaceAware)
    {
        try
        {
            using XmlReader reader = CreateReader(xml, namespaceAware);
            return BuildTree(reader);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static XmlReader CreateReader(string xml, bool namespaceAware)
    {
        var text = new StringReader(xml);
        if (namespaceAware)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                CheckCharacters = true,
                IgnoreWhitespace = false,
            };
            return XmlReader.Create(text, settings);
        }
        return new XmlTextReader(text)
        {
            Namespaces = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Normalization = true,
            WhitespaceHandling = WhitespaceHandling.All,
        };
    }

    /// <summary>
    /// the document element as an Elem tree. Built while reading (no recursion), so a file nested 20000 deep cannot
    /// overflow the stack: an element below MaxDepth is flattened into its parent as plain text, a br becomes a space.
    /// </summary>
    private static Elem BuildTree(XmlReader reader)
    {
        Elem root = null;
        var open = new List<Elem>(); // the kept elements that are currently open; the last one receives the content
        int hidden = 0; // > 0 while inside a flattened subtree or a br: the number of its open elements
        bool dropping = false; // the hidden subtree is a br, whose content is dropped (a flattened one keeps its text)
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    bool empty = reader.IsEmptyElement;
                    if (hidden > 0)
                    {
                        if (!empty)
                        {
                            hidden++;
                        }
                        break;
                    }
                    string name = ElementName(reader.Name);
                    if (open.Count == 0)
                    {
                        if (root != null)
                        {
                            throw new XmlException("There are multiple root elements.");
                        }
                        root = new Elem(name, null);
                        ReadAttributes(reader, root);
                        if (!empty)
                        {
                            open.Add(root);
                        }
                        break;
                    }
                    Elem parent = open[open.Count - 1];
                    if (name == "br")
                    {
                        parent.AddText(" ");
                        if (!empty)
                        {
                            hidden = 1;
                            dropping = true;
                        }
                    }
                    else if (open.Count - 1 >= MaxDepth)
                    {
                        if (!empty)
                        {
                            hidden = 1;
                            dropping = false;
                        }
                    }
                    else
                    {
                        var child = new Elem(name, parent);
                        ReadAttributes(reader, child);
                        parent.AddChild(child);
                        if (!empty)
                        {
                            open.Add(child);
                        }
                    }
                    break;
                }
                case XmlNodeType.EndElement:
                    if (hidden > 0)
                    {
                        hidden--;
                        if (hidden == 0)
                        {
                            dropping = false;
                        }
                    }
                    else
                    {
                        open.RemoveAt(open.Count - 1);
                    }
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    if (open.Count > 0 && !dropping)
                    {
                        open[open.Count - 1].AddText(reader.Value);
                    }
                    break;
                default:
                    break; // declaration, comments and processing instructions carry no lyrics
            }
        }
        return root;
    }

    private static void ReadAttributes(XmlReader reader, Elem target)
    {
        if (!reader.HasAttributes)
        {
            return;
        }
        var found = new List<KeyValuePair<string, string>>(reader.AttributeCount);
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                found.Add(new KeyValuePair<string, string>(reader.Name, reader.Value));
            }
            while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        // the Java DOM hands out an element's attributes sorted by their qualified name
        found.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        foreach (KeyValuePair<string, string> attribute in found)
        {
            if (!attribute.Key.StartsWith("xmlns", StringComparison.Ordinal))
            {
                target.SetAttr(LocalName(attribute.Key), attribute.Value);
            }
        }
    }

    /// <summary>repairs the two things that most often break real files: bare '&amp;' and a stray '&lt;'</summary>
    private static string Sanitize(string s)
    {
        s = StripDoctype(s);
        int first = s.IndexOf('<');
        int last = s.LastIndexOf('>');
        if (first >= 0 && last > first)
        {
            s = JavaString.Substring(s, first, last + 1); // text outside the root element is not allowed
        }
        s = Ampersand.Replace(s, m =>
        {
            string whole = m.Value;
            if (whole.Length == 1)
            {
                return "&amp;";
            }
            string number = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : null;
            if (number != null)
            {
                long codePoint = long.Parse(number, m.Groups[1].Success ? NumberStyles.None : NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture);
                if (!IsXmlChar(codePoint))
                {
                    return "&amp;" + whole.Substring(1);
                }
            }
            return whole;
        });
        return StrayLt.Replace(s, "&lt;");
    }

    /// <summary>removes any &lt;!DOCTYPE ...&gt; including its internal subset, so no declared entity can ever be expanded</summary>
    private static string StripDoctype(string s)
    {
        StringBuilder sb = null;
        int copied = 0;
        int from = 0;
        int at;
        while ((at = s.IndexOf("<!", from, StringComparison.Ordinal)) >= 0)
        {
            if (!RegionMatchesIgnoreCase(s, at + 2, "DOCTYPE"))
            {
                from = at + 2;
                continue;
            }
            int end = at + 9;
            while (end < s.Length && s[end] != '>')
            {
                if (s[end] == '[')
                {
                    int close = s.IndexOf(']', end);
                    end = close < 0 ? s.Length : close;
                }
                end++;
            }
            end = Math.Min(end + 1, s.Length);
            if (sb == null)
            {
                sb = new StringBuilder(s.Length);
            }
            sb.Append(s, copied, at - copied);
            copied = end;
            from = end;
        }
        if (sb == null)
        {
            return s;
        }
        return sb.Append(s, copied, s.Length - copied).ToString();
    }

    /// <summary>String.regionMatches(true, offset, other, 0, other.length()) for ASCII text</summary>
    private static bool RegionMatchesIgnoreCase(string s, int offset, string other)
    {
        if (offset + other.Length > s.Length)
        {
            return false;
        }
        return string.Compare(s, offset, other, 0, other.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool IsXmlChar(long cp)
    {
        return cp == 0x9 || cp == 0xA || cp == 0xD || (cp >= 0x20 && cp <= 0xD7FF)
                || (cp >= 0xE000 && cp <= 0xFFFD) || (cp >= 0x10000 && cp <= 0x10FFFF);
    }

    /// <summary>
    /// last resort: tokenizes the text without caring whether it is well-formed. Unknown or
    /// unmatched closing tags are ignored, unclosed elements are closed implicitly, a new &lt;p&gt;
    /// closes an open one, and a truncated tag at the end is dropped.
    /// </summary>
    private static Elem ScanLenient(string s)
    {
        var root = new Elem("#document", null);
        var stack = new List<Elem>();
        stack.Add(root);
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            int lt = s.IndexOf('<', i);
            if (lt < 0)
            {
                stack[stack.Count - 1].AddText(Unescape(s.Substring(i)));
                break;
            }
            if (lt > i)
            {
                stack[stack.Count - 1].AddText(Unescape(JavaString.Substring(s, i, lt)));
            }
            i = lt;

            if (StartsWithAt(s, "<!--", i))
            {
                int end = s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? n : end + 3;
            }
            else if (StartsWithAt(s, "<![CDATA[", i))
            {
                int end = s.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                stack[stack.Count - 1].AddText(JavaString.Substring(s, i + 9, end < 0 ? n : end));
                i = end < 0 ? n : end + 3;
            }
            else if (StartsWithAt(s, "<?", i) || StartsWithAt(s, "<!", i))
            {
                int end = s.IndexOf('>', i);
                i = end < 0 ? n : end + 1;
            }
            else if (StartsWithAt(s, "</", i))
            {
                int j = i + 2;
                while (j < n && IsNameChar(s[j]))
                {
                    j++;
                }
                string name = ElementName(JavaString.Substring(s, i + 2, j));
                int end = s.IndexOf('>', j);
                i = end < 0 ? n : end + 1;
                CloseElement(stack, name);
            }
            else if (i + 1 < n && IsNameStart(s[i + 1]))
            {
                Tag tag = ReadStartTag(s, i);
                if (tag == null)
                {
                    break; // the file ends inside this tag
                }
                i = tag.End;
                OpenElement(stack, tag);
            }
            else
            {
                stack[stack.Count - 1].AddText("<"); // stray '<' is plain text
                i++;
            }
        }
        return root;
    }

    /// <summary>String.startsWith(prefix, offset)</summary>
    private static bool StartsWithAt(string s, string prefix, int offset)
    {
        return s.AsSpan(offset).StartsWith(prefix.AsSpan(), StringComparison.Ordinal);
    }

    private static void OpenElement(List<Elem> stack, Tag tag)
    {
        string name = ElementName(tag.Name);
        Elem parent = stack[stack.Count - 1];
        if (name == "br")
        {
            parent.AddText(" ");
            return;
        }
        if (name == "p")
        {
            CloseElement(stack, "p"); // paragraphs never nest, so a missing </p> is implied
            parent = stack[stack.Count - 1];
        }
        if (stack.Count > MaxDepth)
        {
            return;
        }
        var element = new Elem(name, parent);
        foreach (KeyValuePair<string, string> attribute in InJavaHashOrder(tag.Attrs))
        {
            element.SetAttr(LocalName(attribute.Key), attribute.Value);
        }
        parent.AddChild(element);
        if (!tag.SelfClosing)
        {
            stack.Add(element);
        }
    }

    /// <summary>
    /// The Java scanner kept a tag's attributes in a HashMap, so when two of them share a local name ("begin" and
    /// "ttm:begin") the one iterated first wins. This returns them in the order java.util.HashMap iterates: by bucket
    /// (String.hashCode spread with h ^ h>>>16, table size 16 doubling when more than 3/4 full), insertion order inside
    /// a bucket.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> InJavaHashOrder(OrderedDictionary<string, string> attrs)
    {
        if (attrs.Count < 2)
        {
            return attrs;
        }
        int capacity = 16;
        while (attrs.Count > capacity / 4 * 3)
        {
            capacity <<= 1;
        }
        // OrderBy is stable, so the insertion order is kept inside a bucket
        return attrs.OrderBy(attribute => BucketOf(attribute.Key, capacity)).ToList();
    }

    /// <summary>the HashMap bucket of a key: String.hashCode(), spread, masked to the table size</summary>
    private static int BucketOf(string key, int capacity)
    {
        int hash = 0;
        foreach (char c in key)
        {
            hash = unchecked(31 * hash + c);
        }
        hash ^= (int)((uint)hash >> 16);
        return hash & (capacity - 1);
    }

    private static void CloseElement(List<Elem> stack, string name)
    {
        for (int k = stack.Count - 1; k >= 1; k--)
        {
            if (stack[k].Name == name)
            {
                stack.RemoveRange(k, stack.Count - k);
                return;
            }
        }
    }

    private sealed class Tag
    {
        public string Name;
        public readonly OrderedDictionary<string, string> Attrs = new OrderedDictionary<string, string>();
        public bool SelfClosing;
        public int End;
    }

    /// <summary>reads "&lt;name attr=value ...&gt;" starting at the '&lt;'; null when the text ends inside the tag</summary>
    private static Tag ReadStartTag(string s, int start)
    {
        int n = s.Length;
        int j = start + 1;
        while (j < n && IsNameChar(s[j]))
        {
            j++;
        }
        var tag = new Tag();
        tag.Name = JavaString.Substring(s, start + 1, j);

        while (true)
        {
            while (j < n && IsWhitespace(s[j]))
            {
                j++;
            }
            if (j >= n)
            {
                return null;
            }
            char c = s[j];
            if (c == '>')
            {
                tag.End = j + 1;
                return tag;
            }
            if (c == '<')
            {
                tag.End = j; // malformed: the next tag starts before this one was closed
                return tag;
            }
            if (c == '/')
            {
                if (j + 1 < n && s[j + 1] == '>')
                {
                    tag.SelfClosing = true;
                    tag.End = j + 2;
                    return tag;
                }
                j++;
                continue;
            }

            int nameStart = j;
            while (j < n && !IsWhitespace(s[j]) && "=<>/".IndexOf(s[j]) < 0)
            {
                j++;
            }
            if (j == nameStart)
            {
                j++;
                continue;
            }
            string attrName = JavaString.Substring(s, nameStart, j);
            while (j < n && IsWhitespace(s[j]))
            {
                j++;
            }
            string value = "";
            if (j < n && s[j] == '=')
            {
                j++;
                while (j < n && IsWhitespace(s[j]))
                {
                    j++;
                }
                if (j < n && (s[j] == '"' || s[j] == '\''))
                {
                    char quote = s[j];
                    int close = s.IndexOf(quote, j + 1);
                    int stop = close < 0 ? n : close;
                    // attribute values cannot contain '<', so one before the closing quote means the
                    // quote was never closed (and without any closing quote, '>' ends the tag)
                    for (int k = j + 1; k < stop; k++)
                    {
                        char ch = s[k];
                        if (ch == '<' || (close < 0 && ch == '>'))
                        {
                            stop = k;
                            close = -1;
                            break;
                        }
                    }
                    value = JavaString.Substring(s, j + 1, stop);
                    j = close >= 0 ? close + 1 : stop;
                }
                else
                {
                    int valueStart = j;
                    while (j < n && !IsWhitespace(s[j]) && s[j] != '>' && s[j] != '<')
                    {
                        j++;
                    }
                    value = JavaString.Substring(s, valueStart, j);
                }
            }
            tag.Attrs.TryAdd(attrName, Unescape(value));
        }
    }

    private static bool IsNameStart(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
    }

    private static bool IsNameChar(char c)
    {
        return IsNameStart(c) || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == ':';
    }

    /// <summary>resolves the five predefined entities and numeric references; anything else stays as written</summary>
    private static string Unescape(string s)
    {
        if (s.IndexOf('&') < 0)
        {
            return s;
        }
        var sb = new StringBuilder(s.Length);
        int n = s.Length;
        for (int i = 0; i < n; i++)
        {
            char c = s[i];
            if (c == '&')
            {
                int semicolon = -1;
                for (int k = i + 1; k < n && k <= i + 10; k++) // longest reference we know is "&#x10FFFF;"
                {
                    if (s[k] == ';')
                    {
                        semicolon = k;
                        break;
                    }
                }
                if (semicolon > i + 1)
                {
                    string replacement = EntityValue(JavaString.Substring(s, i + 1, semicolon));
                    if (replacement != null)
                    {
                        sb.Append(replacement);
                        i = semicolon;
                        continue;
                    }
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string EntityValue(string entity)
    {
        switch (entity)
        {
            case "amp":
                return "&";
            case "lt":
                return "<";
            case "gt":
                return ">";
            case "quot":
                return "\"";
            case "apos":
                return "'";
            default:
                break;
        }
        if (entity.Length > 1 && entity[0] == '#')
        {
            bool hex = entity[1] == 'x' || entity[1] == 'X';
            // not a character reference when it does not parse
            if (TryParseJavaLong(entity.Substring(hex ? 2 : 1), hex ? 16 : 10, out long codePoint) && IsXmlChar(codePoint))
            {
                return char.ConvertFromUtf32((int)codePoint);
            }
        }
        return null;
    }

    /// <summary>
    /// Long.parseLong(s, radix): an optional sign, then digits (any script's decimal digits count, as in Java), no
    /// whitespace, no overflow; false where Java throws NumberFormatException
    /// </summary>
    private static bool TryParseJavaLong(string s, int radix, out long value)
    {
        value = 0;
        int length = s.Length;
        if (length == 0)
        {
            return false;
        }
        int i = 0;
        bool negative = false;
        long limit = -long.MaxValue;
        char first = s[0];
        if (first < '0')
        {
            if (first == '-')
            {
                negative = true;
                limit = long.MinValue;
            }
            else if (first != '+')
            {
                return false;
            }
            if (length == 1)
            {
                return false;
            }
            i++;
        }
        long multMin = limit / radix;
        long result = 0;
        while (i < length)
        {
            int digit = DigitOf(s[i++], radix);
            if (digit < 0 || result < multMin)
            {
                return false;
            }
            result *= radix;
            if (result < limit + digit)
            {
                return false;
            }
            result -= digit;
        }
        value = negative ? result : -result;
        return true;
    }

    /// <summary>Character.digit(c, radix): -1 when c is no digit in that radix</summary>
    private static int DigitOf(char c, int radix)
    {
        int digit;
        if (c >= '0' && c <= '9')
        {
            digit = c - '0';
        }
        else if (c >= 'a' && c <= 'z')
        {
            digit = c - 'a' + 10;
        }
        else if (c >= 'A' && c <= 'Z')
        {
            digit = c - 'A' + 10;
        }
        else
        {
            digit = CharUnicodeInfo.GetDecimalDigitValue(c);
        }
        return digit >= 0 && digit < radix ? digit : -1;
    }

    private static string LocalName(string qualified)
    {
        int colon = qualified.LastIndexOf(':');
        return colon < 0 ? qualified : qualified.Substring(colon + 1);
    }

    private static string ElementName(string qualified)
    {
        return LocalName(qualified).ToLowerInvariant();
    }

    // Java's Character.isWhitespace / isSpaceChar / String.strip() / isBlank() differ from .NET's char.IsWhiteSpace and
    // string.Trim() (no-break spaces, U+0085, the C0 separators U+001C-U+001F), and the word-gap rules below depend on
    // the exact set, so the Java definitions live here.

    /// <summary>Character.isSpaceChar: the Unicode space, line and paragraph separators (no-break spaces included)</summary>
    private static bool IsSpaceChar(char c)
    {
        UnicodeCategory category = char.GetUnicodeCategory(c);
        return category == UnicodeCategory.SpaceSeparator || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator;
    }

    /// <summary>Character.isWhitespace: the separators except the no-break spaces, plus \t \n VT FF \r and U+001C-U+001F</summary>
    private static bool IsWhitespace(char c)
    {
        switch (c)
        {
            case '\t':
            case '\n':
            case '\u000B':
            case '\f':
            case '\r':
            case '\u001C':
            case '\u001D':
            case '\u001E':
            case '\u001F':
                return true;
            case '\u00A0':
            case '\u2007':
            case '\u202F':
                return false;
            default:
                return IsSpaceChar(c);
        }
    }

    private static string StripTrailing(string s)
    {
        int end = s.Length;
        while (end > 0 && IsWhitespace(s[end - 1]))
        {
            end--;
        }
        return end == s.Length ? s : s.Substring(0, end);
    }

    private static string Strip(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && IsWhitespace(s[start]))
        {
            start++;
        }
        while (end > start && IsWhitespace(s[end - 1]))
        {
            end--;
        }
        return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
    }

    private static bool IsBlank(string s)
    {
        foreach (char c in s)
        {
            if (!IsWhitespace(c))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>minimal element: local name, attributes by local name, and text/child content in order</summary>
    private sealed class Elem
    {
        public readonly string Name;
        public readonly Dictionary<string, string> Attrs = new Dictionary<string, string>(4);
        private readonly List<object> _content = new List<object>(); // string or Elem
        private StringBuilder _pendingText; // not yet in content, so thousands of tiny pieces stay linear
        public bool Preserve; // xml:space="preserve" is in effect

        public Elem(string name, Elem parent)
        {
            Name = name;
            Preserve = parent != null && parent.Preserve;
        }

        public void SetAttr(string name, string value)
        {
            Attrs.TryAdd(name, value);
            if (name == "space")
            {
                Preserve = value == "preserve";
            }
        }

        public string Attr(string name)
        {
            return Attrs.TryGetValue(name, out string value) ? value : null;
        }

        public void AddText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            if (_pendingText == null)
            {
                _pendingText = new StringBuilder();
            }
            _pendingText.Append(text);
        }

        public void AddChild(Elem child)
        {
            Content();
            _content.Add(child);
        }

        /// <summary>the children in order: adjacent text pieces are already merged into one string</summary>
        public List<object> Content()
        {
            if (_pendingText != null)
            {
                _content.Add(_pendingText.ToString());
                _pendingText = null;
            }
            return _content;
        }

        public bool HasElements()
        {
            foreach (object o in Content())
            {
                if (o is Elem)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>all descendant text, with pretty-printing line breaks folded into single spaces</summary>
        public string TextContent()
        {
            var sb = new StringBuilder();
            AppendText(sb);
            return CollapseBreaks(sb.ToString());
        }

        private void AppendText(StringBuilder sb)
        {
            foreach (object o in Content())
            {
                if (o is string text)
                {
                    sb.Append(text);
                }
                else
                {
                    ((Elem)o).AppendText(sb);
                }
            }
        }
    }

    private static Elem FirstElement(Elem parent)
    {
        foreach (object o in parent.Content())
        {
            if (o is Elem element)
            {
                return element;
            }
        }
        return null;
    }

    /// <summary>every &lt;p&gt; in document order, across all &lt;div&gt;s, ignoring anything inside &lt;head&gt;</summary>
    private static void FindParagraphs(Elem parent, List<Elem> output)
    {
        foreach (object o in parent.Content())
        {
            if (o is not Elem child)
            {
                continue;
            }
            if (child.Name == "head")
            {
                continue;
            }
            if (child.Name == "p")
            {
                output.Add(child);
            }
            else
            {
                FindParagraphs(child, output);
            }
        }
    }

    // ------------------------------------------------------------------ reading a <p>

    /// <summary>one &lt;p&gt; (or one background-vocal span) before any timing is interpreted</summary>
    private sealed class RawLine
    {
        public readonly int Index;
        public readonly string Key; // itunes:key, used to match head-level translations
        public readonly string Begin;
        public readonly string End;
        public string Dur; // used as begin + dur when there is no end
        public readonly List<Item> Items = new List<Item>();
        public readonly List<string> Translations = new List<string>();
        public readonly List<string> Romans = new List<string>();
        public readonly List<RawLine> Backgrounds = new List<RawLine>();
        public Item LastWithText; // where a word separator gets appended
        public bool IsBackground; // begin/end were borrowed from the parent, so the line times come from its own words

        public RawLine(int index, string key, string begin, string end)
        {
            Index = index;
            Key = key;
            Begin = begin;
            End = end;
        }

        public string Label()
        {
            return Key ?? "#" + Index.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>a word span, or a stretch of loose text (no times of its own)</summary>
    private sealed class Item
    {
        public string Text;
        public readonly string Begin;
        public readonly string End;
        public readonly string Dur;
        public bool BadTime; // an attribute was present but unreadable

        /// <param name="text">the item's text</param>
        /// <param name="source">the span the text came from, or null for loose text that has no times</param>
        public Item(string text, Elem source)
        {
            Text = text;
            Begin = source?.Attr("begin");
            End = source?.Attr("end");
            Dur = source?.Attr("dur");
        }
    }

    private static void Collect(Elem container, RawLine target)
    {
        string pendingSeparator = null;
        foreach (object o in container.Content())
        {
            if (o is string text)
            {
                if (IsFormatting(text))
                {
                    // pretty-printing between spans is noise; a lone space (or any xml:space="preserve") is a word gap
                    if (container.Preserve || text == " ")
                    {
                        pendingSeparator = " ";
                    }
                }
                else if (text.Length == 1 && IsSpaceChar(text[0]))
                {
                    pendingSeparator = text; // e.g. an ideographic or no-break space between spans
                }
                else
                {
                    AddItem(target, pendingSeparator, new Item(CollapseBreaks(text), null));
                    pendingSeparator = null;
                }
                continue;
            }

            var element = (Elem)o;
            string role = element.Attr("role");
            if (role == "x-translation")
            {
                target.Translations.Add(element.TextContent());
            }
            else if (role == "x-roman" || role == "x-transliteration")
            {
                target.Romans.Add(element.TextContent());
            }
            else if (role == "x-bg")
            {
                // a background vocal borrows its parent's times where it has none of its own
                string begin = element.Attr("begin") ?? target.Begin;
                string end = element.Attr("end") ?? target.End;
                var background = new RawLine(target.Index, null, begin, end);
                background.IsBackground = true;
                background.Dur = element.Attr("dur") != null || element.Attr("begin") != null ? element.Attr("dur") : target.Dur;
                if (element.HasElements())
                {
                    Collect(element, background);
                }
                else
                {
                    background.Items.Add(new Item(element.TextContent(), element));
                }
                target.Backgrounds.Add(background);
            }
            else if (element.HasElements())
            {
                Collect(element, target);
            }
            else
            {
                AddItem(target, pendingSeparator, new Item(element.TextContent(), element));
                pendingSeparator = null;
            }
        }
    }

    private static void AddItem(RawLine target, string separator, Item item)
    {
        Item previous = target.LastWithText;
        if (separator != null && previous != null
                && !IsSpace(previous.Text[previous.Text.Length - 1])
                && (item.Text.Length == 0 || !IsSpace(item.Text[0])))
        {
            previous.Text += separator;
        }
        target.Items.Add(item);
        if (item.Text.Length > 0)
        {
            target.LastWithText = item;
        }
    }

    private static bool IsSpace(char c)
    {
        return IsWhitespace(c) || IsSpaceChar(c);
    }

    /// <summary>only space, tab, CR, LF: what indentation is made of</summary>
    private static bool IsFormatting(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }
        for (int i = 0; i < s.Length; i++)
        {
            if (!IsIndentChar(s[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsIndentChar(char c)
    {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    }

    /// <summary>a run of whitespace that contains a line break is indentation: it becomes one space</summary>
    private static string CollapseBreaks(string s)
    {
        if (s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0)
        {
            return s;
        }
        var sb = new StringBuilder(s.Length);
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            if (!IsIndentChar(s[i]))
            {
                sb.Append(s[i++]);
                continue;
            }
            int j = i;
            bool lineBreak = false;
            while (j < n && IsIndentChar(s[j]))
            {
                lineBreak |= s[j] == '\n' || s[j] == '\r';
                j++;
            }
            if (lineBreak)
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(s, i, j - i);
            }
            i = j;
        }
        return sb.ToString();
    }

    /// <summary>Apple's real files keep translations in head/metadata/iTunesMetadata, keyed by itunes:key</summary>
    private static void AttachHeadTexts(Elem root, Dictionary<string, RawLine> byKey)
    {
        if (byKey.Count == 0)
        {
            return;
        }
        var heads = new List<Elem>();
        FindHeads(root, heads);
        foreach (Elem head in heads)
        {
            WalkHead(head, 0, byKey);
        }
    }

    private static void FindHeads(Elem parent, List<Elem> output)
    {
        foreach (object o in parent.Content())
        {
            if (o is Elem child)
            {
                if (child.Name == "head")
                {
                    output.Add(child);
                }
                else if (child.Name != "body" && child.Name != "p")
                {
                    FindHeads(child, output);
                }
            }
        }
    }

    /// <param name="parent">the element whose children are searched</param>
    /// <param name="kind">0 outside, 1 inside a &lt;translation&gt;, 2 inside a &lt;transliteration&gt;</param>
    /// <param name="byKey">the paragraphs by their itunes:key</param>
    private static void WalkHead(Elem parent, int kind, Dictionary<string, RawLine> byKey)
    {
        foreach (object o in parent.Content())
        {
            if (o is not Elem child)
            {
                continue;
            }
            int childKind = kind;
            if (child.Name == "translation")
            {
                childKind = 1;
            }
            else if (child.Name == "transliteration")
            {
                childKind = 2;
            }
            string forKey = child.Attr("for");
            if (childKind != 0 && child.Name == "text" && forKey != null)
            {
                if (byKey.TryGetValue(forKey, out RawLine target))
                {
                    (childKind == 1 ? target.Translations : target.Romans).Add(child.TextContent());
                }
            }
            else
            {
                WalkHead(child, childKind, byKey);
            }
        }
    }

    // ------------------------------------------------------------------ interpreting times

    /// <summary>a lyric line before its times are final</summary>
    private sealed class Draft
    {
        public string Text;
        public long Start;
        public long End;
        public bool Timed; // the source gave it a real time
        public readonly List<WordTimeStamp> Words = new List<WordTimeStamp>();
        public readonly List<string> Translations = new List<string>();
    }

    /// <summary>the repairs made to one paragraph: each distinct one once, in the order it was made</summary>
    private sealed class RepairSet
    {
        private readonly List<string> _ordered = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>();

        public void Add(string repair)
        {
            if (_seen.Add(repair))
            {
                _ordered.Add(repair);
            }
        }

        public bool IsEmpty => _ordered.Count == 0;

        public string Join(string separator)
        {
            return string.Join(separator, _ordered);
        }
    }

    /// <summary>turns one &lt;p&gt; into its line plus one line per background vocal, recording a single warning if anything was repaired</summary>
    private static void BuildDrafts(RawLine raw, long tickRate, List<Draft> output, List<string> warnings)
    {
        var repairs = new RepairSet();
        Draft main = BuildDraft(raw, tickRate, repairs);
        var backgrounds = new List<Draft>();
        foreach (RawLine background in raw.Backgrounds)
        {
            Draft draft = BuildDraft(background, tickRate, repairs);
            if (draft != null)
            {
                backgrounds.Add(draft);
            }
        }

        var translations = new List<string>();
        foreach (string translation in raw.Translations)
        {
            AddTranslation(translations, translation, repairs);
        }
        foreach (string roman in raw.Romans)
        {
            AddTranslation(translations, roman, repairs);
        }

        // translations belong to the main vocal; if it has no text they go to the first background line
        Draft carrier = main ?? (backgrounds.Count == 0 ? null : backgrounds[0]);
        if (carrier != null)
        {
            carrier.Translations.AddRange(translations);
        }
        if (main != null)
        {
            output.Add(main);
        }
        output.AddRange(backgrounds);

        if (!repairs.IsEmpty)
        {
            warnings.Add("TTML line " + raw.Label() + ": " + repairs.Join("; "));
        }
    }

    private static void AddTranslation(List<string> output, string raw, RepairSet repairs)
    {
        string text = Strip(CollapseBreaks(raw));
        if (text.Length == 0 || text.IndexOf(Replacement) >= 0)
        {
            repairs.Add("dropped corrupted translation");
        }
        else
        {
            output.Add(text);
        }
    }

    private static Draft BuildDraft(RawLine raw, long tickRate, RepairSet repairs)
    {
        long lineBegin = ParseTime(raw.Begin, tickRate);
        long lineEnd = EndTime(raw.End, raw.Dur, lineBegin, tickRate);
        bool badBegin = raw.Begin != null && lineBegin == NoTime;
        bool badEnd = lineEnd == NoTime && (raw.End != null || (raw.Dur != null && lineBegin != NoTime));
        if (badBegin || badEnd)
        {
            repairs.Add("repaired invalid line time");
        }

        int n = raw.Items.Count;
        var begins = new long[n];
        var ends = new long[n];
        bool anyTime = false;
        bool anyBad = false;
        for (int i = 0; i < n; i++)
        {
            Item item = raw.Items[i];
            begins[i] = ParseTime(item.Begin, tickRate);
            ends[i] = EndTime(item.End, item.Dur, begins[i], tickRate);
            item.BadTime = (item.Begin != null && begins[i] == NoTime)
                    || ((item.End != null || (item.Dur != null && begins[i] != NoTime)) && ends[i] == NoTime);
            anyBad |= item.BadTime;
            anyTime |= begins[i] != NoTime || ends[i] != NoTime;
        }
        if (anyBad)
        {
            repairs.Add("repaired invalid word timing");
        }

        if (!anyTime)
        {
            return LineOnlyDraft(raw, lineBegin, lineEnd, repairs);
        }

        var words = new string[n];
        var wordBegins = new long[n];
        var wordEnds = new long[n];
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            Item item = raw.Items[i];
            string itemText = item.Text;
            if (itemText.IndexOf(Replacement) >= 0)
            {
                itemText = itemText.Replace(Replacement.ToString(), "");
                repairs.Add("removed corrupted characters");
            }
            if (item.BadTime)
            {
                Match leaked = LeakedTag.Match(itemText);
                if (leaked.Success)
                {
                    itemText = itemText.Substring(leaked.Index + leaked.Length);
                    repairs.Add("removed leaked time-tag text");
                }
            }
            if (itemText.Length == 0)
            {
                continue;
            }
            words[m] = itemText;
            wordBegins[m] = begins[i];
            wordEnds[m] = ends[i];
            m++;
        }
        if (m == 0)
        {
            return null;
        }

        FillMissingTimes(wordBegins, wordEnds, m, lineBegin, lineEnd, repairs);
        ClampTimes(wordBegins, wordEnds, m, repairs);

        var text = new StringBuilder();
        var draft = new Draft();
        for (int i = 0; i < m; i++)
        {
            text.Append(words[i]);
            draft.Words.Add(new WordTimeStamp(words[i], wordBegins[i], wordEnds[i]));
        }
        draft.Text = StripTrailing(text.ToString());
        if (IsBlank(draft.Text))
        {
            return null;
        }
        draft.Start = lineBegin != NoTime && !raw.IsBackground ? lineBegin : wordBegins[0];
        draft.End = lineEnd != NoTime && !raw.IsBackground ? lineEnd : wordEnds[m - 1];
        if (draft.End < draft.Start)
        {
            draft.End = draft.Start;
        }
        draft.Timed = true;
        return draft;
    }

    /// <summary>a &lt;p&gt; with no readable word times: a plain line-synced (or unsynced) line</summary>
    private static Draft LineOnlyDraft(RawLine raw, long lineBegin, long lineEnd, RepairSet repairs)
    {
        var joined = new StringBuilder();
        foreach (Item item in raw.Items)
        {
            joined.Append(item.Text);
        }
        string text = Strip(CollapseBreaks(joined.ToString()));
        if (text.IndexOf(Replacement) >= 0)
        {
            repairs.Add("dropped corrupted line");
            return null;
        }
        if (text.Length == 0)
        {
            return null;
        }
        var draft = new Draft();
        draft.Text = text;
        draft.Start = lineBegin;
        draft.End = lineEnd;
        draft.Timed = lineBegin != NoTime;
        return draft;
    }

    /// <summary>
    /// a word with no usable begin starts where the previous word ended (or where the line starts);
    /// a word with no usable end stops at the next valid time in the line (or the line end)
    /// </summary>
    private static void FillMissingTimes(long[] begins, long[] ends, int m, long lineBegin, long lineEnd,
            RepairSet repairs)
    {
        // nextValid[i]: the first readable time at or after word i, so each lookup is O(1)
        var nextValid = new long[m + 1];
        nextValid[m] = NoTime;
        for (int j = m - 1; j >= 0; j--)
        {
            nextValid[j] = begins[j] != NoTime ? begins[j] : ends[j] != NoTime ? ends[j] : nextValid[j + 1];
        }
        for (int i = 0; i < m; i++)
        {
            if (begins[i] == NoTime)
            {
                repairs.Add("repaired invalid word timing");
                if (i > 0)
                {
                    begins[i] = ends[i - 1];
                }
                else if (lineBegin != NoTime)
                {
                    begins[i] = lineBegin;
                }
                else
                {
                    begins[i] = nextValid[0] != NoTime ? nextValid[0] : lineEnd != NoTime ? lineEnd : 0;
                }
            }
            if (ends[i] == NoTime)
            {
                repairs.Add("repaired invalid word timing");
                long next = nextValid[i + 1];
                ends[i] = next != NoTime ? next : lineEnd != NoTime ? lineEnd : begins[i];
            }
        }
    }

    /// <summary>guarantees begin &lt;= end &lt;= next begin for every word, so downstream code never sees time run backwards</summary>
    private static void ClampTimes(long[] begins, long[] ends, int m, RepairSet repairs)
    {
        for (int i = 0; i < m; i++)
        {
            if (i > 0 && begins[i] < begins[i - 1])
            {
                begins[i] = begins[i - 1];
                repairs.Add("clamped out-of-order word times");
            }
            if (ends[i] < begins[i])
            {
                ends[i] = begins[i];
                repairs.Add("clamped out-of-order word times");
            }
            if (i > 0 && begins[i] < ends[i - 1])
            {
                ends[i - 1] = begins[i];
                repairs.Add("clamped out-of-order word times");
            }
        }
    }

    /// <summary>gives lines without a usable start the previous line's end, and lines without an end the next line's start</summary>
    private static void ResolveMissingTimes(List<Draft> drafts, bool anyTimed, List<string> warnings)
    {
        int inferred = 0;
        long previousEnd = 0;
        foreach (Draft d in drafts)
        {
            if (d.Start == NoTime)
            {
                d.Start = previousEnd;
                inferred++;
            }
            previousEnd = d.End != NoTime ? d.End : d.Start;
        }
        for (int i = 0; i < drafts.Count; i++)
        {
            Draft d = drafts[i];
            if (d.End == NoTime)
            {
                Draft next = i + 1 < drafts.Count ? drafts[i + 1] : null;
                d.End = next != null && next.Start != NoTime && next.Start >= d.Start ? next.Start : d.Start;
            }
            if (d.End < d.Start)
            {
                d.End = d.Start;
            }
        }
        if (anyTimed && inferred > 0)
        {
            warnings.Add(inferred.ToString(CultureInfo.InvariantCulture) + " TTML line(s) had no start time and follow the previous line");
        }
    }

    /// <summary>the end time: the end attribute, or begin + dur when only a duration was given</summary>
    private static long EndTime(string end, string dur, long begin, long tickRate)
    {
        if (end != null)
        {
            return ParseTime(end, tickRate);
        }
        long duration = ParseTime(dur, tickRate);
        return begin != NoTime && duration != NoTime ? begin + duration : NoTime;
    }

    private static long ParseTickRate(string raw)
    {
        if (raw == null)
        {
            return DefaultTickRate;
        }
        if (!TryParseJavaLong(JavaString.Trim(raw), 10, out long rate))
        {
            return DefaultTickRate;
        }
        return rate > 0 ? rate : DefaultTickRate;
    }

    /// <summary>
    /// parses a TTML time expression to milliseconds: mm:ss.SSS, hh:mm:ss.fff, hh:mm:ss:ff (frames
    /// ignored), ss.SSS, or an offset like 12.5s / 1500ms / 1.5m / 1h / 123456t
    /// </summary>
    /// <returns>the time, or NoTime (-1) when the text is not a valid, sane time</returns>
    public static long ParseTime(string raw, long tickRate)
    {
        if (raw == null)
        {
            return NoTime;
        }
        string s = JavaString.Trim(raw);
        if (s.Length == 0 || s.Length > 64)
        {
            return NoTime;
        }

        if (s.Length == 9 && s[2] == ':' && s[5] == '.') // Apple's mm:ss.SSS, by far the most common
        {
            int minutes = Digits(s, 0, 2);
            int seconds = Digits(s, 3, 2);
            int millis = Digits(s, 6, 3);
            if (minutes >= 0 && seconds >= 0 && millis >= 0)
            {
                return minutes * 60_000L + seconds * 1000L + millis;
            }
        }

        BigDec ms;
        Match m;
        if ((m = OffsetTime.Match(s)).Success)
        {
            BigDec value = BigDec.Parse(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null);
            switch (m.Groups[3].Value)
            {
                case "h":
                    ms = value.Multiply(HourMs);
                    break;
                case "m":
                    ms = value.Multiply(MinuteMs);
                    break;
                case "s":
                    ms = value.Multiply(Thousand);
                    break;
                case "ms":
                    ms = value;
                    break;
                default: // ticks
                    ms = value.Multiply(Thousand).DivideHalfUp(tickRate, 6);
                    break;
            }
        }
        else if ((m = ClockFrames.Match(s)).Success)
        {
            ms = Clock(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, null);
        }
        else if ((m = ClockHms.Match(s)).Success)
        {
            ms = Clock(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, GroupOrNull(m, 4));
        }
        else if ((m = ClockMs.Match(s)).Success)
        {
            ms = Clock("0", m.Groups[1].Value, m.Groups[2].Value, GroupOrNull(m, 3));
        }
        else if ((m = Seconds.Match(s)).Success)
        {
            ms = BigDec.Parse(m.Groups[1].Value, GroupOrNull(m, 2)).Multiply(Thousand);
        }
        else
        {
            return NoTime;
        }

        if (ms.CompareTo(MaxTimeMs) > 0)
        {
            return NoTime;
        }
        return ms.RoundHalfUpToLong();
    }

    private static string GroupOrNull(Match m, int group)
    {
        return m.Groups[group].Success ? m.Groups[group].Value : null;
    }

    /// <summary>the value of {@code count} ASCII digits starting at {@code from}, or -1 if any is not a digit</summary>
    private static int Digits(string s, int from, int count)
    {
        int value = 0;
        for (int i = from; i < from + count; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9')
            {
                return -1;
            }
            value = value * 10 + (c - '0');
        }
        return value;
    }

    private static BigDec Clock(string hours, string minutes, string seconds, string fraction)
    {
        BigDec ms = BigDec.Parse(hours, null).Multiply(HourMs)
                .Add(BigDec.Parse(minutes, null).Multiply(MinuteMs))
                .Add(BigDec.Parse(seconds, null).Multiply(Thousand));
        if (fraction != null)
        {
            ms = ms.Add(BigDec.Parse("0", fraction).Multiply(Thousand));
        }
        return ms;
    }

    /// <summary>
    /// the little of java.math.BigDecimal the time parser needs, so that rounding is exactly the Java rounding however
    /// many digits a damaged file has (System.Decimal would silently round long inputs). Never negative here.
    /// </summary>
    private readonly struct BigDec
    {
        private readonly BigInteger _unscaled;
        private readonly int _scale; // the value is _unscaled / 10^_scale

        private BigDec(BigInteger unscaled, int scale)
        {
            _unscaled = unscaled;
            _scale = scale;
        }

        /// <summary>new BigDecimal(integer) or new BigDecimal(integer + "." + fraction), digits only</summary>
        public static BigDec Parse(string integer, string fraction)
        {
            if (fraction == null)
            {
                return new BigDec(BigInteger.Parse(integer, NumberStyles.None, CultureInfo.InvariantCulture), 0);
            }
            return new BigDec(BigInteger.Parse(integer + fraction, NumberStyles.None, CultureInfo.InvariantCulture), fraction.Length);
        }

        public BigDec Multiply(BigInteger factor)
        {
            return new BigDec(_unscaled * factor, _scale);
        }

        public BigDec Add(BigDec other)
        {
            int scale = Math.Max(_scale, other._scale);
            BigInteger a = _unscaled * BigInteger.Pow(10, scale - _scale);
            BigInteger b = other._unscaled * BigInteger.Pow(10, scale - other._scale);
            return new BigDec(a + b, scale);
        }

        /// <summary>divide(BigDecimal.valueOf(divisor), scale, RoundingMode.HALF_UP)</summary>
        public BigDec DivideHalfUp(long divisor, int scale)
        {
            // (u / 10^s) / d at the given scale  ==  u * 10^scale / (d * 10^s), rounded half up
            BigInteger numerator = _unscaled * BigInteger.Pow(10, scale);
            BigInteger denominator = new BigInteger(divisor) * BigInteger.Pow(10, _scale);
            return new BigDec(DivideRoundHalfUp(numerator, denominator), scale);
        }

        public int CompareTo(long other)
        {
            return _unscaled.CompareTo(new BigInteger(other) * BigInteger.Pow(10, _scale));
        }

        /// <summary>setScale(0, RoundingMode.HALF_UP).longValueExact()</summary>
        public long RoundHalfUpToLong()
        {
            if (_scale == 0)
            {
                return (long)_unscaled;
            }
            return (long)DivideRoundHalfUp(_unscaled, BigInteger.Pow(10, _scale));
        }

        private static BigInteger DivideRoundHalfUp(BigInteger numerator, BigInteger denominator)
        {
            BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            return remainder * 2 >= denominator ? quotient + 1 : quotient;
        }
    }
}
