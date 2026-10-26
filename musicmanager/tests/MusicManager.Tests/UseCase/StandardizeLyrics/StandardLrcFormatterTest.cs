using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

public class StandardLrcFormatterTest
{
    private const string Header = "[offset:0]\r\n[tool:Music Manager]\r\n\r\n";
    // spelled as numbers so the invisible characters cannot get lost from the source
    private const char Bom = (char)0xFEFF;
    private const char IdeographicSpace = (char)0x3000;

    private static WordTimeStamp W(string text, long start, long end)
    {
        return new WordTimeStamp(text, start, end);
    }

    private static LyricLine Line(long start, long end, string text, params string[] translations)
    {
        LyricLine line = new LyricLine(text, start, end);
        foreach (string translation in translations)
        {
            line.AddTranslation(translation);
        }
        return line;
    }

    private static LyricLine Words(long start, long end, params WordTimeStamp[] words)
    {
        StringBuilder text = new StringBuilder();
        foreach (WordTimeStamp word in words)
        {
            if (word != null && word.Word != null)
            {
                text.Append(word.Word);
            }
        }
        LyricLine line = new LyricLine(JavaString.StripTrailing(text.ToString()), start, end);
        line.WordTimeStamps = words;
        return line;
    }

    private static Lyric LyricOf(SyncType type, params LyricLine[] lines)
    {
        return new Lyric(type, new List<LyricLine>(lines), "song.lrc");
    }

    private static string Lines(params string[] lines)
    {
        return string.Join("\r\n", lines) + "\r\n";
    }

    // ---- the exact reference style (taken from LDDC's LISA - Cobra.lrc) ----

    [Fact]
    public void MatchesTheReferenceFileExactly()
    {
        LyricLine credit = Words(0, 33, W("Cobra ", 0, 11), W("(Explicit) - ", 11, 22), W("LISA", 22, 33));
        credit.AddTranslation("TME享有本翻译作品的著作权");
        LyricLine dancefloor = Words(583, 1494, W("Dancefloor", 583, 1494));
        dancefloor.AddTranslation("舞池");
        // "We " ends at 10.292 but "do " only starts at 10.519: the gap is an extra tag
        LyricLine gap = Words(9740, 11695, W("(Yeah) ", 9740, 9963), W("We ", 9963, 10292), W("do ", 10519, 10837),
                W("what ", 10837, 11213), W("want", 11213, 11695));
        Lyric lyric = LyricOf(SyncType.WordSync, credit, dancefloor, gap);
        lyric.Metadata["ti"] = "Cobra (Explicit)";
        lyric.Metadata["ar"] = "LISA (리사)";
        lyric.Metadata["al"] = "PRESS PLAY (Explicit)";

        Assert.Equal(Lines(
                "[ti:Cobra (Explicit)]",
                "[ar:LISA (리사)]",
                "[al:PRESS PLAY (Explicit)]",
                "[offset:0]",
                "[tool:Music Manager]",
                "",
                "[00:00.000]<00:00.000>Cobra <00:00.011>(Explicit) - <00:00.022>LISA<00:00.033>",
                "[00:00.000]<00:00.000>TME享有本翻译作品的著作权<00:00.033>",
                "[00:00.583]<00:00.583>Dancefloor<00:01.494>",
                "[00:00.583]<00:00.583>舞池<00:01.494>",
                "[00:09.740]<00:09.740>(Yeah) <00:09.963>We <00:10.292><00:10.519>do <00:10.837>what <00:11.213>want<00:11.695>"),
                StandardLrcFormatter.Format(lyric));
    }

