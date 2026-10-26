using System.Runtime.ExceptionServices;
using System.Text;
using MusicManager.DataAccess;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.Tests.DataAccess;

public class HmrcParserTest
{
    // spelled as numbers so the invisible characters cannot get lost from the source
    private const char Bom = (char)0xFEFF;
    private const char IdeographicSpace = (char)0x3000;

    private static Lyric Parse(string text)
    {
        return HmrcParser.Parse(text, "song.hmrc");
    }

    private static T AssertTimeoutPreemptively<T>(TimeSpan timeout, Func<T> action)
    {
        Task<T> task = Task.Run(action);
        bool finished;
        try
        {
            finished = task.Wait(timeout);
        }
        catch (AggregateException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            return default;
        }
        Assert.True(finished, "execution timed out after " + timeout);
        return task.Result;
    }

    /// <summary>"text@start-end|text@start-end" so a whole line of words reads as one string</summary>
    private static string Words(LyricLine line)
    {
        StringBuilder sb = new StringBuilder();
        foreach (WordTimeStamp w in line.WordTimeStamps)
        {
            if (sb.Length > 0)
            {
                sb.Append('|');
            }
            sb.Append(w.Word).Append('@').Append(w.StartTimeMs).Append('-').Append(w.EndTimeMs);
        }
        return sb.ToString();
    }

    private static bool HasWarning(Lyric lyric, string part)
    {
        return lyric.Warnings.Any(w => w.Contains(part));
    }

    // ---- happy path, taken from the real library ----

    [Fact]
    public void ParsesRealLinesWithTranslationTail()
    {
        Lyric lyric = Parse("[offset:0]\n"
            + "[379,436]<0,168>Bite - <168,137>aespa (<305,131>에스파)<436,0>^TME享有本翻译作品的著作权\n"
            + "[815,268]<0,134>词：주원 (<134,134>Joowon)/Zaya(153/Joombas)\n"
            + "[3631,2107]<0,594>Careful, <594,255>'cause <849,380>I <1229,878>Bite<2107,0>^小心一些 因为我可不是好惹的\n");

        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        Assert.Equal("song.hmrc", lyric.Path);
        Assert.Equal(0L, lyric.OffsetMs);
        Assert.True(lyric.Warnings.Count == 0);
        Assert.Equal(3, lyric.Lines.Count);

        LyricLine first = lyric.Lines[0];
        Assert.Equal("Bite - aespa (에스파)", first.Line);
        Assert.Equal(379L, first.StartTimeMs);
        Assert.Equal(815L, first.EndTimeMs);
        Assert.Equal("Bite - @379-547|aespa (@547-684|에스파)@684-815", Words(first));
        Assert.Equal(new List<string> { "TME享有本翻译作品的著作权" }, first.Translations);

        LyricLine second = lyric.Lines[1];
        Assert.Equal("词：주원 (Joowon)/Zaya(153/Joombas)", second.Line);
        Assert.Equal("词：주원 (@815-949|Joowon)/Zaya(153/Joombas)@949-1083", Words(second));
        Assert.True(second.Translations.Count == 0);

        LyricLine third = lyric.Lines[2];
        Assert.Equal("Careful, 'cause I Bite", third.Line);
        Assert.Equal(3631L, third.StartTimeMs);
        Assert.Equal(5738L, third.EndTimeMs);
        Assert.Equal("Careful, @3631-4225|'cause @4225-4480|I @4480-4860|Bite@4860-5738", Words(third));
        Assert.Equal(new List<string> { "小心一些 因为我可不是好惹的" }, third.Translations);
    }

    [Fact]
    public void WordTextKeepsItsTrailingSpaceButTheLineDoesNot()
    {
        Lyric lyric = Parse("[0,100]<0,50>a <50,50>b \t ");
        LyricLine line = lyric.Lines[0];

        Assert.Equal("a b", line.Line);
        Assert.Equal("b \t ", line.WordTimeStamps[1].Word);
    }

    [Fact]
    public void KeepsLinesInFileOrderEvenWhenTheirTimesAreNot()
    {
        Lyric lyric = Parse("[offset:0]\n[5000,100]<0,100>late\n[1000,100]<0,100>early\n");

        Assert.Equal("late", lyric.Lines[0].Line);
        Assert.Equal("early", lyric.Lines[1].Line);
    }

