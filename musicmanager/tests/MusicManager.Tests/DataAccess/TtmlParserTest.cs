using System.Diagnostics;
using System.Globalization;
using System.Text;
using MusicManager.Common;
using MusicManager.DataAccess;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.Tests.DataAccess;

public class TtmlParserTest : IDisposable
{
    private const string NS = "xmlns=\"http://www.w3.org/ns/ttml\""
            + " xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\""
            + " xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\"";

    private static readonly string FFFD = ((char)0xFFFD).ToString();

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public TtmlParserTest()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    // ------------------------------------------------------------------ fixture helpers

    private static string Tt(string body)
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><tt " + NS + " itunes:timing=\"Word\">"
                + "<head><metadata><ttm:agent type=\"person\" xml:id=\"v1\"/></metadata></head>"
                + "<body dur=\"03:09.205\"><div begin=\"00:00.000\" end=\"03:09.205\">" + body + "</div></body></tt>";
    }

    private static string Span(string begin, string end, string text)
    {
        return "<span begin=\"" + begin + "\" end=\"" + end + "\">" + text + "</span>";
    }

    private static string Translation(string text)
    {
        return "<span ttm:role=\"x-translation\" xml:lang=\"zh-CN\">" + text + "</span>";
    }

    private static string Line(string begin, string end, string key, params string[] inner)
    {
        return "<p begin=\"" + begin + "\" end=\"" + end + "\" ttm:agent=\"v1\" itunes:key=\"" + key + "\">"
                + string.Join("", inner) + "</p>";
    }

    private static Lyric Parse(string xml)
    {
        return TtmlParser.Parse(Encoding.UTF8.GetBytes(xml), "song.ttml");
    }

    private static List<string> L(params string[] items)
    {
        return new List<string>(items);
    }

    private static List<string> Words(LyricLine line)
    {
        var output = new List<string>();
        foreach (WordTimeStamp w in line.WordTimeStamps)
        {
            output.Add(w.Word);
        }
        return output;
    }

    private static List<string> LineTexts(Lyric lyric)
    {
        var output = new List<string>();
        foreach (LyricLine l in lyric.Lines)
        {
            output.Add(l.Line);
        }
        return output;
    }

    /// <summary>the guarantee downstream code relies on: begin &lt;= end &lt;= next begin for every word</summary>
    private static void AssertWellOrdered(Lyric lyric)
    {
        foreach (LyricLine l in lyric.Lines)
        {
            Assert.True(l.EndTimeMs >= l.StartTimeMs, "line end before start: " + l.Line);
            long previousEnd = long.MinValue;
            foreach (WordTimeStamp w in l.WordTimeStamps)
            {
                Assert.True(w.EndTimeMs >= w.StartTimeMs, "word end before start: " + w.Word);
                Assert.True(w.StartTimeMs >= previousEnd, "word overlaps previous: " + w.Word);
                previousEnd = w.EndTimeMs;
            }
        }
    }

    private static byte[] Join(params byte[][] parts)
    {
        var output = new MemoryStream();
        foreach (byte[] part in parts)
        {
            output.Write(part, 0, part.Length);
        }
        return output.ToArray();
    }

    private static byte[] Utf8(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    private static string JoinWarnings(Lyric lyric)
    {
        return string.Join("\n", lyric.Warnings);
    }

    /// <summary>List.toString() as the Java assertion messages print it</summary>
    private static string Show(List<string> list)
    {
        return "[" + string.Join(", ", list) + "]";
    }

    // ------------------------------------------------------------------ the happy path

    [Fact]
    public void ParsesAppleWordSyncedLines()
    {
        Lyric lyric = Parse(Tt(
                Line("00:00.379", "00:01.083", "L1",
                        Span("00:00.379", "00:00.547", "Bite - "),
                        Span("00:00.547", "00:00.815", "aespa ("),
                        Span("00:00.815", "00:01.083", "에스파)"),
                        Translation("TME享有本翻译作品的著作权"))
                        + Line("00:03.631", "00:05.738", "L2",
                                Span("00:03.631", "00:04.225", "Careful, "),
                                Span("00:04.225", "00:04.480", "'cause "),
                                Span("00:04.480", "00:04.700", "I "),
                                Span("00:04.700", "00:05.738", "Bite"),
                                Translation("小心一些 因为我可不是好惹的"))));

        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        Assert.Equal("song.ttml", lyric.Path);
        Assert.True(lyric.Warnings.Count == 0, Show(lyric.Warnings));
        Assert.Equal(2, lyric.Lines.Count);

        LyricLine first = lyric.Lines[0];
        Assert.Equal("Bite - aespa (에스파)", first.Line);
        Assert.Equal(379, first.StartTimeMs);
        Assert.Equal(1083, first.EndTimeMs);
        Assert.Equal(L("Bite - ", "aespa (", "에스파)"), Words(first));
        Assert.Equal(379, first.WordTimeStamps[0].StartTimeMs);
        Assert.Equal(547, first.WordTimeStamps[0].EndTimeMs);
        Assert.Equal(L("TME享有本翻译作品的著作权"), first.Translations);

        LyricLine second = lyric.Lines[1];
        Assert.Equal("Careful, 'cause I Bite", second.Line);
        Assert.Equal(L("Careful, ", "'cause ", "I ", "Bite"), Words(second));
        Assert.Equal(L("小心一些 因为我可不是好惹的"), second.Translations);
        AssertWellOrdered(lyric);
    }

    [Fact]
    public void PrettyPrintedFileParsesTheSameAsCompactOne()
    {
        string compact = Tt(Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:01.500", "Hello "),
                Span("00:01.500", "00:02.000", "world")));
        string pretty = "<?xml version=\"1.0\"?>\n<tt " + NS + " itunes:timing=\"Word\">\n  <head>\n    <metadata>\n"
                + "      <ttm:agent type=\"person\" xml:id=\"v1\"/>\n    </metadata>\n  </head>\n  <body>\n    <div>\n"
                + "      <p begin=\"00:01.000\" end=\"00:02.000\" itunes:key=\"L1\">\n"
                + "        <span begin=\"00:01.000\" end=\"00:01.500\">Hello </span>\n"
                + "        <span begin=\"00:01.500\" end=\"00:02.000\">world</span>\n"
                + "      </p>\n    </div>\n  </body>\n</tt>\n";

        Lyric a = Parse(compact);
        Lyric b = Parse(pretty);

        Assert.Equal(Words(a.Lines[0]), Words(b.Lines[0]));
        Assert.Equal("Hello world", b.Lines[0].Line);
        Assert.Empty(b.Warnings);
    }

    [Fact]
    public void CrlfLineEndingsAreHarmless()
    {
        string xml = "<?xml version=\"1.0\"?>\r\n<tt " + NS + ">\r\n<body>\r\n<div>\r\n"
                + "<p begin=\"00:01.000\" end=\"00:02.000\">\r\n"
                + "<span begin=\"00:01.000\" end=\"00:01.500\">Hello </span>\r\n"
                + "<span begin=\"00:01.500\" end=\"00:02.000\">world</span>\r\n"
                + "</p>\r\n</div>\r\n</body>\r\n</tt>\r\n";

        Lyric lyric = Parse(xml);

        Assert.Equal(L("Hello ", "world"), Words(lyric.Lines[0]));
    }

    [Fact]
    public void MultipleDivsKeepDocumentOrder()
    {
        string xml = "<tt " + NS + "><body>"
                + "<div>" + Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "one")) + "</div>"
                + "<div>" + Line("00:03.000", "00:04.000", "L2", Span("00:03.000", "00:04.000", "two")) + "</div>"
                + "<div><div>" + Line("00:05.000", "00:06.000", "L3", Span("00:05.000", "00:06.000", "three"))
                + "</div></div></body></tt>";

        Assert.Equal(L("one", "two", "three"), LineTexts(Parse(xml)));
    }

    [Fact]
    public void OtherPrefixForTheTtmlNamespaceStillWorks()
    {
        string xml = "<tt:tt xmlns:tt=\"http://www.w3.org/ns/ttml\"><tt:body><tt:div>"
                + "<tt:p begin=\"00:01.000\" end=\"00:02.000\"><tt:span begin=\"00:01.000\" end=\"00:02.000\">Hi</tt:span>"
                + "</tt:p></tt:div></tt:body></tt:tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal(L("Hi"), Words(lyric.Lines[0]));
        Assert.Empty(lyric.Warnings);
    }

    [Fact]
    public void UndeclaredNamespacePrefixIsRepaired()
    {
        string xml = "<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div>"
                + "<p begin=\"00:01.000\" end=\"00:02.000\" ttm:agent=\"v1\" itunes:key=\"L1\">"
                + "<span begin=\"00:01.000\" end=\"00:02.000\">Hi</span>"
                + "<span ttm:role=\"x-translation\">hello</span></p></div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal(L("Hi"), Words(lyric.Lines[0]));
        Assert.Equal(L("hello"), lyric.Lines[0].Translations);
        Assert.Contains("not well-formed", JoinWarnings(lyric));
    }

    [Fact]
    public void GenericRootThatIsNotTtIsAcceptedWhenLinesAreTimed()
    {
        Lyric lyric = Parse("<transcript><p begin=\"1s\" end=\"2s\">Hi there</p></transcript>");

        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        Assert.Equal("Hi there", lyric.Lines[0].Line);
        Assert.Equal(1000, lyric.Lines[0].StartTimeMs);
    }

    // ------------------------------------------------------------------ time formats

    [Theory]
    [InlineData("00:00.379", 379L)]
    [InlineData("00:00.000", 0L)]
    [InlineData("01:02.345", 62345L)]
    [InlineData("1:02.345", 62345L)]
    [InlineData("75:00.000", 4500000L)]
    [InlineData("01:02:03.456", 3723456L)]
    [InlineData("00:04:00.756", 240756L)]
    [InlineData("01:02:03", 3723000L)]
    [InlineData("01:02:03:25", 3723000L)]
    [InlineData("12.5", 12500L)]
    [InlineData("0", 0L)]
    [InlineData("12.5s", 12500L)]
    [InlineData("12s", 12000L)]
    [InlineData("1500ms", 1500L)]
    [InlineData("1.5m", 90000L)]
    [InlineData("1h", 3600000L)]
    [InlineData("0.5h", 1800000L)]
    [InlineData("10000000t", 1000L)]
    [InlineData("123456t", 12L)]
    [InlineData("00:01.5", 1500L)]
    [InlineData("00:01.50", 1500L)]
    [InlineData("00:01.5000", 1500L)]
    [InlineData("00:01.123456789", 1123L)]
    [InlineData("' 00:01.000 '", 1000L)]
    public void ParsesEveryTimeFormat(string text, long expectedMs)
    {
        Assert.Equal(expectedMs, TtmlParser.ParseTime(text.Replace("'", ""), 10_000_000L));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("-1s")]
    [InlineData("-00:01.000")]
    [InlineData("1e5s")]
    [InlineData("1.2.3s")]
    [InlineData("12:xx")]
    [InlineData("00:00:00:00:00")]
    [InlineData("9111,67][")]
    [InlineData("富[04:00.9")]
    [InlineData("1,5s")]
    [InlineData(".5s")]
    [InlineData("5x")]
    [InlineData("s")]
    [InlineData("ms")]
    [InlineData("00:01.")]
    [InlineData("::")]
    [InlineData("00:00:00.00.0")]
    [InlineData("99999999999999999999s")]
    [InlineData("99999999999999999999999999999999999999999999999999999999999999999999999")]
    [InlineData("360000001ms")]
    [InlineData("1000h")]
    public void RejectsInvalidAndOverflowingTimes(string text)
    {
        Assert.Equal(-1, TtmlParser.ParseTime(text, 10_000_000L));
    }

    [Fact]
    public void TickRateAttributeScalesTickValues()
    {
        string xml = "<tt " + NS + " xmlns:ttp=\"http://www.w3.org/ns/ttml#parameter\" ttp:tickRate=\"1000\"><body><div>"
                + "<p begin=\"1500t\" end=\"3000t\"><span begin=\"1500t\" end=\"3000t\">tick</span></p></div></body></tt>";

        LyricLine l = Parse(xml).Lines[0];

        Assert.Equal(1500, l.StartTimeMs);
        Assert.Equal(3000, l.EndTimeMs);
    }

    [Fact]
    public void MixedTimeFormatsInOneFile()
    {
        string xml = Tt("<p begin=\"00:00:01.000\" end=\"2.5s\" itunes:key=\"L1\">"
                + Span("1s", "1500ms", "a ") + Span("0:01.500", "00:00:02.500", "b") + "</p>");

        LyricLine l = Parse(xml).Lines[0];

        Assert.Equal(1000, l.StartTimeMs);
        Assert.Equal(2500, l.EndTimeMs);
        Assert.Equal(1500, l.WordTimeStamps[1].StartTimeMs);
        Assert.Equal(2500, l.WordTimeStamps[1].EndTimeMs);
    }

    // ------------------------------------------------------------------ whitespace between spans

    private static string OneLine(string inner)
    {
        return "<tt " + NS + "><body><div><p begin=\"00:01.000\" end=\"00:05.000\">" + inner + "</p></div></body></tt>";
    }

    [Fact]
    public void NewlineBetweenSpansIsFormattingNotAWordGap()
    {
        Lyric lyric = Parse(OneLine("\n    " + Span("00:01.000", "00:02.000", "Hello") + "\n    "
                + Span("00:02.000", "00:03.000", "World") + "\n"));

        Assert.Equal(L("Hello", "World"), Words(lyric.Lines[0]));
        Assert.Equal("HelloWorld", lyric.Lines[0].Line);
    }

    [Fact]
    public void SingleSpaceBetweenSpansBecomesAWordSeparator()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "Hello") + " "
                + Span("00:02.000", "00:03.000", "World")));

        Assert.Equal(L("Hello ", "World"), Words(lyric.Lines[0]));
        Assert.Equal("Hello World", lyric.Lines[0].Line);
    }

    [Fact]
    public void SeparatorIsNotAddedWhenTheWordAlreadyEndsInASpace()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "Hello ") + " "
                + Span("00:02.000", "00:03.000", "World")));

        Assert.Equal(L("Hello ", "World"), Words(lyric.Lines[0]));
    }

    [Fact]
    public void TwoSpacesBetweenSpansAreFormatting()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "Hello") + "  "
                + Span("00:02.000", "00:03.000", "World")));

        Assert.Equal(L("Hello", "World"), Words(lyric.Lines[0]));
    }

    [Fact]
    public void XmlSpacePreserveTurnsIndentationIntoASeparator()
    {
        string xml = "<tt " + NS + "><body><div><p xml:space=\"preserve\" begin=\"00:01.000\" end=\"00:05.000\">\n  "
                + Span("00:01.000", "00:02.000", "Hello") + "\n  " + Span("00:02.000", "00:03.000", "World")
                + "\n</p></div></body></tt>";

        Assert.Equal(L("Hello ", "World"), Words(Parse(xml).Lines[0]));
    }

    [Fact]
    public void NoBreakAndIdeographicSpacesBetweenSpansAreKept()
    {
        string nbsp = ((char)0xA0).ToString();
        string ideographic = ((char)0x3000).ToString();
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "a") + nbsp + Span("00:02.000", "00:03.000", "b")
                + ideographic + Span("00:03.000", "00:04.000", "c")));

        Assert.Equal(L("a" + nbsp, "b" + ideographic, "c"), Words(lyric.Lines[0]));
    }

    [Fact]
    public void WhitespaceOnlyTimedSpansAreRealWords()
    {
        // Stray Kids - Any: every space is its own timed span
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:01.100", "아니") + Span("00:01.100", "00:01.200", " ")
                + Span("00:01.200", "00:01.300", "(Any)")));

        LyricLine l = lyric.Lines[0];
        Assert.Equal(L("아니", " ", "(Any)"), Words(l));
        Assert.Equal("아니 (Any)", l.Line);
    }

    [Fact]
    public void WordTextIsKeptExactlyIncludingLeadingSpace()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "  lead and trail  ")));

        LyricLine l = lyric.Lines[0];
        Assert.Equal("  lead and trail  ", l.WordTimeStamps[0].Word);
        Assert.Equal("  lead and trail", l.Line); // only trailing whitespace is trimmed from the line text
    }

    // ------------------------------------------------------------------ real broken files

    [Fact]
    public void InvalidUtf8InsideATranslationDropsOnlyThatTranslation()
    {
        byte[] before = Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\"?><tt " + NS + "><body><div>"
                + "<p begin=\"00:01.000\" end=\"00:02.000\" itunes:key=\"L1\">" + Span("00:01.000", "00:02.000", "Hello")
                + "<span ttm:role=\"x-translation\" xml:lang=\"zh-CN\">TME");
        byte[] gbk = { 0xCF, 0xED, 0xD3, 0xD0, 0xB1, 0xBE };
        byte[] after = Utf8("</span></p><p begin=\"00:03.000\" end=\"00:04.000\" itunes:key=\"L2\">"
                + Span("00:03.000", "00:04.000", "World") + Translation("你好") + "</p></div></body></tt>");

        Lyric lyric = TtmlParser.Parse(Join(before, gbk, after), "song.ttml");

        Assert.Equal(2, lyric.Lines.Count);
        Assert.Equal(L("Hello"), Words(lyric.Lines[0]));
        Assert.Empty(lyric.Lines[0].Translations);
        Assert.Equal(L("你好"), lyric.Lines[1].Translations);
        Assert.Single(lyric.Warnings);
        Assert.Contains("L1", lyric.Warnings[0]);
        Assert.Contains("dropped corrupted translation", lyric.Warnings[0]);
    }

    [Fact]
    public void InvalidUtf8InsideAWordIsStrippedButTheWordKeepsItsTiming()
    {
        byte[] before = Utf8("<tt " + NS + "><body><div><p begin=\"00:01.000\" end=\"00:05.000\">"
                + "<span begin=\"00:01.000\" end=\"00:02.000\">by");
        byte[] bad = { 0xA3, 0xBA }; // a GBK full-width colon
        byte[] after = Utf8("</span>" + Span("00:02.000", "00:03.000", "Tim") + "</p></div></body></tt>");

        Lyric lyric = TtmlParser.Parse(Join(before, bad, after), "song.ttml");

        LyricLine l = lyric.Lines[0];
        Assert.Equal(L("by", "Tim"), Words(l));
        Assert.Equal(1000, l.WordTimeStamps[0].StartTimeMs);
        Assert.Equal(2000, l.WordTimeStamps[0].EndTimeMs);
        Assert.DoesNotContain(FFFD, l.Line);
        Assert.Single(lyric.Warnings);
    }

    [Fact]
    public void BareAmpersandsAreRepaired()
    {
        string xml = Tt(Line("00:01.000", "00:02.000", "L1",
                Span("00:01.000", "00:01.500", "Kick & "), Span("00:01.500", "00:02.000", "snare"),
                Translation("Hey drummer兴奋得紧张 Kick & snare热烈的感情")));

        Lyric lyric = Parse(xml);

        LyricLine l = lyric.Lines[0];
        Assert.Equal(L("Kick & ", "snare"), Words(l));
        Assert.Equal(L("Hey drummer兴奋得紧张 Kick & snare热烈的感情"), l.Translations);
        Assert.Contains("not well-formed", JoinWarnings(lyric));
    }

    [Fact]
    public void ValidEntitiesAreDecodedWithoutAnyWarning()
    {
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:02.000", "L1",
                Span("00:01.000", "00:02.000", "Tom &amp; Jerry &lt;3 it&#39;s &#x41; &quot;q&quot; &apos;a&apos; &gt;"))));

        Assert.Equal("Tom & Jerry <3 it's A \"q\" 'a' >", lyric.Lines[0].Line);
        Assert.Empty(lyric.Warnings);
    }

    [Fact]
    public void UnknownEntityIsKeptAsLiteralText()
    {
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "a&nbsp;b &#0; &#xD800; &#99999999;"))));

        Assert.Equal("a&nbsp;b &#0; &#xD800; &#99999999;", lyric.Lines[0].Line);
    }

    [Fact]
    public void StrayLessThanAsTheWholeTextOfASpan()
    {
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:04.000", "L1",
                Span("00:01.000", "00:02.000", "u + me = "), Span("00:02.968", "00:03.710", "<"),
                Span("00:03.710", "00:04.000", "3 - Olivia Rodrigo"))));

        LyricLine l = lyric.Lines[0];
        Assert.Equal(L("u + me = ", "<", "3 - Olivia Rodrigo"), Words(l));
        Assert.Equal("u + me = <3 - Olivia Rodrigo", l.Line);
        Assert.Contains("not well-formed", JoinWarnings(lyric));
    }

    [Fact]
    public void LeakedTimeTagFragmentsInAttributesAndText()
    {
        // Stray Kids - Any, line L9
        string xml = Tt(Line("00:09.111", "00:09.178", "L9",
                "<span begin=\"00:09.111\" end=\"9111,67][\"></span>",
                "<span begin=\"9111,67][\" end=\"00:09.125\">0:09.118]All </span>",
                Span("00:09.125", "00:09.133", "instruments "), Span("00:09.133", "00:09.140", "and ")));

        Lyric lyric = Parse(xml);

        LyricLine l = lyric.Lines[0];
        Assert.Equal(L("All ", "instruments ", "and "), Words(l));
        Assert.Equal(9111, l.WordTimeStamps[0].StartTimeMs);
        Assert.Equal(9125, l.WordTimeStamps[0].EndTimeMs);
        Assert.Equal(9111, l.StartTimeMs);
        Assert.Equal(9178, l.EndTimeMs);
        Assert.Single(lyric.Warnings);
        Assert.Contains("L9", lyric.Warnings[0]);
        Assert.Contains("leaked", lyric.Warnings[0]);
        AssertWellOrdered(lyric);
    }

    [Fact]
    public void LeakedFragmentWithALostCharacter()
    {
        // G.E.M. - 李香兰, line L50: the first character of the line ended up inside an attribute
        string xml = Tt(Line("04:00.756", "04:04.036", "L50",
                "<span begin=\"00:04:00.756\" end=\"富[04:00.9\"></span>",
                "<span begin=\"富[04:00.9\" end=\"04:01.076\">4]士</span>",
                Span("04:01.076", "04:01.248", "太"), Span("04:01.248", "04:01.400", "平")));

        Lyric lyric = Parse(xml);

        LyricLine l = lyric.Lines[0];
        Assert.Equal(L("士", "太", "平"), Words(l));
        Assert.Equal(240756, l.WordTimeStamps[0].StartTimeMs);
        Assert.Equal(241076, l.WordTimeStamps[0].EndTimeMs);
        Assert.Single(lyric.Warnings);
    }

    [Fact]
    public void TimeLikePrefixIsOnlyStrippedWhenTheSpanTimingWasCorrupt()
    {
        // a legitimate word that merely looks like a leaked fragment must survive when its times are fine
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "]") + Span("00:02.000", "00:03.000", "1]")));

        Assert.Equal(L("]", "1]"), Words(lyric.Lines[0]));
        Assert.Empty(lyric.Warnings);
    }

    [Fact]
    public void ParagraphsWithoutSpansAreLineSyncedAndStayWordless()
    {
        string xml = Tt(Line("02:50.000", "02:51.633", "L62", Span("02:50.000", "02:51.633", "last sung line"))
                + "<p begin=\"02:51.633\" end=\"02:51.633\" ttm:agent=\"v1\" itunes:key=\"L63\"> Aah-aah heaven, heaven\n            </p>"
                + "<p begin=\"02:51.633\" end=\"02:51.633\" ttm:agent=\"v1\" itunes:key=\"L64\">Aah-aah 天堂 天堂\n            </p>");

        Lyric lyric = Parse(xml);

        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        Assert.Equal(3, lyric.Lines.Count);
        LyricLine plain = lyric.Lines[1];
        Assert.Equal("Aah-aah heaven, heaven", plain.Line);
        Assert.False(plain.IsWordSynced);
        Assert.Empty(plain.WordTimeStamps);
        Assert.Equal(171633, plain.StartTimeMs);
        Assert.Equal(171633, plain.EndTimeMs);
        Assert.Equal("Aah-aah 天堂 天堂", lyric.Lines[2].Line);
    }

    [Fact]
    public void CorruptedPlainTextLineIsDropped()
    {
        byte[] before = Utf8("<tt " + NS + "><body><div>"
                + Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "ok"))
                + "<p begin=\"00:03.000\" end=\"00:03.000\" itunes:key=\"L2\">Aah ");
        byte[] bad = { 0xB0, 0xA1, 0xD7 };
        byte[] after = Utf8("</p></div></body></tt>");

        Lyric lyric = TtmlParser.Parse(Join(before, bad, after), "song.ttml");

        Assert.Equal(L("ok"), LineTexts(lyric));
        Assert.Contains("L2", JoinWarnings(lyric));
        Assert.Contains("dropped corrupted line", JoinWarnings(lyric));
    }

    // ------------------------------------------------------------------ translations

    [Fact]
    public void RomanizationFollowsTranslationsEvenWhenItComesFirst()
    {
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:02.000", "L1",
                Span("00:01.000", "00:02.000", "안녕"),
                "<span ttm:role=\"x-roman\" xml:lang=\"ko-Latn\">annyeong</span>",
                "<span ttm:role=\"x-translation\" xml:lang=\"en\">hello</span>")));

        Assert.Equal(L("hello", "annyeong"), lyric.Lines[0].Translations);
    }

    [Fact]
    public void AppleHeadLevelTranslationsAreMatchedByKey()
    {
        string xml = "<tt " + NS + " itunes:timing=\"Word\"><head><metadata>"
                + "<ttm:agent type=\"person\" xml:id=\"v1\"/>"
                + "<iTunesMetadata xmlns=\"http://music.apple.com/lyric-ttml-internal\">"
                + "<translations><translation type=\"subtitle\" xml:lang=\"en\">"
                + "<text for=\"L1\">Hello there</text>"
                + "<text for=\"L2\"><span xmlns=\"http://www.w3.org/ns/ttml\">Second </span>one</text>"
                + "<text for=\"L99\">no such line</text>"
                + "</translation></translations>"
                + "<transliterations><transliteration xml:lang=\"ko-Latn\"><text for=\"L1\">annyeong</text></transliteration></transliterations>"
                + "</iTunesMetadata></metadata></head><body><div>"
                + Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "안녕"),
                        Translation("inline"))
                + Line("00:03.000", "00:04.000", "L2", Span("00:03.000", "00:04.000", "둘"))
                + "</div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal(2, lyric.Lines.Count);
        Assert.Equal(L("inline", "Hello there", "annyeong"), lyric.Lines[0].Translations);
        Assert.Equal(L("Second one"), lyric.Lines[1].Translations);
        Assert.Equal(L("안녕", "둘"), LineTexts(lyric));
    }

    [Fact]
    public void HeadTextsNeverBecomeLyricLines()
    {
        string xml = "<tt " + NS + "><head><metadata><p begin=\"00:00.000\" end=\"00:01.000\">metadata paragraph</p>"
                + "</metadata></head><body><div>"
                + Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "real")) + "</div></body></tt>";

        Assert.Equal(L("real"), LineTexts(Parse(xml)));
    }

    [Fact]
    public void EmptyOrCorruptTranslationsAreDroppedWithAWarning()
    {
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "a"),
                Translation("   "), Translation("good"), Translation("bad" + FFFD + "text"),
                Translation("\n    multi\n    line\n   "))));

        Assert.Equal(L("good", "multi line"), lyric.Lines[0].Translations);
        Assert.Single(lyric.Warnings);
        Assert.Contains("dropped corrupted translation", lyric.Warnings[0]);
    }

    // ------------------------------------------------------------------ background vocals

    [Fact]
    public void BackgroundVocalsBecomeTheirOwnLineRightAfterTheirParent()
    {
        Lyric lyric = Parse(Tt(
                Line("00:10.000", "00:14.000", "L1",
                        Span("00:10.000", "00:11.000", "Main "), Span("00:11.000", "00:12.000", "vocal"),
                        "<span ttm:role=\"x-bg\"><span begin=\"00:12.000\" end=\"00:13.000\">(ooh </span>"
                                + "<span begin=\"00:13.000\" end=\"00:14.000\">ahh)</span></span>",
                        "<span ttm:role=\"x-translation\" xml:lang=\"en\">main translation</span>")
                        + Line("00:15.000", "00:16.000", "L2", Span("00:15.000", "00:16.000", "Next"))));

        Assert.Equal(L("Main vocal", "(ooh ahh)", "Next"), LineTexts(lyric));
        LyricLine background = lyric.Lines[1];
        Assert.Equal(12000, background.StartTimeMs);
        Assert.Equal(14000, background.EndTimeMs);
        Assert.Equal(L("(ooh ", "ahh)"), Words(background));
        Assert.Equal(L("main translation"), lyric.Lines[0].Translations);
        Assert.Empty(background.Translations);
        Assert.Equal(10000, lyric.Lines[0].StartTimeMs);
        Assert.Equal(14000, lyric.Lines[0].EndTimeMs);
    }

    [Fact]
    public void LeafBackgroundSpanWithItsOwnTimes()
    {
        Lyric lyric = Parse(Tt(Line("00:10.000", "00:14.000", "L1",
                Span("00:10.000", "00:12.000", "Main"),
                "<span ttm:role=\"x-bg\" begin=\"00:12.000\" end=\"00:13.000\">(ooh)</span>")));

        Assert.Equal(L("Main", "(ooh)"), LineTexts(lyric));
        LyricLine background = lyric.Lines[1];
        Assert.Equal(12000, background.StartTimeMs);
        Assert.Equal(13000, background.EndTimeMs);
        Assert.Equal(L("(ooh)"), Words(background));
    }

    [Fact]
    public void UntimedBackgroundVocalBorrowsTheParentLineTimes()
    {
        Lyric lyric = Parse(Tt(Line("00:10.000", "00:14.000", "L1",
                Span("00:10.000", "00:12.000", "Main"), "<span ttm:role=\"x-bg\">(ooh)</span>")));

        Assert.Equal(L("Main", "(ooh)"), LineTexts(lyric));
        LyricLine background = lyric.Lines[1];
        Assert.False(background.IsWordSynced);
        Assert.Equal(10000, background.StartTimeMs);
        Assert.Equal(14000, background.EndTimeMs);
    }

    [Fact]
    public void ParagraphWithOnlyBackgroundVocalsKeepsTheTranslationOnTheFirstOfThem()
    {
        Lyric lyric = Parse(Tt(Line("00:10.000", "00:14.000", "L1",
                "<span ttm:role=\"x-bg\"><span begin=\"00:10.000\" end=\"00:11.000\">(la)</span></span>",
                "<span ttm:role=\"x-bg\"><span begin=\"00:11.000\" end=\"00:12.000\">(da)</span></span>",
                Translation("lala"))));

        Assert.Equal(L("(la)", "(da)"), LineTexts(lyric));
        Assert.Equal(L("lala"), lyric.Lines[0].Translations);
    }

    [Fact]
    public void NestedNonBackgroundSpansAreFlattenedIntoWords()
    {
        Lyric lyric = Parse(OneLine("<span><span begin=\"00:01.000\" end=\"00:02.000\">a </span>"
                + "<span><span begin=\"00:02.000\" end=\"00:03.000\">b</span></span></span>"));

        Assert.Equal(L("a ", "b"), Words(lyric.Lines[0]));
    }

    // ------------------------------------------------------------------ line-timed files

    [Fact]
    public void LineTimedFileWithoutSpans()
    {
        string xml = "<tt " + NS + " itunes:timing=\"Line\"><body><div>"
                + "<p begin=\"00:01.000\" end=\"00:03.000\">First</p>"
                + "<p begin=\"00:03.000\">Second</p>"
                + "<p begin=\"6.5s\">Third</p></div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        Assert.Equal(L("First", "Second", "Third"), LineTexts(lyric));
        Assert.Equal(3000, lyric.Lines[0].EndTimeMs);
        Assert.Equal(6500, lyric.Lines[1].EndTimeMs); // missing end: the next line's start
        Assert.Equal(6500, lyric.Lines[2].StartTimeMs);
        Assert.Equal(6500, lyric.Lines[2].EndTimeMs);
        foreach (LyricLine l in lyric.Lines)
        {
            Assert.Empty(l.WordTimeStamps);
        }
    }

    [Fact]
    public void CompletelyUntimedLinesAreNoSync()
    {
        Lyric lyric = Parse("<tt " + NS + "><body><div><p>One</p><p>Two</p></div></body></tt>");

        Assert.Equal(SyncType.NoSync, lyric.GetSyncType());
        Assert.Equal(L("One", "Two"), LineTexts(lyric));
        Assert.Equal(0, lyric.Lines[1].StartTimeMs);
    }

    [Fact]
    public void UntimedLineInATimedFileFollowsThePreviousLine()
    {
        Lyric lyric = Parse("<tt " + NS + "><body><div><p begin=\"1s\" end=\"2s\">One</p><p>Two</p></div></body></tt>");

        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        Assert.Equal(2000, lyric.Lines[1].StartTimeMs);
        Assert.Contains("no start time", JoinWarnings(lyric));
    }

    [Fact]
    public void ParagraphTimesFallBackToTheWords()
    {
        Lyric lyric = Parse("<tt " + NS + "><body><div><p>" + Span("00:01.000", "00:02.000", "a ")
                + Span("00:02.000", "00:03.500", "b") + "</p></div></body></tt>");

        LyricLine l = lyric.Lines[0];
        Assert.Equal(1000, l.StartTimeMs);
        Assert.Equal(3500, l.EndTimeMs);
        Assert.Empty(lyric.Warnings);
    }

    // ------------------------------------------------------------------ timing repair

    [Fact]
    public void SpanWithoutOrWithBadBeginTakesThePreviousEnd()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "A ") + "<span end=\"00:03.000\">B </span>"
                + "<span begin=\"junk\" end=\"00:04.000\">C</span>"));

        LyricLine l = lyric.Lines[0];
        Assert.Equal(2000, l.WordTimeStamps[1].StartTimeMs);
        Assert.Equal(3000, l.WordTimeStamps[1].EndTimeMs);
        Assert.Equal(3000, l.WordTimeStamps[2].StartTimeMs);
        Assert.Equal(4000, l.WordTimeStamps[2].EndTimeMs);
        Assert.Single(lyric.Warnings);
        AssertWellOrdered(lyric);
    }

    [Fact]
    public void SpanWithBadEndTakesTheNextValidTimeOrTheParagraphEnd()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "oops", "A ") + Span("00:03.000", "00:04.000", "B ")
                + "<span begin=\"00:04.000\" end=\"\">C</span>"));

        LyricLine l = lyric.Lines[0];
        Assert.Equal(3000, l.WordTimeStamps[0].EndTimeMs);
        Assert.Equal(5000, l.WordTimeStamps[2].EndTimeMs); // the <p> ends at 5s
        Assert.Single(lyric.Warnings);
        AssertWellOrdered(lyric);
    }

    [Fact]
    public void FirstSpanWithBadBeginStartsWhereTheParagraphStarts()
    {
        Lyric lyric = Parse(OneLine("<span begin=\"nope\" end=\"00:02.000\">A</span>"));

        Assert.Equal(1000, lyric.Lines[0].WordTimeStamps[0].StartTimeMs);
    }

    [Fact]
    public void HugeAndNegativeTimesCountAsInvalid()
    {
        Lyric lyric = Parse(OneLine("<span begin=\"99999999999999999999s\" end=\"00:02.000\">A </span>"
                + "<span begin=\"00:02.000\" end=\"-5s\">B</span>"));

        LyricLine l = lyric.Lines[0];
        Assert.Equal(1000, l.WordTimeStamps[0].StartTimeMs);
        Assert.Equal(2000, l.WordTimeStamps[0].EndTimeMs);
        Assert.Equal(5000, l.WordTimeStamps[1].EndTimeMs);
        AssertWellOrdered(lyric);
    }

    [Fact]
    public void OutOfOrderAndOverlappingWordTimesAreClampedWithOneWarningPerLine()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "A ") + Span("00:01.500", "00:02.500", "B ")
                + Span("00:00.500", "00:03.000", "C ") + Span("00:04.000", "00:03.500", "D")));

        LyricLine l = lyric.Lines[0];
        WordTimeStamp[] w = l.WordTimeStamps;
        Assert.Equal(1000, w[0].StartTimeMs);
        Assert.Equal(1500, w[0].EndTimeMs); // trimmed where B starts
        Assert.Equal(1500, w[1].StartTimeMs);
        Assert.Equal(1500, w[1].EndTimeMs); // C was "before" B, so it starts where B starts
        Assert.Equal(4000, w[3].StartTimeMs);
        Assert.Equal(4000, w[3].EndTimeMs); // end before begin
        Assert.Single(lyric.Warnings);
        AssertWellOrdered(lyric);
    }

    [Fact]
    public void EmptySpansAreSkippedWithoutAWarning()
    {
        // 4863 of these exist in the real library
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:01.500", "i-") + "<span begin=\"00:01.500\" end=\"00:01.501\"></span>"
                + "<span begin=\"00:01.501\" end=\"00:01.400\"></span>" + Span("00:01.501", "00:02.000", "dle")));

        Assert.Equal(L("i-", "dle"), Words(lyric.Lines[0]));
        Assert.Empty(lyric.Warnings);
    }

    [Fact]
    public void ParagraphWhoseWordsAreAllEmptyIsDropped()
    {
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:02.000", "L1", "<span begin=\"00:01.000\" end=\"00:02.000\"></span>")
                + Line("00:03.000", "00:04.000", "L2", Span("00:03.000", "00:04.000", "kept"))));

        Assert.Equal(L("kept"), LineTexts(lyric));
    }

    [Fact]
    public void WhenEveryLineIsDroppedTheFileIsRejected()
    {
        string xml = Tt(Line("00:01.000", "00:02.000", "L1", "<span begin=\"00:01.000\" end=\"00:02.000\"></span>"));

        Assert.Throws<LyricParseException>(() => Parse(xml));
    }

    [Fact]
    public void LooseTextBetweenSpansBecomesAnUntimedWordFilledFromItsNeighbours()
    {
        Lyric lyric = Parse(OneLine(Span("00:01.000", "00:02.000", "Hello ") + "world "
                + Span("00:03.000", "00:04.000", "foo")));

        LyricLine l = lyric.Lines[0];
        Assert.Equal("Hello world foo", l.Line);
        Assert.Equal(L("Hello ", "world ", "foo"), Words(l));
        Assert.Equal(2000, l.WordTimeStamps[1].StartTimeMs);
        Assert.Equal(3000, l.WordTimeStamps[1].EndTimeMs);
        AssertWellOrdered(lyric);
    }

    [Fact]
    public void SpansWithoutAnyTimesMakeALineSyncedLine()
    {
        Lyric lyric = Parse(OneLine("<span>Hello </span><span>world</span>"));

        LyricLine l = lyric.Lines[0];
        Assert.Equal("Hello world", l.Line);
        Assert.False(l.IsWordSynced);
        Assert.Equal(1000, l.StartTimeMs);
        Assert.Equal(5000, l.EndTimeMs);
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
    }

    // ------------------------------------------------------------------ markup oddities

    [Fact]
    public void BrInsideAParagraphBecomesASpace()
    {
        string xml = "<tt " + NS + "><body><div><p begin=\"1s\" end=\"3s\">Line one<br/>Line two</p>"
                + "<p begin=\"3s\" end=\"5s\">" + Span("3s", "4s", "Hello") + "<br/>" + Span("4s", "5s", "World")
                + "</p></div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal("Line one Line two", lyric.Lines[0].Line);
        Assert.Equal(L("Hello ", "World"), Words(lyric.Lines[1]));
    }

    [Fact]
    public void CommentsProcessingInstructionsAndCdataAreHandled()
    {
        string xml = "<?xml version=\"1.0\"?><!-- header --><tt " + NS + "><?pi data?><body><div>"
                + "<p begin=\"1s\" end=\"2s\"><!-- c --><span begin=\"1s\" end=\"2s\"><![CDATA[a < b & c]]></span></p>"
                + "</div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal("a < b & c", lyric.Lines[0].Line);
        Assert.Empty(lyric.Warnings);
    }

    [Fact]
    public void SelfClosingParagraphIsIgnored()
    {
        Lyric lyric = Parse("<tt " + NS + "><body><div><p begin=\"1s\" end=\"2s\"/>"
                + "<p begin=\"2s\" end=\"3s\">kept</p></div></body></tt>");

        Assert.Equal(L("kept"), LineTexts(lyric));
    }

    [Fact]
    public void CjkEmojiAndSurrogatePairsSurvive()
    {
        string emoji = char.ConvertFromUtf32(0x1F600);
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:03.000", "L1", Span("00:01.000", "00:02.000", "你好" + emoji),
                Span("00:02.000", "00:03.000", "こんにちは한국어"), Translation("翻译" + emoji))));

        LyricLine l = lyric.Lines[0];
        Assert.Equal("你好" + emoji + "こんにちは한국어", l.Line);
        Assert.Equal(L("翻译" + emoji), l.Translations);
    }

    // ------------------------------------------------------------------ byte level decoding

    private static readonly string SIMPLE = Tt(Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "한국어 ")));

    private static void AssertSimple(Lyric lyric)
    {
        Assert.Single(lyric.Lines);
        Assert.Equal(L("한국어 "), Words(lyric.Lines[0]));
        Assert.True(lyric.Warnings.Count == 0, Show(lyric.Warnings));
    }

    [Fact]
    public void Utf8BomIsSkipped()
    {
        AssertSimple(TtmlParser.Parse(Join(new byte[] { 0xEF, 0xBB, 0xBF }, Utf8(SIMPLE)), "x"));
    }

    [Fact]
    public void Utf16WithBomIsDecoded()
    {
        AssertSimple(TtmlParser.Parse(Join(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes(SIMPLE)), "x"));
        AssertSimple(TtmlParser.Parse(Join(new byte[] { 0xFE, 0xFF }, Encoding.BigEndianUnicode.GetBytes(SIMPLE)), "x"));
    }

    [Fact]
    public void Utf16WithoutBomIsDetectedFromTheFirstTag()
    {
        AssertSimple(TtmlParser.Parse(Encoding.Unicode.GetBytes(SIMPLE), "x"));
        AssertSimple(TtmlParser.Parse(Encoding.BigEndianUnicode.GetBytes(SIMPLE), "x"));
    }

    [Fact]
    public void NulAndControlCharactersAreStripped()
    {
        string nul = ((char)0).ToString();
        string soh = ((char)1).ToString();
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "Hel" + nul + "lo" + soh))));

        Assert.Equal("Hello", lyric.Lines[0].Line);
        Assert.Empty(lyric.Warnings);
    }

    [Fact]
    public void LastDitchByteSoupIsRejectedNotCrashed()
    {
        byte[] junk = new byte[4096];
        new JavaRandom(42).NextBytes(junk); // the same bytes the Java test uses

        Assert.Throws<LyricParseException>(() => TtmlParser.Parse(junk, "x"));
        Assert.Throws<LyricParseException>(() => TtmlParser.Parse(new byte[] { 0xFF }, "x"));
        Assert.Throws<LyricParseException>(() => TtmlParser.Parse(new byte[] { 0 }, "x"));
        Assert.Throws<LyricParseException>(() => TtmlParser.Parse(new byte[] { 0xFE, 0xFF, 0 }, "x"));
    }

    /// <summary>java.util.Random: a seed gives the same byte sequence in both languages</summary>
    private sealed class JavaRandom
    {
        private const long Multiplier = 0x5DEECE66DL;
        private const long Mask = (1L << 48) - 1;
        private long _seed;

        public JavaRandom(long seed)
        {
            _seed = (seed ^ Multiplier) & Mask;
        }

        private int Next(int bits)
        {
            _seed = (_seed * Multiplier + 0xBL) & Mask;
            return (int)(_seed >>> (48 - bits));
        }

        public void NextBytes(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length;)
            {
                for (int rnd = Next(32), n = Math.Min(bytes.Length - i, 4); n-- > 0; rnd >>= 8)
                {
                    bytes[i++] = (byte)rnd;
                }
            }
        }
    }

    [Fact]
    public void DurationIsUsedWhenThereIsNoEnd()
    {
        string xml = "<tt " + NS + "><body><div><p begin=\"1s\" dur=\"4s\">"
                + "<span begin=\"1s\" dur=\"1s\">a </span><span begin=\"2s\" dur=\"500ms\">b</span>"
                + "<span begin=\"3s\" end=\"3.5s\" dur=\"99s\">c</span></p></div></body></tt>";

        LyricLine l = Parse(xml).Lines[0];

        Assert.Equal(1000, l.StartTimeMs);
        Assert.Equal(5000, l.EndTimeMs);
        Assert.Equal(2000, l.WordTimeStamps[0].EndTimeMs);
        Assert.Equal(2500, l.WordTimeStamps[1].EndTimeMs);
        Assert.Equal(3500, l.WordTimeStamps[2].EndTimeMs); // an explicit end beats dur
    }

    [Theory]
    [InlineData("ISO-8859-1")]
    [InlineData("UTF-16")]
    [InlineData("US-ASCII")]
    [InlineData("GBK")]
    [InlineData("nonsense-charset")]
    public void TheDeclaredEncodingNeverOverridesTheRealOne(string declared)
    {
        string xml = "<?xml version=\"1.0\" encoding=\"" + declared + "\"?>" + SIMPLE.Substring(SIMPLE.IndexOf("<tt", StringComparison.Ordinal));

        AssertSimple(Parse(xml));
    }

    [Fact]
    public void WhitespaceBeforeTheXmlDeclarationIsTolerated()
    {
        Lyric lyric = Parse("\n\n   " + SIMPLE);

        Assert.Equal(L("한국어 "), Words(lyric.Lines[0]));
    }

    [Fact]
    public void TrailingGarbageAfterTheRootIsCutOff()
    {
        Lyric lyric = Parse(SIMPLE + "\nGARBAGE after the end");

        Assert.Equal(L("한국어 "), Words(lyric.Lines[0]));
    }

    [Fact]
    public void ConcatenatedDocumentsKeepEveryLine()
    {
        string first = Tt(Line("00:01.000", "00:02.000", "L1", Span("00:01.000", "00:02.000", "one")));
        string second = Tt(Line("00:03.000", "00:04.000", "L1", Span("00:03.000", "00:04.000", "two")));

        Lyric lyric = Parse(first + second);

        Assert.Equal(L("one", "two"), LineTexts(lyric));
    }

    [Fact]
    public void ParagraphThatOnlyHoldsATranslationIsDropped()
    {
        Lyric lyric = Parse(Tt(Line("00:01.000", "00:02.000", "L1", Translation("orphan"))
                + Line("00:03.000", "00:04.000", "L2", Span("00:03.000", "00:04.000", "kept"))));

        Assert.Equal(L("kept"), LineTexts(lyric));
        Assert.Empty(lyric.Lines[0].Translations);
    }

    // ------------------------------------------------------------------ not usable at all

    [Fact]
    public void EmptyInputIsRejected()
    {
        Assert.Throws<LyricParseException>(() => TtmlParser.Parse(new byte[0], "x"));
        Assert.Throws<LyricParseException>(() => TtmlParser.Parse(null, "x"));
    }

    [Fact]
    public void EmptyBodyIsRejected()
    {
        Assert.Throws<LyricParseException>(() => Parse("<tt " + NS + "><body/></tt>"));
        Assert.Throws<LyricParseException>(() => Parse("<tt " + NS + "><body><div></div></body></tt>"));
        Assert.Throws<LyricParseException>(() => Parse(Tt("")));
    }

    [Fact]
    public void NonTtmlDocumentsAreRejected()
    {
        Assert.Throws<LyricParseException>(() => Parse("<?xml version=\"1.0\"?><html><body><p>Hello</p></body></html>"));
        Assert.Throws<LyricParseException>(() => Parse("<root><item>x</item></root>"));
        Assert.Throws<LyricParseException>(() => Parse("just some text, not markup"));
        Assert.Throws<LyricParseException>(() => Parse("[00:01.000]an lrc file by mistake"));
        Assert.Throws<LyricParseException>(() => Parse("{\"json\": true}"));
    }

    [Fact]
    public void RejectionMessageSaysWhy()
    {
        LyricParseException e = Assert.Throws<LyricParseException>(() => Parse("<root/>"));

        Assert.NotNull(e.Message);
        Assert.False(JavaString.IsBlank(e.Message));
    }

    // ------------------------------------------------------------------ damaged structure (lenient scanner)

    private static string ThreeLines()
    {
        return Tt(Line("00:00.379", "00:01.083", "L1", Span("00:00.379", "00:01.083", "first"))
                + Line("00:03.631", "00:05.738", "L2", Span("00:03.631", "00:04.225", "Careful, "),
                        Span("00:04.225", "00:05.738", "Bite"))
                + Line("00:06.000", "00:07.000", "L3", Span("00:06.000", "00:07.000", "third")));
    }

    [Fact]
    public void TruncatedFileKeepsEverythingBeforeTheCut()
    {
        string full = ThreeLines();
        string cut = full.Substring(0, full.IndexOf("Careful, ", StringComparison.Ordinal) + 4); // "...>Care"

        Lyric lyric = Parse(cut);

        Assert.Equal(L("first", "Care"), LineTexts(lyric));
        Assert.Equal(379, lyric.Lines[0].StartTimeMs);
        Assert.Equal(3631, lyric.Lines[1].StartTimeMs);
        Assert.Contains("lenient", JoinWarnings(lyric));
    }

    [Fact]
    public void FileTruncatedInsideATagDropsOnlyThatTag()
    {
        string full = ThreeLines();
        string cut = full.Substring(0, full.IndexOf("<span begin=\"00:03.631\"", StringComparison.Ordinal) + 22);

        Lyric lyric = Parse(cut);

        Assert.Equal(L("first"), LineTexts(lyric));
    }

    [Fact]
    public void MissingClosingParagraphTagsAreImplied()
    {
        string xml = "<tt " + NS + "><body><div>"
                + "<p begin=\"1s\" end=\"2s\"><span begin=\"1s\" end=\"2s\">one</span>"
                + "<p begin=\"2s\" end=\"3s\"><span begin=\"2s\" end=\"3s\">two</span>"
                + "</div></body></tt>";

        Assert.Equal(L("one", "two"), LineTexts(Parse(xml)));
    }

    [Fact]
    public void DuplicateAttributesFirstOneWins()
    {
        string xml = "<tt " + NS + "><body><div><p begin=\"1s\" end=\"3s\">"
                + "<span begin=\"1s\" begin=\"2s\" end=\"3s\">word</span></p></div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal(1000, lyric.Lines[0].WordTimeStamps[0].StartTimeMs);
        Assert.Contains("lenient", JoinWarnings(lyric));
    }

    [Fact]
    public void UnquotedAndSingleQuotedAttributes()
    {
        string xml = "<tt><body><div><p begin=00:01.000 end='00:03.000'>"
                + "<span begin=00:01.000 end=00:02.000>a </span><span begin='00:02.000' end=\"00:03.000\">b</span>"
                + "</p></div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.Equal(L("a ", "b"), Words(lyric.Lines[0]));
        Assert.Equal(3000, lyric.Lines[0].EndTimeMs);
    }

    [Fact]
    public void UnterminatedAttributeQuoteDoesNotSwallowTheRest()
    {
        string xml = "<tt " + NS + "><body><div>"
                + "<p begin=\"1s\" end=\"2s\"><span begin=\"1s\" end=\"2s>broken</span></p>"
                + "<p begin=\"3s\" end=\"4s\"><span begin=\"3s\" end=\"4s\">fine</span></p></div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.True(LineTexts(lyric).Contains("fine"), Show(LineTexts(lyric)));
    }

    [Fact]
    public void UppercaseMarkupInALenientFileIsStillRecognised()
    {
        string xml = "<TT><BODY><DIV><P begin=\"1s\" end=\"2s\"><SPAN begin=\"1s\" end=\"2s\">loud & proud</SPAN></P></DIV></BODY></TT>";

        Lyric lyric = Parse(xml);

        Assert.Equal("loud & proud", lyric.Lines[0].Line);
    }

    [Fact]
    public void LenientScannerStillDecodesEntitiesAndMatchesHeadTranslations()
    {
        string xml = "<tt " + NS + "><head><metadata><iTunesMetadata><translations><translation>"
                + "<text for=\"L1\">hi &amp; bye</text></translation></translations></iTunesMetadata></metadata></head>"
                + "<body><div><p begin=\"1s\" end=\"2s\" itunes:key=\"L1\"><span begin=\"1s\" end=\"2s\">a &amp; b & c</span></p>";

        Lyric lyric = Parse(xml); // unclosed body: only the lenient scanner can read it

        Assert.Equal("a & b & c", lyric.Lines[0].Line);
        Assert.Equal(L("hi & bye"), lyric.Lines[0].Translations);
    }

    // ------------------------------------------------------------------ safety and scale

    [Fact]
    public void ExternalEntitiesAreNeverResolved()
    {
        string secret = Path.Combine(_dir, "secret.txt");
        File.WriteAllText(secret, "TOPSECRET-CONTENT");
        string xml = "<?xml version=\"1.0\"?><!DOCTYPE tt [<!ENTITY xxe SYSTEM \"" + new Uri(secret).AbsoluteUri + "\">]>"
                + "<tt " + NS + "><body><div><p begin=\"1s\" end=\"2s\"><span begin=\"1s\" end=\"2s\">&xxe;</span></p></div></body></tt>";

        Lyric lyric = Parse(xml);

        Assert.DoesNotContain("TOPSECRET", lyric.Lines[0].Line);
        Assert.Equal("&xxe;", lyric.Lines[0].Line);
    }

    [Fact]
    public void EntityExpansionBombsAreNotExpanded()
    {
        var dtd = new StringBuilder("<!ENTITY a0 \"lol\">");
        for (int i = 1; i <= 9; i++)
        {
            string prev = "&a" + (i - 1) + ";";
            dtd.Append("<!ENTITY a").Append(i).Append(" \"").Append(JavaString.Repeat(prev, 10)).Append("\">");
        }
        string xml = "<?xml version=\"1.0\"?><!DOCTYPE tt [" + dtd + "]><tt " + NS
                + "><body><div><p begin=\"1s\" end=\"2s\"><span begin=\"1s\" end=\"2s\">&a9;</span></p></div></body></tt>";

        var watch = Stopwatch.StartNew();
        Lyric lyric = Parse(xml);
        long elapsedMs = watch.ElapsedMilliseconds;

        Assert.Equal("&a9;", lyric.Lines[0].Line);
        Assert.True(elapsedMs < 2000, "took " + elapsedMs + " ms");
    }

    [Fact]
    public void AbsurdlyDeepNestingDoesNotOverflowTheStack()
    {
        int depth = 20_000;
        string xml = "<tt " + NS + "><body><div><p begin=\"1s\" end=\"2s\">" + JavaString.Repeat("<span>", depth) + "deep"
                + JavaString.Repeat("</span>", depth) + "</p></div></body></tt>";

        try
        {
            Lyric lyric = Parse(xml);
            Assert.Contains("deep", lyric.Lines[0].Line);
        }
        catch (LyricParseException)
        {
            // rejecting it is acceptable too; crashing is not
        }
    }

    [Fact]
    public void AbsurdlyDeepUnclosedNestingInLenientMode()
    {
        int depth = 20_000;
        string xml = "<tt " + NS + "><body><div><p begin=\"1s\" end=\"2s\">" + JavaString.Repeat("<span>", depth) + "deep";

        try
        {
            Parse(xml);
        }
        catch (LyricParseException)
        {
            // fine
        }
    }

    [Fact]
    public async Task AdversarialInputsFinishQuickly()
    {
        string prefix = "<tt " + NS + "><body><div><p begin=\"1s\" end=\"2s\">";
        string suffix = "</p></div></body></tt>";
        int n = 100_000;
        string[] nasty =
        {
            prefix + "<span begin=\"1s\" end=\"2s\">" + JavaString.Repeat("<", n) + "</span>" + suffix,
            prefix + JavaString.Repeat("&", n) + suffix,
            prefix + JavaString.Repeat("<!DOCTYPE", n),
            prefix + JavaString.Repeat("<span a=\"", n),
            prefix + JavaString.Repeat("<a ", n) + JavaString.Repeat("x=1 ", n),
            prefix + JavaString.Repeat("<![CDATA[", n),
            prefix + JavaString.Repeat("<!--", n),
            prefix + JavaString.Repeat("&#", n) + JavaString.Repeat("&#x", n),
            prefix + JavaString.Repeat(" ", n) + JavaString.Repeat("\n", n) + suffix,
            prefix + JavaString.Repeat("<span>", 1000) + JavaString.Repeat("</span>", 1000) + JavaString.Repeat("<br/>", n) + suffix,
        };
        foreach (string xml in nasty)
        {
            Task run = Task.Run(() =>
            {
                try
                {
                    Parse(xml);
                }
                catch (LyricParseException)
                {
                    // rejecting garbage is fine; hanging or crashing is not
                }
            });
            // the Java test used assertTimeoutPreemptively(10 s); a hung parse cannot be interrupted here, only reported
            Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(finished == run, "an adversarial input took longer than 10 s");
            await run; // rethrows a crash
        }
    }

    [Fact]
    public void FiveThousandLinesParseQuickly()
    {
        var body = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            long t = i * 3000L;
            var inner = new StringBuilder("\n        ");
            for (int w = 0; w < 8; w++)
            {
                inner.Append(Span(Clock(t + w * 300L), Clock(t + w * 300L + 300), "word" + w + (w < 7 ? " " : "")))
                        .Append("\n        ");
            }
            inner.Append(Translation("第" + i + "行翻译"));
            body.Append("\n      ").Append(Line(Clock(t), Clock(t + 2400), "L" + (i + 1), inner.ToString()));
        }
        string xml = Tt(body.ToString());

        Parse(Tt(Line("00:00.000", "00:01.000", "L1", Span("00:00.000", "00:01.000", "warm up")))); // load classes first
        var watch = Stopwatch.StartNew();
        Lyric lyric = Parse(xml);
        long elapsedMs = watch.ElapsedMilliseconds;

        Assert.Equal(5000, lyric.Lines.Count);
        Assert.Equal(8, lyric.Lines[4999].WordTimeStamps.Length);
        Assert.Equal(L("第4999行翻译"), lyric.Lines[4999].Translations);
        Assert.True(elapsedMs < 1500, "5000 lines took " + elapsedMs + " ms");
    }

    [Fact]
    public void FiveThousandBrokenLinesAlsoParseQuickly()
    {
        // a bare '&' forces the sanitize step; truncation forces the lenient scanner
        var body = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            long t = i * 3000L;
            body.Append(Line(Clock(t), Clock(t + 2400), "L" + (i + 1), Span(Clock(t), Clock(t + 1200), "rock & "),
                    Span(Clock(t + 1200), Clock(t + 2400), "roll")));
        }
        string xml = Tt(body.ToString());
        string truncated = xml.Substring(0, xml.LastIndexOf("roll", StringComparison.Ordinal) + 4);

        var watch = Stopwatch.StartNew();
        Lyric sanitized = Parse(xml);
        Lyric lenient = Parse(truncated);
        long elapsedMs = watch.ElapsedMilliseconds;

        Assert.Equal(5000, sanitized.Lines.Count);
        Assert.Equal(5000, lenient.Lines.Count);
        Assert.Equal("rock & roll", lenient.Lines[4999].Line);
        Assert.True(elapsedMs < 3000, "took " + elapsedMs + " ms");
    }

    private static string Clock(long ms)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", ms / 60000, (ms / 1000) % 60, ms % 1000);
    }
}