    [Fact]
    public void UsesCrlfAndEndsWithOne()
    {
        string output = StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, Words(0, 10, W("a", 0, 10))));

        Assert.True(output.EndsWith("<00:00.010>\r\n", StringComparison.Ordinal));
        Assert.False(output.EndsWith("\r\n\r\n", StringComparison.Ordinal));
        Assert.Equal(-1, output.Replace("\r\n", "").IndexOf('\n'));
        Assert.Equal(-1, output.Replace("\r\n", "").IndexOf('\r'));
    }

    [Fact]
    public void AdjacentWordsGetNoGapTagButAOneMillisecondGapDoes()
    {
        LyricLine tight = Words(0, 200, W("a ", 0, 100), W("b", 100, 200));
        LyricLine loose = Words(1000, 1200, W("a ", 1000, 1100), W("b", 1101, 1200));

        Assert.Equal(Header + Lines(
                "[00:00.000]<00:00.000>a <00:00.100>b<00:00.200>",
                "[00:01.000]<00:01.000>a <00:01.100><00:01.101>b<00:01.200>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, tight, loose)));
    }

    [Fact]
    public void ZeroLengthWordFollowedByItsNeighbourNeedsNoGapTag()
    {
        LyricLine l = Words(100, 200, W("a ", 100, 100), W("b", 100, 200));

        Assert.Equal(Header + Lines("[00:00.100]<00:00.100>a <00:00.100>b<00:00.200>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
    }

    [Fact]
    public void FirstWordMayStartAfterTheLineTag()
    {
        LyricLine l = Words(1000, 3000, W("a", 1200, 2000), W("b", 2000, 3000));

        Assert.Equal(Header + Lines("[00:01.000]<00:01.200>a<00:02.000>b<00:03.000>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
    }

    [Fact]
    public void MultipleTranslationsFollowTheLineInOrder()
    {
        LyricLine l = Words(1000, 2000, W("hi", 1000, 2000));
        l.AddTranslation("one");
        l.AddTranslation("two");

        Assert.Equal(Header + Lines(
                "[00:01.000]<00:01.000>hi<00:02.000>",
                "[00:01.000]<00:01.000>one<00:02.000>",
                "[00:01.000]<00:01.000>two<00:02.000>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
    }

    // ---- header ----

    [Fact]
    public void HeaderOrderIsTiArAlThenTheRestThenOffsetAndTool()
    {
        Lyric lyric = LyricOf(SyncType.WordSync);
        lyric.Metadata["by"] = "someone";
        lyric.Metadata["al"] = "Album";
        lyric.Metadata["tool"] = "LDDC v0.9.0";
        lyric.Metadata["ti"] = "Title";
        lyric.Metadata["re"] = "x";
        lyric.Metadata["ve"] = "1.0";
        lyric.Metadata["length"] = "3:00";
        lyric.Metadata["ar"] = "Artist";
        lyric.Metadata["offset"] = "999";
        lyric.OffsetMs = 700;

        Assert.Equal(Lines(
                "[ti:Title]",
                "[ar:Artist]",
                "[al:Album]",
                "[by:someone]",
                "[length:3:00]",
                "[offset:700]",
                "[tool:Music Manager]",
                ""), StandardLrcFormatter.Format(lyric));
    }

    [Fact]
    public void BlankAndMalformedHeaderEntriesAreLeftOut()
    {
        Lyric lyric = LyricOf(SyncType.WordSync);
        lyric.Metadata["ti"] = "   ";
        lyric.Metadata["ar"] = null;
        lyric.Metadata["al"] = "";
        lyric.Metadata["by"] = "";
        lyric.Metadata["bad key"] = "x";
        lyric.Metadata["evil\r\n[tool:x"] = "x";
        lyric.Metadata[""] = "x";
        lyric.Metadata["x]"] = "x";

        Assert.Equal(Header, StandardLrcFormatter.Format(lyric));
    }

    [Fact]
    public void HeaderValuesStayOnOneLineAndAreTrimmed()
    {
        Lyric lyric = LyricOf(SyncType.WordSync);
        lyric.Metadata["ti"] = "  Two\r\nLines\nhere\r  ";

        Assert.True(StandardLrcFormatter.Format(lyric).StartsWith("[ti:Two Lines here]\r\n[offset:0]", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeOffsetIsWrittenAsIs()
    {
        Lyric lyric = LyricOf(SyncType.LineSync);
        lyric.OffsetMs = -250;

        Assert.True(StandardLrcFormatter.Format(lyric).Contains("[offset:-250]\r\n"));
    }

    // ---- line synced / lines without words / plain ----

    [Fact]
    public void LineSyncedLyricHasTimestampAndTextOnly()
    {
        Lyric lyric = LyricOf(SyncType.LineSync,
                Line(3631, 5738, "Careful, 'cause I Bite", "小心一些 因为我可不是好惹的"),
                Line(6000, 7000, "Second"),
                Line(8000, 9000, ""));
        lyric.OffsetMs = 700;

        Assert.Equal(Lines(
                "[offset:700]",
                "[tool:Music Manager]",
                "",
                "[00:03.631]Careful, 'cause I Bite",
                "[00:03.631]小心一些 因为我可不是好惹的",
                "[00:06.000]Second",
                "[00:08.000]"), StandardLrcFormatter.Format(lyric));
    }

    [Fact]
    public void LineWithoutWordsInsideAWordSyncedLyricBecomesOneWord()
    {
        LyricLine synced = Words(1000, 2000, W("a", 1000, 2000));
        LyricLine plain = Line(5000, 6000, "Plain line", "its translation");
        LyricLine backwards = Line(7000, 4000, "Backwards");
        LyricLine empty = Line(8000, 9000, "");

        Assert.Equal(Header + Lines(
                "[00:01.000]<00:01.000>a<00:02.000>",
                "[00:05.000]<00:05.000>Plain line<00:06.000>",
                "[00:05.000]<00:05.000>its translation<00:06.000>",
                "[00:07.000]<00:07.000>Backwards<00:07.000>",
                "[00:08.000]"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, synced, plain, backwards, empty)));
    }

    [Fact]
    public void ALyricWithAnyWordSyncedLineIsWordSyncedWhateverItsType()
    {
        LyricLine synced = Words(1000, 2000, W("a", 1000, 2000));
        LyricLine plain = Line(3000, 4000, "b");

        string expected = Header + Lines("[00:01.000]<00:01.000>a<00:02.000>", "[00:03.000]<00:03.000>b<00:04.000>");
        Assert.Equal(expected, StandardLrcFormatter.Format(LyricOf(SyncType.LineSync, synced, plain)));
        Assert.Equal(expected, StandardLrcFormatter.Format(LyricOf(SyncType.NoSync, synced, plain)));
    }

    [Fact]
    public void WordSyncTypeWithoutAnyWordsStillWritesWordLines()
    {
        Assert.Equal(Header + Lines("[00:01.000]<00:01.000>only text<00:02.000>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, Line(1000, 2000, "only text"))));
    }

    [Fact]
    public void NoSyncLyricIsJustThePlainText()
    {
        Lyric lyric = LyricOf(SyncType.NoSync, Line(0, 0, "Line one", "its translation"), Line(5, 5, "Line two"),
                Line(9, 9, ""));

        Assert.Equal(Header + Lines("Line one", "its translation", "Line two", ""),
                StandardLrcFormatter.Format(lyric));
    }

    // ---- odd data ----

    [Fact]
    public void NullAndEmptyInputNeverThrow()
    {
        Assert.Equal("", StandardLrcFormatter.Format(null));
        Assert.Equal(Header, StandardLrcFormatter.Format(new Lyric(SyncType.WordSync, null, null)));
        Assert.Equal(Header, StandardLrcFormatter.Format(LyricOf(SyncType.WordSync)));
        Assert.Equal(Header, StandardLrcFormatter.Format(LyricOf(SyncType.NoSync)));
        // Java passed a null sync type here; a C# enum has no null, so a value that is none of the three stands in for it
        Assert.Equal(Header, StandardLrcFormatter.Format(new Lyric((SyncType)(-1), new List<LyricLine>(), null)));
    }

    [Fact]
    public void NullEntriesAreSkipped()
    {
        LyricLine l = Words(1000, 3000, null, W(null, 1000, 1500), W("", 1000, 1500), W("a", 1000, 2000));
        List<LyricLine> list = new List<LyricLine>();
        list.Add(null);
        list.Add(l);
        list.Add(null);

        Assert.Equal(Header + Lines("[00:01.000]<00:01.000>a<00:02.000>"),
                StandardLrcFormatter.Format(new Lyric(SyncType.WordSync, list, null)));
    }

    [Fact]
    public void LineWhoseWordsAreAllEmptyFallsBackToItsText()
    {
        LyricLine l = new LyricLine("fallback", 1000, 2000);
        l.WordTimeStamps = new WordTimeStamp[] { W("", 1000, 1500), W(null, 1500, 2000) };

        Assert.Equal(Header + Lines("[00:01.000]<00:01.000>fallback<00:02.000>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
    }

    [Fact]
    public void LineWithNullTextAndNullWordArrayIsJustItsTag()
    {
        LyricLine l = new LyricLine(null, 1000, 2000);

        Assert.Equal(Header + Lines("[00:01.000]"), StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
        Assert.Equal(Header + Lines("[00:01.000]"), StandardLrcFormatter.Format(LyricOf(SyncType.LineSync, l)));
        Assert.Equal(Header + Lines(""), StandardLrcFormatter.Format(LyricOf(SyncType.NoSync, l)));
    }

    [Fact]
    public void LineBreaksInsideWordsAndTranslationsBecomeSpaces()
    {
        LyricLine l = Words(0, 100, W("a\r\nb ", 0, 50), W("c\nd\re", 50, 100));
        l.Translations = new List<string> { "x\r\ny", null, "", "  ", "z" };

        Assert.Equal(Header + Lines(
                "[00:00.000]<00:00.000>a b <00:00.050>c d e<00:00.100>",
                "[00:00.000]<00:00.000>x y<00:00.100>",
                "[00:00.000]<00:00.000>z<00:00.100>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
    }

    [Fact]
    public void BlankTranslationsAreSkippedInEveryMode()
    {
        LyricLine l = Line(1000, 2000, "text");
        l.Translations = new List<string> { " ", "\t" };

        Assert.Equal(Header + Lines("[00:01.000]text"), StandardLrcFormatter.Format(LyricOf(SyncType.LineSync, l)));
        Assert.Equal(Header + Lines("text"), StandardLrcFormatter.Format(LyricOf(SyncType.NoSync, l)));
    }

    [Fact]
    public void NegativeAndInvertedTimesNeverGoBackwards()
    {
        LyricLine negative = Words(-500, -100, W("x", -300, -200));
        LyricLine inverted = Words(2000, 1000, W("y", 2500, 2400));
        LyricLine overlapping = Words(1000, 4000, W("a ", 1000, 3000), W("b ", 2000, 4000), W("c", 1500, 1800));
        LyricLine outOfOrder = Words(1000, 3000, W("p ", 500, 2000), W("q", 2500, 1000));

        Assert.Equal(Header + Lines(
                "[00:00.000]<00:00.000>x<00:00.000>",
                "[00:02.000]<00:02.500>y<00:02.500>",
                "[00:01.000]<00:01.000>a <00:02.000>b <00:02.000>c<00:02.000>",
                "[00:01.000]<00:01.000>p <00:02.000><00:02.500>q<00:02.500>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, negative, inverted, overlapping, outOfOrder)));
    }

    [Fact]
    public void TranslationOfAnInvertedLineIsAZeroLengthSpan()
    {
        LyricLine l = Words(2000, 1000, W("y", 2000, 2100));
        l.AddTranslation("t");

        Assert.Equal(Header + Lines("[00:02.000]<00:02.000>y<00:02.100>", "[00:02.000]<00:02.000>t<00:02.000>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
    }

    [Fact]
    public void EveryTimestampInTheOutputIsNondecreasingWithinItsLine()
    {
        long[] values = { long.MinValue, -1, 0, 1, 999, 1000, 59999, 60000, 6000000, long.MaxValue };
        List<LyricLine> list = new List<LyricLine>();
        foreach (long a in values)
        {
            foreach (long b in values)
            {
                list.Add(Words(a, b, W("p ", b, a), W("q ", a, b), W("r", b, b)));
            }
        }
        string output = null;
        Exception error = Record.Exception(() => output = StandardLrcFormatter.Format(new Lyric(SyncType.WordSync, list, null)));
        Assert.Null(error);

        Regex stamp = new Regex(@"[\[<]([0-9]+):([0-9]{2})\.([0-9]{3})[\]>]");
        int checkedRows = 0;
        foreach (string row in JavaString.Split(output, "\r\n"))
        {
            if (!Regex.IsMatch(row, @"^\[[0-9]"))
            {
                continue; // header rows
            }
            long previous = -1;
            foreach (Match m in stamp.Matches(row))
            {
                long ms = (long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                        + long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) * 1000
                        + long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                Assert.True(ms >= previous, row);
                previous = ms;
            }
            checkedRows++;
        }
        Assert.Equal(values.Length * values.Length, checkedRows);
    }

    [Fact]
    public void MinutesBeyondOneHourKeepCounting()
    {
        Assert.Equal(Header + Lines("[100:00.000]<100:00.000>x<100:01.500>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, Words(6000000, 6001500, W("x", 6000000, 6001500)))));
    }

    [Fact]
    public void UnicodeIsWrittenUntouched()
    {
        LyricLine l = Words(0, 200, W("😀 ", 0, 100), W("漢字" + IdeographicSpace, 100, 200));

        Assert.Equal(Header + Lines("[00:00.000]<00:00.000>😀 <00:00.100>漢字" + IdeographicSpace + "<00:00.200>"),
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, l)));
    }

    // ---- formatTimestamp ----

    [Theory]
    [InlineData(0L, "00:00.000")]
    [InlineData(1L, "00:00.001")]
    [InlineData(999L, "00:00.999")]
    [InlineData(1000L, "00:01.000")]
    [InlineData(59999L, "00:59.999")]
    [InlineData(60000L, "01:00.000")]
    [InlineData(60001L, "01:00.001")]
    [InlineData(3599999L, "59:59.999")]
    [InlineData(3600000L, "60:00.000")]
    [InlineData(5999999L, "99:59.999")]
    [InlineData(6000000L, "100:00.000")]
    [InlineData(123456789L, "2057:36.789")]
    [InlineData(-1L, "00:00.000")]
    [InlineData(long.MinValue, "00:00.000")] // -9223372036854775808
    public void FormatsTimestamps(long ms, string expected)
    {
        Assert.Equal(expected, StandardLrcFormatter.FormatTimestamp(ms));
    }

    [Fact]
    public void HugeTimestampDoesNotThrow()
    {
        Assert.Equal("153722867280912:55.807", StandardLrcFormatter.FormatTimestamp(long.MaxValue));
    }

    // ---- isStandardWordSynced ----

    [Fact]
    public void RecognisesTheStandardSyntax()
    {
        Assert.True(StandardLrcFormatter.IsStandardWordSynced(
                "[ti:x]\r\n[offset:0]\r\n\r\n[00:00.583]<00:00.583>Dancefloor<00:01.494>\r\n"));
        Assert.True(StandardLrcFormatter.IsStandardWordSynced("[00:00.583]<00:00.583>x"));
        Assert.True(StandardLrcFormatter.IsStandardWordSynced(Bom + "[00:00.583]<00:00.583>x"));
        Assert.True(StandardLrcFormatter.IsStandardWordSynced("[100:00.000]<100:00.000>x"));
        Assert.True(StandardLrcFormatter.IsStandardWordSynced("[00:01.000]plain\n[00:02.000]<00:02.000>x"));
        Assert.True(StandardLrcFormatter.IsStandardWordSynced("[00:01.000]plain\r[00:02.000]<00:02.000>x"));
        Assert.True(StandardLrcFormatter.IsStandardWordSynced(
                StandardLrcFormatter.Format(LyricOf(SyncType.WordSync, Words(0, 10, W("a", 0, 10))))));
    }

    [Fact]
    public void RejectsEverythingElse()
    {
        Assert.False(StandardLrcFormatter.IsStandardWordSynced(null));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced(""));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[offset:700]\n[00:03.631]Careful, 'cause I Bite\n"));
        // two-digit fractions, as the earlier converter wrote them
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:03.63]<00:03.63>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:03.6310]<00:03.631>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:03.631]<00:03.63>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:03.63]<00:03.631>x"));
        // LDDC's line-synced flavour: a start tag and an end tag, no word tags
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:26.980]손 닿을 수 없는 저기 어딘가[00:34.700]\r\n"));
        // a word tag with no line tag, or a tag in the middle of a line
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("<00:03.631>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("lyrics [00:03.631]<00:03.631>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced(" [00:03.631]<00:03.631>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:03.631] <00:03.631>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[0:03.631]<0:03.631>x"));
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:03:631]<00:03:631>x"));
        // enhanced syntax with the tags on a translation-less line elsewhere in text, not at a line start
        Assert.False(StandardLrcFormatter.IsStandardWordSynced("[00:03.631]x [00:04.000]<00:04.000>y"));
    }
}