    // ---- offset ----

    [Fact]
    public void NegativeOffsetIsTakenBackOutOfEveryTime()
    {
        // the real file with [offset:-250]: the writer had added -250 to the first line, which started at 0
        Lyric lyric = Parse("[offset:-250]\n[-250,5910]<0,100>Hi <100,100>there\n[5660,200]<0,200>Bye");

        LyricLine first = lyric.Lines[0];
        Assert.Equal(0L, first.StartTimeMs);
        Assert.Equal(5910L, first.EndTimeMs);
        Assert.Equal("Hi @0-100|there@100-200", Words(first));
        Assert.Equal("Bye@5910-6110", Words(lyric.Lines[1]));
        Assert.Equal(0L, lyric.OffsetMs); // the times already include the correction
        Assert.True(HasWarning(lyric, "[offset:-250]"));
        Assert.False(HasWarning(lyric, "below 0"));
    }

    [Fact]
    public void PositiveOffsetIsSubtracted()
    {
        Lyric lyric = Parse("[offset:250]\n[1250,100]<0,100>Hi");

        Assert.Equal("Hi@1000-1100", Words(lyric.Lines[0]));
        Assert.Equal(1000L, lyric.Lines[0].StartTimeMs);
    }

    [Fact]
    public void TimesThatFallBelowZeroAreClampedAndReported()
    {
        Lyric lyric = Parse("[offset:300]\n[100,200]<0,200>Hi");

        LyricLine line = lyric.Lines[0];
        Assert.Equal(0L, line.StartTimeMs);
        Assert.Equal(0L, line.EndTimeMs);
        Assert.Equal("Hi@0-0", Words(line));
        Assert.True(HasWarning(lyric, "below 0"));
    }

    [Fact]
    public void NegativeStartWithoutOffsetIsClamped()
    {
        Lyric lyric = Parse("[-250,5910]<0,100>Hi");

        Assert.Equal(0L, lyric.Lines[0].StartTimeMs);
        Assert.True(HasWarning(lyric, "below 0"));
    }

    [Fact]
    public void OffsetHeaderIsReadWhereverItIs()
    {
        Lyric lyric = Parse("[100,50]<0,50>a\n[offset:40]\n");

        Assert.Equal(60L, lyric.Lines[0].StartTimeMs);
    }

    [Fact]
    public void OffsetHeaderToleratesSpacesAndPlusSign()
    {
        Assert.Equal(60L, Parse("[offset: +40 ]\n[100,50]<0,50>a").Lines[0].StartTimeMs);
    }

    [Theory]
    [InlineData("[offset:abc]")]
    [InlineData("[offset:]")]
    [InlineData("[offset:1.5]")]
    [InlineData("[offset:--3]")]
    [InlineData("[offset:12ms]")]
    public void UnreadableOffsetIsIgnoredWithAWarning(string header)
    {
        Lyric lyric = Parse(header + "\n[100,50]<0,50>a");

        Assert.Equal(100L, lyric.Lines[0].StartTimeMs);
        Assert.True(HasWarning(lyric, "unreadable [offset:"));
    }

    [Fact]
    public void AbsurdOffsetDoesNotOverflow()
    {
        Lyric lyric = Parse("[offset:99999999999999999999]\n[100,50]<0,50>a");

        Assert.Equal(0L, lyric.Lines[0].StartTimeMs);
        Assert.True(HasWarning(lyric, "out-of-range"));
    }

    [Fact]
    public void OtherHeaderTagsGoToTheMetadata()
    {
        Lyric lyric = Parse("[TI:Song]\n[ar: Someone ]\n[offset:0]\n[100,50]<0,50>a");

        Assert.Equal("Song", lyric.Metadata["ti"]);
        Assert.Equal("Someone", lyric.Metadata["ar"]);
        Assert.False(lyric.Metadata.ContainsKey("offset"));
    }

    // ---- lines without word segments ----

    [Fact]
    public void PlainTextLineIsLineSynced()
    {
        Lyric lyric = Parse("[offset:0]\n[1000,500]plain text");

        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        LyricLine line = lyric.Lines[0];
        Assert.Equal("plain text", line.Line);
        Assert.Equal(1000L, line.StartTimeMs);
        Assert.Equal(1500L, line.EndTimeMs);
        Assert.False(line.IsWordSynced);
        Assert.NotNull(line.WordTimeStamps);
    }

    [Fact]
    public void PlainTextLineCanCarryATranslation()
    {
        Lyric lyric = Parse("[1000,500]plain<500,0>^tr");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("plain", line.Line);
        Assert.Equal(new List<string> { "tr" }, line.Translations);
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
    }

    [Fact]
    public void EmptyLineFromASpanlessParagraphIsKept()
    {
        // the real "[171633,0]" lines (ttml <p> without spans)
        Lyric lyric = Parse("[171633,0]\n[172000,500]<0,500>word");

        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        LyricLine empty = lyric.Lines[0];
        Assert.Equal("", empty.Line);
        Assert.Equal(171633L, empty.StartTimeMs);
        Assert.Equal(171633L, empty.EndTimeMs);
        Assert.False(empty.IsWordSynced);
    }

    [Fact]
    public void TranslationOnlyLineBecomesAnEmptyLineWithATranslation()
    {
        Lyric lyric = Parse("[1000,500]<500,0>^only a translation");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("", line.Line);
        Assert.Equal(new List<string> { "only a translation" }, line.Translations);
    }

    [Fact]
    public void TextBeforeTheFirstMarkerBecomesALeadingWord()
    {
        Lyric lyric = Parse("[100,200]intro <50,50>word");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("intro word", line.Line);
        Assert.Equal("intro @100-150|word@150-200", Words(line));
    }

    // ---- translation tail ----

    [Fact]
    public void OnlyTheFirstCaretSplitsTheTranslation()
    {
        Lyric lyric = Parse("[0,100]<0,100>Hi<100,0>^a^b ^c");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("Hi", line.Line);
        Assert.Equal(new List<string> { "a^b ^c" }, line.Translations);
    }

    [Fact]
    public void TranslationMayLookLikeMarkers()
    {
        Lyric lyric = Parse("[0,100]<0,100>Hi<100,0>^see <5,5> ok");

        Assert.Equal(new List<string> { "see <5,5> ok" }, lyric.Lines[0].Translations);
        Assert.Single(lyric.Lines[0].WordTimeStamps);
    }

    [Fact]
    public void EmptyOrBlankTranslationIsDropped()
    {
        Assert.True(Parse("[0,100]<0,100>Hi<100,0>^").Lines[0].Translations.Count == 0);
        Assert.True(Parse("[0,100]<0,100>Hi<100,0>^   ").Lines[0].Translations.Count == 0);
    }

    [Fact]
    public void ZeroDurationMarkerWithNoTextIsNotAWord()
    {
        Lyric lyric = Parse("[0,100]<0,100>Hi<100,0>");

        Assert.Single(lyric.Lines[0].WordTimeStamps);
    }

    [Fact]
    public void TranslationMarkerWithADurationIsStillNotAWord()
    {
        Lyric lyric = Parse("[0,100]<0,100>Hi<100,5>^tr");

        Assert.Equal("Hi@0-100", Words(lyric.Lines[0]));
        Assert.Equal(new List<string> { "tr" }, lyric.Lines[0].Translations);
    }

    // ---- text that only looks like markup ----

    [Fact]
    public void LessThanSignInsideTextIsText()
    {
        Lyric lyric = Parse("[100,100]<0,50>a < b <50,50>c");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("a < b c", line.Line);
        Assert.Equal("a < b @100-150|c@150-200", Words(line));
    }

    [Fact]
    public void StrayLessThanAsAWholeWordFromTheRealLibrary()
    {
        // "u + me = <3 - Olivia Rodrigo": the ttml span text was a bare "<"
        Lyric lyric = Parse("[0,6678]<0,742>u <742,742>+ <1484,742>me <2226,742>= <2968,742><<3710,742>3 "
            + "<4452,742>- <5194,742>Olivia <5936,742>Rodrigo<6678,0>^TME享有本翻译作品的著作权");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("u + me = <3 - Olivia Rodrigo", line.Line);
        Assert.Equal(9, line.WordTimeStamps.Length);
        WordTimeStamp stray = line.WordTimeStamps[4];
        Assert.Equal("<", stray.Word);
        Assert.Equal(2968L, stray.StartTimeMs);
        Assert.Equal(3710L, stray.EndTimeMs);
        Assert.Equal("3 ", line.WordTimeStamps[5].Word);
        Assert.Equal(new List<string> { "TME享有本翻译作品的著作权" }, line.Translations);
    }

    [Fact]
    public void OnlyAnExactIntCommaIntIsAMarker()
    {
        Lyric lyric = Parse("[0,100]<0,100>x<1.5,2>y<1, 2>z<a,b><,><1,2<1,>");

        Assert.Single(lyric.Lines[0].WordTimeStamps);
        Assert.Equal("x<1.5,2>y<1, 2>z<a,b><,><1,2<1,>", lyric.Lines[0].Line);
    }

    [Fact]
    public void MarkerLikeTextWithNegativeNumbersIsAMarker()
    {
        Lyric lyric = Parse("[100,100]<0,50>a<-5,10>b");

        Assert.Equal(2, lyric.Lines[0].WordTimeStamps.Length);
    }

    [Fact]
    public void EmptyTextSegmentsAreDropped()
    {
        // from TWICE - LAST WALTZ: "<6,-1>" is followed straight away by the next marker
        Lyric lyric = Parse("[5268,23]<0,6>Background <6,-1><5,6>vocals <11,6>by：<17,6>ejae");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("Background vocals by：ejae", line.Line);
        // "vocals" starts at +5 while "Background" runs to +6: the earlier word is cut short, not pushed later
        Assert.Equal("Background @5268-5273|vocals @5273-5279|by：@5279-5285|ejae@5285-5291", Words(line));
        Assert.True(HasWarning(lyric, "1 word(s)"));
    }

    [Fact]
    public void WhitespaceOnlyWordIsKept()
    {
        Lyric lyric = Parse("[0,200]<0,100>a<100,50> <150,50>b");

        LyricLine line = lyric.Lines[0];
        Assert.Equal(3, line.WordTimeStamps.Length);
        Assert.Equal("a b", line.Line);
    }

    // ---- XML entities left in the text by the old writer ----

    [Fact]
    public void XmlEntitiesAreTurnedBackIntoCharacters()
    {
        // from YUQI - Bonnie & Clyde: the hmrc says "Bonnie &amp; " where the song says "Bonnie & "
        Lyric lyric = Parse("[54523,1382]<0,630>Bonnie &amp; <630,752>Clyde<1382,0>^a &lt;b&gt; &quot;q&quot; &apos;x&apos; &amp;lt;");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("Bonnie & Clyde", line.Line);
        Assert.Equal("Bonnie & @54523-55153|Clyde@55153-55905", Words(line));
        Assert.Equal(new List<string> { "a <b> \"q\" 'x' &lt;" }, line.Translations);
        Assert.True(HasWarning(lyric, "XML entity"));
    }

    [Fact]
    public void EntitiesInPlainTextAndLeadingTextAreDecodedToo()
    {
        Assert.Equal("rock & roll", Parse("[0,100]rock &amp; roll").Lines[0].Line);
        Assert.Equal("I <3 you", Parse("[0,100]<0,50>I &lt;3 you").Lines[0].Line);
        Assert.Equal("a & b", Parse("[0,100]a &amp; <50,50>b").Lines[0].Line);
    }

    [Fact]
    public void BareAmpersandsAndUnknownEntitiesAreLeftAlone()
    {
        Lyric lyric = Parse("[0,100]<0,50>Kick & snare &nbsp; &amp <50,50>&#38; &lt");

        Assert.Equal("Kick & snare &nbsp; &amp &#38; &lt", lyric.Lines[0].Line);
        Assert.True(lyric.Warnings.Count == 0);
    }

    [Fact]
    public void ParsedHmrcRendersAsTheStandardLrc()
    {
        Lyric lyric = Parse("[offset:0]\n"
            + "[379,436]<0,168>Bite - <168,137>aespa (<305,131>에스파)<436,0>^TME享有本翻译作品的著作权\n"
            + "[815,268]<0,134>词：주원 (<134,134>Joowon)/Zaya(153/Joombas)\n");

        Assert.Equal("[offset:0]\r\n[tool:Music Manager]\r\n\r\n"
            + "[00:00.379]<00:00.379>Bite - <00:00.547>aespa (<00:00.684>에스파)<00:00.815>\r\n"
            + "[00:00.379]<00:00.379>TME享有本翻译作品的著作权<00:00.815>\r\n"
            + "[00:00.815]<00:00.815>词：주원 (<00:00.949>Joowon)/Zaya(153/Joombas)<00:01.083>\r\n",
            StandardLrcFormatter.Format(lyric));
    }

    // ---- messy timing ----

    [Fact]
    public void WordsBeforeTheirLineOrBeforeEachOtherAreStraightenedOut()
    {
        // from G.E.M. - 两个你: negative durations and starts before the line
        Lyric lyric = Parse("[188060,5825]<0,-219>版<-219,110>权<-109,36>所<-73,36>有<-37,50>x");

        LyricLine line = lyric.Lines[0];
        Assert.Equal("版权所有x", line.Line);
        long previousStart = line.StartTimeMs;
        foreach (WordTimeStamp w in line.WordTimeStamps)
        {
            Assert.True(w.StartTimeMs >= previousStart);
            Assert.True(w.EndTimeMs >= w.StartTimeMs);
            previousStart = w.StartTimeMs;
        }
        Assert.Equal(188060L, line.WordTimeStamps[1].StartTimeMs);
        Assert.True(HasWarning(lyric, "adjusted"));
    }

    [Fact]
    public void NegativeDurationIsAZeroLengthWord()
    {
        Lyric lyric = Parse("[100,100]<0,-30>a<10,20>b");

        Assert.Equal("a@100-100|b@110-130", Words(lyric.Lines[0]));
    }

    [Fact]
    public void NegativeDurationNearTheStartIsNotReportedAsBelowZero()
    {
        Lyric lyric = Parse("[10,100]<0,-30>a");

        Assert.Equal("a@10-10", Words(lyric.Lines[0]));
        Assert.True(lyric.Warnings.Count == 0);
    }

    [Fact]
    public void WordThatStartsBeforeAnEarlierWordIsPushedForward()
    {
        // c is written to start at 1200 but b already started at 1300
        Lyric lyric = Parse("[1000,500]<0,100>a <300,100>b <200,100>c");

        Assert.Equal("a @1000-1100|b @1300-1300|c@1300-1300", Words(lyric.Lines[0]));
    }

    [Fact]
    public void NegativeLineDurationGivesAZeroLengthLine()
    {
        Lyric lyric = Parse("[100,-50]<0,10>a");

        Assert.Equal(100L, lyric.Lines[0].StartTimeMs);
        Assert.Equal(100L, lyric.Lines[0].EndTimeMs);
    }

    // ---- numbers that do not fit ----

    [Fact]
    public void OverflowingLineStartIsClampedNotFatal()
    {
        Lyric lyric = Parse("[99999999999999999999,5]<0,5>x");

        Assert.Equal((long)int.MaxValue, lyric.Lines[0].StartTimeMs);
        Assert.True(HasWarning(lyric, "out-of-range"));
    }

    [Fact]
    public void OverflowingWordNumbersAreClampedToo()
    {
        Lyric lyric = Parse("[100,50]<99999999999999999999,5>x<0,99999999999999999999>y");

        LyricLine line = lyric.Lines[0];
        Assert.Equal(2, line.WordTimeStamps.Length);
        Assert.Equal(100L + int.MaxValue, line.WordTimeStamps[0].StartTimeMs);
        Assert.True(line.WordTimeStamps[1].EndTimeMs >= line.WordTimeStamps[1].StartTimeMs);
        Assert.True(HasWarning(lyric, "out-of-range"));
    }

    [Fact]
    public void HugeNegativeStartEndsUpAtZero()
    {
        Lyric lyric = Parse("[-99999999999999999999,5]<0,5>x");

        Assert.Equal(0L, lyric.Lines[0].StartTimeMs);
    }

    [Fact]
    public void JustOverIntRangeIsClamped()
    {
        Lyric lyric = Parse("[2147483648,1]<0,1>x");

        Assert.Equal((long)int.MaxValue, lyric.Lines[0].StartTimeMs);
    }

    [Fact]
    public void LeadingZerosAreNotAnOverflow()
    {
        Lyric lyric = Parse("[000000000000000000100,0050]<0,00050>x");

        Assert.Equal("x@100-150", Words(lyric.Lines[0]));
        Assert.True(lyric.Warnings.Count == 0);
    }

    // ---- file level quirks ----

    [Fact]
    public void ToleratesBomCrlfLoneCrBlankAndPaddedLines()
    {
        Lyric crlf = Parse(Bom + "[offset:0]\r\n\r\n[100,50]<0,50>a\r\n   \r\n\t\r\n[200,50]<0,50>b\r\n");
        Assert.Equal(2, crlf.Lines.Count);
        Assert.True(crlf.Warnings.Count == 0);

        Lyric cr = Parse("[offset:0]\r[100,50]<0,50>a\r[200,50]<0,50>b");
        Assert.Equal(2, cr.Lines.Count);
        Assert.Equal("b", cr.Lines[1].Line);
    }

    [Fact]
    public void ToleratesSpacesInsideTheLineHead()
    {
        Lyric lyric = Parse("  [ 100 , 50 ]<0,50>x");

        Assert.Equal("x@100-150", Words(lyric.Lines[0]));
    }

    [Fact]
    public void UnreadableLinesAreSkippedWithAWarning()
    {
        Lyric lyric = Parse("[offset:0]\nrandom garbage\n[100,50]<0,50>a\n[bad\n[00:12.30]lrc style\n[200,50]<0,50>b");

        Assert.Equal(2, lyric.Lines.Count);
        Assert.True(HasWarning(lyric, "3 unreadable line(s) skipped (first at line 2)"));
    }

    [Fact]
    public void TruncatedInputKeepsWhatWasReadable()
    {
        Lyric lyric = Parse("[100,50]<0,50>a\n[200,50]<0,5");

        Assert.Equal(2, lyric.Lines.Count);
        Assert.Equal("a", lyric.Lines[0].Line);
        Assert.Equal("<0,5", lyric.Lines[1].Line); // half a marker is just text
        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
    }

    [Fact]
    public void UnicodeSurvives()
    {
        Lyric lyric = Parse("[0,300]<0,100>😀 <100,100>漢字 <200,100>ñandú\t" + IdeographicSpace);

        LyricLine line = lyric.Lines[0];
        Assert.Equal("😀 漢字 ñandú", line.Line);
        Assert.Equal("😀 ", line.WordTimeStamps[0].Word);
        Assert.Equal("ñandú\t" + IdeographicSpace, line.WordTimeStamps[2].Word);
    }

    [Fact]
    public void NullPathIsAllowed()
    {
        Lyric lyric = HmrcParser.Parse("[0,10]<0,10>a", null);

        Assert.Null(lyric.Path);
    }

    // ---- nothing usable ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData("\uFEFF")]
    [InlineData("\uFEFF\uFEFF\n")]
    [InlineData("[offset:0]")]
    [InlineData("[offset:0]\n")]
    [InlineData("garbage")]
    [InlineData("[100,")]
    [InlineData("[ti:x]\nnot lyrics")]
    public void ThrowsWhenNoLineAtAllCanBeRead(string text)
    {
        LyricParseException e = Assert.Throws<LyricParseException>(() => Parse(text));

        Assert.Contains("song.hmrc", e.Message);
    }

    [Fact]
    public void ThrowsOnNullText()
    {
        Assert.Throws<LyricParseException>(() => HmrcParser.Parse(null, "x.hmrc"));
    }

    // ---- size ----

    [Fact]
    public void HugeInputsStayFast()
    {
        StringBuilder big = new StringBuilder("[offset:0]\n");
        for (int line = 0; line < 3000; line++)
        {
            big.Append('[').Append(line * 100).Append(",100]");
            for (int w = 0; w < 10; w++)
            {
                big.Append('<').Append(w * 10).Append(",10>w").Append(w).Append(' ');
            }
            big.Append("<100,0>^translation ").Append(line).Append('\n');
        }
        Lyric lyric = AssertTimeoutPreemptively(TimeSpan.FromSeconds(10), () => Parse(big.ToString()));
        Assert.Equal(3000, lyric.Lines.Count);

        // one enormous line, and hostile runs of '<' and digits that must not blow up the marker scan
        string hostile = "[0,100]" + string.Concat(Enumerable.Repeat("<1", 50000)) + new string('<', 50000)
            + new string('9', 50000) + "x";
        Lyric lyric2 = AssertTimeoutPreemptively(TimeSpan.FromSeconds(10), () => Parse(hostile));
        Assert.Single(lyric2.Lines);
    }
}
