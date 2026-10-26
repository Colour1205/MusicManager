using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using MusicManager.Common;
using MusicManager.DataAccess;
using MusicManager.Entity;
using Xunit;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.Tests.DataAccess;

public class LrcParserTest
{
    private const long MaxTimeMs = 100L * 60 * 60 * 1000;

    // ---------------------------------------------------------------- helpers

    private static Lyric Parse(string text)
    {
        return LrcParser.Parse(text, "song.lrc");
    }

    private static string Join(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    /// <summary>xUnit's Assert.Equal takes no message; this one says which check of a helper or loop failed.</summary>
    private static void AssertEquals<T>(T expected, T actual, string message)
    {
        try
        {
            Assert.Equal(expected, actual);
        }
        catch (Xunit.Sdk.XunitException e)
        {
            throw new Xunit.Sdk.XunitException(message + ": " + e.Message);
        }
    }

    private static T DoesNotThrow<T>(Func<T> action, string message)
    {
        try
        {
            return action();
        }
        catch (Exception e)
        {
            throw new Xunit.Sdk.XunitException("unexpected exception for " + message + ": " + e);
        }
    }

    private static void AssertTimeoutPreemptively(TimeSpan timeout, Action action)
    {
        Task task = Task.Run(action);
        bool finished;
        try
        {
            finished = task.Wait(timeout);
        }
        catch (AggregateException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            return;
        }
        Assert.True(finished, "execution timed out after " + timeout);
    }

    private static string Show(List<string> list)
    {
        return "[" + string.Join(", ", list) + "]";
    }

    private static void AssertWord(LyricLine line, int index, string text, long start, long end)
    {
        WordTimeStamp word = line.WordTimeStamps[index];
        AssertEquals(text, word.Word, "word " + index + " text");
        AssertEquals(start, word.StartTimeMs, "word " + index + " start");
        AssertEquals(end, word.EndTimeMs, "word " + index + " end");
    }

    private static void AssertLine(LyricLine line, string text, long start, long end)
    {
        AssertEquals(text, line.Line, "line text");
        AssertEquals(start, line.StartTimeMs, "line start of '" + text + "'");
        AssertEquals(end, line.EndTimeMs, "line end of '" + text + "'");
    }

    private static bool Warned(Lyric lyric, string fragment)
    {
        return lyric.Warnings.Any(w => w.Contains(fragment));
    }

    private static string Num(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>the whole structure as text, so two parses can be compared with one Assert.Equal</summary>
    private static string Dump(Lyric lyric)
    {
        StringBuilder output = new StringBuilder();
        output.Append(lyric.GetSyncType()).Append(" offset=").Append(Num(lyric.OffsetMs)).Append(" meta={")
            .Append(string.Join(", ", lyric.Metadata.Select(h => h.Key + "=" + h.Value))).Append("}\n");
        foreach (LyricLine line in lyric.Lines)
        {
            output.Append(Num(line.StartTimeMs)).Append('-').Append(Num(line.EndTimeMs)).Append(" '")
                .Append(line.Line).Append('\'');
            foreach (WordTimeStamp word in line.WordTimeStamps)
            {
                output.Append(" {").Append(word.Word).Append('@').Append(Num(word.StartTimeMs)).Append('-')
                    .Append(Num(word.EndTimeMs)).Append('}');
            }
            foreach (string translation in line.Translations)
            {
                output.Append(" T'").Append(translation).Append('\'');
            }
            output.Append('\n');
        }
        return output.ToString();
    }

    private static string Stamp(long ms)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", ms / 60000, ms / 1000 % 60, ms % 1000);
    }

    /// <summary>minimal stand-in for the real formatter: the standard syntax, translations as one-word lines</summary>
    private static string Format(Lyric lyric)
    {
        StringBuilder output = new StringBuilder();
        foreach (KeyValuePair<string, string> header in lyric.Metadata)
        {
            output.Append('[').Append(header.Key).Append(':').Append(header.Value).Append("]\n");
        }
        output.Append("[offset:").Append(Num(lyric.OffsetMs)).Append("]\n\n");
        foreach (LyricLine line in lyric.Lines)
        {
            output.Append('[').Append(Stamp(line.StartTimeMs)).Append(']');
            if (line.IsWordSynced)
            {
                WordTimeStamp[] words = line.WordTimeStamps;
                for (int i = 0; i < words.Length; i++)
                {
                    output.Append('<').Append(Stamp(words[i].StartTimeMs)).Append('>').Append(words[i].Word);
                    bool gap = i + 1 < words.Length && words[i + 1].StartTimeMs != words[i].EndTimeMs;
                    if (i + 1 == words.Length || gap)
                    {
                        output.Append('<').Append(Stamp(words[i].EndTimeMs)).Append('>');
                    }
                }
            }
            else
            {
                output.Append(line.Line);
            }
            output.Append('\n');
            foreach (string translation in line.Translations)
            {
                output.Append('[').Append(Stamp(line.StartTimeMs)).Append(']');
                if (line.IsWordSynced)
                {
                    output.Append('<').Append(Stamp(line.StartTimeMs)).Append('>').Append(translation).Append('<')
                        .Append(Stamp(line.EndTimeMs)).Append('>');
                }
                else
                {
                    output.Append(translation);
                }
                output.Append('\n');
            }
        }
        return output.ToString();
    }

    private static readonly string Reference = Join(
        "[ti:Cobra (Explicit)]",
        "[ar:LISA (리사)]",
        "[al:PRESS PLAY (Explicit)]",
        "[offset:0]",
        "[tool:LDDC v0.9.0 https://github.com/chenmozhijin/LDDC]",
        "",
        "[00:00.000]<00:00.000>Cobra <00:00.011>(Explicit) - <00:00.022>LISA<00:00.033>",
        "[00:00.000]<00:00.000>TME享有本翻译作品的著作权<00:00.033>",
        "[00:00.583]<00:00.583>Dancefloor<00:01.494>",
        "[00:00.583]<00:00.583>舞池<00:01.494>",
        "[00:01.494]<00:01.494>Tempo<00:02.513>",
        "[00:01.494]<00:01.494>节奏<00:02.513>",
        "[00:09.740]<00:09.740>(Yeah) <00:09.963>We <00:10.292><00:10.519>do <00:10.837>what <00:11.213>want<00:11.695>",
        "[00:09.740]<00:09.740>(是的) 我们做想做的事<00:11.695>",
        "[01:03.232]<01:03.232>Girl<01:03.499>",
        "[01:03.232]<01:03.232>姐妹<01:03.499>",
        "");

    // ---------------------------------------------------------------- the standard (LDDC) syntax

    private void AssertReference(Lyric lyric)
    {
        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        Assert.Equal("song.lrc", lyric.Path);
        Assert.Equal(new List<string> { "ti", "ar", "al", "tool" }, lyric.Metadata.Keys.ToList());
        Assert.Equal("Cobra (Explicit)", lyric.Metadata["ti"]);
        Assert.Equal("LISA (리사)", lyric.Metadata["ar"]);
        Assert.Equal("PRESS PLAY (Explicit)", lyric.Metadata["al"]);
        Assert.Equal("LDDC v0.9.0 https://github.com/chenmozhijin/LDDC", lyric.Metadata["tool"]);
        Assert.Equal(0L, lyric.OffsetMs);
        Assert.True(lyric.Warnings.Count == 0, Show(lyric.Warnings));

        List<LyricLine> lines = lyric.Lines;
        Assert.Equal(5, lines.Count);

        LyricLine credit = lines[0];
        AssertLine(credit, "Cobra (Explicit) - LISA", 0, 33);
        Assert.Equal(3, credit.WordTimeStamps.Length);
        AssertWord(credit, 0, "Cobra ", 0, 11);
        AssertWord(credit, 1, "(Explicit) - ", 11, 22);
        AssertWord(credit, 2, "LISA", 22, 33);
        Assert.Equal(new List<string> { "TME享有本翻译作品的著作权" }, credit.Translations);

        LyricLine dancefloor = lines[1];
        AssertLine(dancefloor, "Dancefloor", 583, 1494);
        Assert.Single(dancefloor.WordTimeStamps);
        AssertWord(dancefloor, 0, "Dancefloor", 583, 1494);
        Assert.Equal(new List<string> { "舞池" }, dancefloor.Translations);

        AssertLine(lines[2], "Tempo", 1494, 2513);
        Assert.Equal(new List<string> { "节奏" }, lines[2].Translations);

        // "We <10.292><10.519>do": the empty tag pair is a gap, not an empty word
        LyricLine yeah = lines[3];
        AssertLine(yeah, "(Yeah) We do what want", 9740, 11695);
        Assert.Equal(5, yeah.WordTimeStamps.Length);
        AssertWord(yeah, 0, "(Yeah) ", 9740, 9963);
        AssertWord(yeah, 1, "We ", 9963, 10292);
        AssertWord(yeah, 2, "do ", 10519, 10837);
        AssertWord(yeah, 3, "what ", 10837, 11213);
        AssertWord(yeah, 4, "want", 11213, 11695);
        Assert.Equal(new List<string> { "(是的) 我们做想做的事" }, yeah.Translations);

        AssertLine(lines[4], "Girl", 63232, 63499);
        Assert.Equal(new List<string> { "姐妹" }, lines[4].Translations);
    }

    [Fact]
    public void ParsesTheReferenceFormatWithLfLineEndings()
    {
        AssertReference(Parse(Reference));
    }

    [Fact]
    public void ParsesTheReferenceFormatWithCrlfLineEndings()
    {
        AssertReference(Parse(Reference.Replace("\n", "\r\n")));
    }

    [Fact]
    public void ParsesTheReferenceFormatWithLoneCrAndMixedLineEndings()
    {
        AssertReference(Parse(Reference.Replace("\n", "\r")));
        string mixed = JavaString.ReplaceFirst(JavaString.ReplaceFirst(Reference, "\n", "\r\n"), "\n", "\r")
            .Replace("\n\n", "\r\n\r\n");
        AssertReference(Parse(mixed));
    }

    [Fact]
    public void ParsesTheReferenceFormatWithBomAtStartAndGluedInTheMiddle()
    {
        AssertReference(Parse("\uFEFF" + Reference));
        AssertReference(Parse(Reference.Replace("[01:03.232]<01:03.232>Girl", "\uFEFF[01:03.232]<01:03.232>Girl")));
        AssertReference(Parse(Reference.Replace("[ar:", "\uFEFF\uFEFF[ar:")));
    }

    [Fact]
    public void ReferenceFormatRoundTripsThroughAFormatter()
    {
        Lyric first = Parse(Reference);
        string written = Format(first);
        // body (everything after the blank line) is byte-identical to the reference
        Assert.Equal(Reference.Substring(Reference.IndexOf("\n\n", StringComparison.Ordinal) + 2),
            written.Substring(written.IndexOf("\n\n", StringComparison.Ordinal) + 2));
        Assert.Equal(Dump(first), Dump(Parse(written)));
    }

    [Fact]
    public void WordTextKeepsItsOwnTrailingSpacesAndLineTextIsTrimmed()
    {
        Lyric lyric = Parse("[00:01.000]<00:01.000>Hello  <00:02.000>world <00:03.000>");
        LyricLine line = lyric.Lines[0];
        AssertWord(line, 0, "Hello  ", 1000, 2000);
        AssertWord(line, 1, "world ", 2000, 3000);
        Assert.Equal("Hello  world", line.Line);
    }

    [Fact]
    public void CjkAndEmojiWordsSurvive()
    {
        Lyric lyric = Parse("[00:01.000]<00:01.000>你好 <00:01.500>世界🎵<00:02.000>안녕<00:02.500>");
        LyricLine line = lyric.Lines[0];
        Assert.Equal("你好 世界🎵안녕", line.Line);
        AssertWord(line, 1, "世界🎵", 1500, 2000);
        AssertWord(line, 2, "안녕", 2000, 2500);
    }

    // ---------------------------------------------------------------- the old line-synced converter output

    [Fact]
    public void ParsesLineSyncedLinesWithTranslationsOnTheNextLine()
    {
        Lyric lyric = Parse(Join(
            "[offset:700]",
            "[00:03.631]Careful, 'cause I Bite",
            "[00:03.631]小心一些 因为我可不是好惹的",
            "[00:05.738]Next line",
            "[00:05.738]下一行",
            "[00:09.00]Last"));
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        Assert.Equal(700L, lyric.OffsetMs);
        Assert.True(lyric.Metadata.Count == 0);
        Assert.Equal(3, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "Careful, 'cause I Bite", 3631, 5738);
        Assert.Equal(new List<string> { "小心一些 因为我可不是好惹的" }, lyric.Lines[0].Translations);
        AssertLine(lyric.Lines[1], "Next line", 5738, 9000);
        Assert.Equal(new List<string> { "下一行" }, lyric.Lines[1].Translations);
        AssertLine(lyric.Lines[2], "Last", 9000, 9000);
        foreach (LyricLine line in lyric.Lines)
        {
            Assert.False(line.IsWordSynced);
            Assert.NotNull(line.WordTimeStamps);
        }
    }

    [Fact]
    public void LineEndIsTheNextDifferentStartEvenAcrossAWordTimedSameTimeLine()
    {
        // the second line has 2 timed words, so it is a real line and not a translation
        Lyric lyric = Parse(Join(
            "[00:01.00]plain",
            "[00:01.00]<00:01.00>two <00:01.50>words<00:02.00>",
            "[00:04.00]after"));
        Assert.Equal(3, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "plain", 1000, 4000);
        AssertLine(lyric.Lines[1], "two words", 1000, 2000);
        AssertLine(lyric.Lines[2], "after", 4000, 4000);
        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
    }

    // ---------------------------------------------------------------- legacy bracket word syntax

    [Fact]
    public void ParsesLegacyBracketWordLines()
    {
        Lyric lyric = Parse("[00:03.631]Careful, [00:04.225]'cause [00:04.480]I [00:04.860]Bite[00:05.738]");
        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        LyricLine line = lyric.Lines[0];
        AssertLine(line, "Careful, 'cause I Bite", 3631, 5738);
        Assert.Equal(4, line.WordTimeStamps.Length);
        AssertWord(line, 0, "Careful, ", 3631, 4225);
        AssertWord(line, 1, "'cause ", 4225, 4480);
        AssertWord(line, 2, "I ", 4480, 4860);
        AssertWord(line, 3, "Bite", 4860, 5738);
    }

    [Fact]
    public void EnhancedLineWhoseFirstWordHasNoTagStartsAtTheLineTime()
    {
        Lyric lyric = Parse("[00:03.631]Careful, <00:04.225>'cause <00:04.480>I<00:05.000>");
        LyricLine line = lyric.Lines[0];
        AssertLine(line, "Careful, 'cause I", 3631, 5000);
        AssertWord(line, 0, "Careful, ", 3631, 4225);
        AssertWord(line, 1, "'cause ", 4225, 4480);
        AssertWord(line, 2, "I", 4480, 5000);
    }

    [Fact]
    public void LineWithoutFinalTagEndsWhereTheNextLineStarts()
    {
        Lyric lyric = Parse(Join(
            "[00:01.000]<00:01.000>Hello <00:01.500>world",
            "[00:03.000]<00:03.000>Next <00:03.500>one",
            "[00:05.000]<00:05.000>Very <00:05.250>last"));
        LyricLine first = lyric.Lines[0];
        AssertLine(first, "Hello world", 1000, 3000);
        AssertWord(first, 1, "world", 1500, 3000);
        AssertLine(lyric.Lines[1], "Next one", 3000, 5000);
        // nothing follows the last line, so its last word is zero-length
        AssertLine(lyric.Lines[2], "Very last", 5000, 5250);
        AssertWord(lyric.Lines[2], 1, "last", 5250, 5250);
    }

    [Fact]
    public void AllThreeSyntaxesCanShareOneFile()
    {
        Lyric lyric = Parse(Join(
            "[00:01.00]plain line",
            "[00:03.00]a [00:03.50]b[00:04.00]",
            "[00:05.00]<00:05.00>c <00:05.50>d<00:06.00>"));
        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        Assert.False(lyric.Lines[0].IsWordSynced);
        AssertLine(lyric.Lines[0], "plain line", 1000, 3000);
        AssertLine(lyric.Lines[1], "a b", 3000, 4000);
        AssertLine(lyric.Lines[2], "c d", 5000, 6000);
        AssertWord(lyric.Lines[2], 1, "d", 5500, 6000);
    }

    [Fact]
    public void LineWithOnlyAnEndTagIsOneWordSpanningTheLine()
    {
        Lyric lyric = Parse("[00:01.000]Whole line[00:02.500]");
        LyricLine line = lyric.Lines[0];
        Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        Assert.Single(line.WordTimeStamps);
        AssertWord(line, 0, "Whole line", 1000, 2500);
    }

    [Fact]
    public void ParsesLddcLineLevelOutputWithEndTagsAndSpacePaddedTranslations()
    {
        // real LDDC output for songs without word timing: "[start]text[end]", translation padded with a space
        string text = Join(
            "[ar:TWICE]",
            "[al:TEN: The Story Goes On]",
            "[ti:ATM (JIHYO)]",
            "[by:someone]",
            "[tool:LDDC v0.9.0 https://github.com/chenmozhijin/LDDC]",
            "",
            "[00:06.990]Mmmmm[00:19.590]",
            "[00:19.590]All the way up[00:21.920]",
            "[00:19.590] 一路向上 势不可挡[00:21.920]",
            "[00:21.920]No one does it like we do[00:23.610]",
            "[00:21.920] 无人能像我们这样[00:23.610]",
            "").Replace("\n", "\r\n");
        Lyric lyric = Parse(text);
        Assert.Equal(new List<string> { "ar", "al", "ti", "by", "tool" }, lyric.Metadata.Keys.ToList());
        Assert.Equal("TEN: The Story Goes On", lyric.Metadata["al"]);
        Assert.Equal(3, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "Mmmmm", 6990, 19590);
        AssertLine(lyric.Lines[1], "All the way up", 19590, 21920);
        AssertWord(lyric.Lines[1], 0, "All the way up", 19590, 21920);
        Assert.Equal(new List<string> { "一路向上 势不可挡" }, lyric.Lines[1].Translations);
        Assert.Equal(new List<string> { "无人能像我们这样" }, lyric.Lines[2].Translations);
        Assert.False(LrcParser.UsesEnhancedSyntax(text));
    }

    [Fact]
    public void LineStartingWithAWordTagInsteadOfALineTagUsesItAsTheStart()
    {
        Lyric lyric = Parse("<00:03.631>Careful, <00:04.225>'cause<00:05.000>");
        LyricLine line = lyric.Lines[0];
        AssertLine(line, "Careful, 'cause", 3631, 5000);
        AssertWord(line, 0, "Careful, ", 3631, 4225);
        AssertWord(line, 1, "'cause", 4225, 5000);
    }

    // ---------------------------------------------------------------- gap markers and odd tag placement

    [Fact]
    public void GapMarkerSetsThePreviousWordsEndInsteadOfCreatingAWord()
    {
        Lyric lyric = Parse("[00:09.000]<00:09.000>a <00:10.000><00:10.500>b<00:11.000>");
        LyricLine line = lyric.Lines[0];
        Assert.Equal(2, line.WordTimeStamps.Length);
        AssertWord(line, 0, "a ", 9000, 10000);
        AssertWord(line, 1, "b", 10500, 11000);
        Assert.Equal("a b", line.Line);
    }

    [Fact]
    public void MoreThanTwoTagsInARowKeepTheFirstAsEndAndTheLastAsStart()
    {
        Lyric lyric = Parse("[00:01.000]<00:01.000>a<00:02.000><00:02.100><00:03.000>b<00:04.000>");
        LyricLine line = lyric.Lines[0];
        Assert.Equal(2, line.WordTimeStamps.Length);
        AssertWord(line, 0, "a", 1000, 2000);
        AssertWord(line, 1, "b", 3000, 4000);
    }

    [Fact]
    public void SpaceOnlyTextBetweenTagsIsATimedWordOfItsOwn()
    {
        Lyric lyric = Parse("[00:09.000]<00:09.000>We<00:10.000> <00:10.500>do<00:11.000>");
        LyricLine line = lyric.Lines[0];
        Assert.Equal(3, line.WordTimeStamps.Length);
        AssertWord(line, 0, "We", 9000, 10000);
        AssertWord(line, 1, " ", 10000, 10500);
        AssertWord(line, 2, "do", 10500, 11000);
        Assert.Equal("We do", line.Line);
    }

    [Fact]
    public void RealLddcLineThatTimesTheSpacesBetweenCjkWordsRoundTrips()
    {
        string body = "[00:00.000]<00:00.000>自<00:00.433>由<00:00.866>的<00:01.299>你<00:01.732> <00:02.165>-<00:02.598> "
            + "<00:03.031>G<00:03.464>.<00:03.897>E<00:04.330>.<00:04.763>M<00:05.196>.<00:05.629>邓<00:06.062>紫"
            + "<00:06.495>棋<00:06.928>\n";
        Lyric lyric = Parse(body);
        LyricLine line = lyric.Lines[0];
        Assert.Equal(16, line.WordTimeStamps.Length);
        Assert.Equal("自由的你 - G.E.M.邓紫棋", line.Line);
        AssertWord(line, 3, "你", 1299, 1732);
        AssertWord(line, 4, " ", 1732, 2165);
        AssertWord(line, 5, "-", 2165, 2598);
        string written = Format(lyric);
        Assert.Equal(body, written.Substring(written.IndexOf("\n\n", StringComparison.Ordinal) + 2));
    }

    [Fact]
    public void WordsWithEmptyTextAreSkipped()
    {
        Lyric lyric = Parse("[00:01.000]<00:01.000><00:01.200>a<00:02.000><00:02.000>");
        LyricLine line = lyric.Lines[0];
        Assert.Single(line.WordTimeStamps);
        AssertWord(line, 0, "a", 1200, 2000);
    }

    [Fact]
    public void WordTimesRunningBackwardsAreRepairedWithAWarning()
    {
        Lyric lyric = Parse("[00:05.000]<00:05.000>a <00:04.000>b<00:03.000>");
        LyricLine line = lyric.Lines[0];
        AssertWord(line, 0, "a ", 5000, 5000);
        AssertWord(line, 1, "b", 4000, 4000);
        Assert.Equal(5000L, line.EndTimeMs);
        Assert.True(Warned(lyric, "backwards"), Show(lyric.Warnings));
    }

    [Fact]
    public void LineWithOnlyTagsIsAnEmptyLineEndingAtItsLastTag()
    {
        Lyric lyric = Parse(Join("[00:01.000]<00:01.000><00:02.000>", "[00:03.000]x"));
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        AssertLine(lyric.Lines[0], "", 1000, 2000);
        Assert.False(lyric.Lines[0].IsWordSynced);
    }

    // ---------------------------------------------------------------- time tag formats

    [Theory]
    [InlineData("[00:03.631]", 3631L)]
    [InlineData("[00:03.63]", 3630L)]
    [InlineData("[00:03.6]", 3600L)]
    [InlineData("[00:03.5]", 3500L)]
    [InlineData("[00:03.05]", 3050L)]
    [InlineData("[00:03.053]", 3053L)]
    [InlineData("[00:03.005]", 3005L)]
    [InlineData("[00:03]", 3000L)]
    [InlineData("[00:03.]", 3000L)]
    [InlineData("[0:03.5]", 3500L)]
    [InlineData("[3:07.250]", 187250L)]
    [InlineData("[01:00.000]", 60000L)]
    [InlineData("[00:00.000]", 0L)]
    [InlineData("[100:00.000]", 6000000L)]
    [InlineData("[123:45.678]", 7425678L)]
    [InlineData("[00:75.250]", 75250L)]
    [InlineData("[00:03:50]", 3500L)]
    [InlineData("[00:03:05]", 3050L)]
    [InlineData("[00:03.6789]", 3678L)]
    [InlineData("[00:03.99999999999999999999]", 3999L)]
    [InlineData("[00:03,631]", 3631L)]
    [InlineData("[000000000000000000000000000001:00.00]", 60000L)]
    public void TimeTagFormats(string tag, long expectedMs)
    {
        Lyric lyric = Parse(tag + "text");
        AssertEquals(1, lyric.Lines.Count, tag);
        AssertEquals(expectedMs, lyric.Lines[0].StartTimeMs, tag);
        AssertEquals("text", lyric.Lines[0].Line, tag);
        Assert.True(lyric.Warnings.Count == 0, Show(lyric.Warnings));
    }

    [Theory]
    [InlineData("<00:03.631>", 3631L)]
    [InlineData("<00:03.6>", 3600L)]
    [InlineData("<3:07>", 187000L)]
    [InlineData("<00:03:50>", 3500L)]
    public void AngleTimeTagFormats(string tag, long expectedMs)
    {
        Lyric lyric = Parse("[00:00.000]" + tag + "word<10:00.000>");
        AssertEquals(expectedMs, lyric.Lines[0].WordTimeStamps[0].StartTimeMs, tag);
    }

    [Fact]
    public void AbsurdlyLargeNumbersAreClampedWithAWarningAndNeverOverflow()
    {
        string[] texts = { "[99999999999999999999:00.00]a", "[00:99999999999999999999.999]a",
            "[00:00.00]c<99999999999999999999999999:99999999999999999999999.999>" };
        long[] expectedStart = { MaxTimeMs, MaxTimeMs, 0 };
        long[] expectedEnd = { MaxTimeMs, MaxTimeMs, MaxTimeMs };
        for (int i = 0; i < texts.Length; i++)
        {
            Lyric lyric = Parse(texts[i]);
            AssertEquals(1, lyric.Lines.Count, texts[i]);
            AssertEquals(expectedStart[i], lyric.Lines[0].StartTimeMs, texts[i]);
            AssertEquals(expectedEnd[i], lyric.Lines[0].EndTimeMs, texts[i]);
            Assert.True(Warned(lyric, "clamped"), Show(lyric.Warnings));
        }
    }

    [Fact]
    public void HundredHoursExactlyIsStillValid()
    {
        Lyric lyric = Parse("[6000:00.000]a");
        Assert.Equal(MaxTimeMs, lyric.Lines[0].StartTimeMs);
        Assert.True(lyric.Warnings.Count == 0);
    }

    [Theory]
    [InlineData("[1:2x]")]
    [InlineData("[00:01.00")]
    [InlineData("<00:01.00]")]
    [InlineData("[:30]")]
    [InlineData("[00:]")]
    [InlineData("[a:01]")]
    [InlineData("[00:01.00>")]
    [InlineData("[-1:00.00]")]
    [InlineData("[00:-1.00]")]
    [InlineData("[0 0:01.00]")]
    [InlineData("[00 :01.00]")]
    [InlineData("[00:01.0.0]")]
    [InlineData("[00:01:02:03]")]
    [InlineData("[\uFF10\uFF10:\uFF10\uFF11]")] // full-width digits
    [InlineData("[]")]
    [InlineData("[")]
    public void NotATimeTagStaysText(string notATag)
    {
        Lyric lyric = Parse(notATag + "text");
        AssertEquals(SyncType.NoSync, lyric.GetSyncType(), notATag);
        Assert.Single(lyric.Lines);
        Assert.Equal(notATag + "text", lyric.Lines[0].Line);
    }

    // ---------------------------------------------------------------- header tags

    [Fact]
    public void HeadersAreStoredLowerCasedInFileOrderAndOffsetIsSeparate()
    {
        Lyric lyric = Parse(Join("[TI:Title]", "[Ar:Someone]", "[offset:12]", "[by:me]", "[00:01.00]x"));
        Assert.Equal(new List<string> { "ti", "ar", "by" }, lyric.Metadata.Keys.ToList());
        Assert.Equal("Title", lyric.Metadata["ti"]);
        Assert.Equal(12L, lyric.OffsetMs);
        Assert.False(lyric.Metadata.ContainsKey("offset"));
        Assert.Single(lyric.Lines);
    }

    [Fact]
    public void HeaderValuesMayContainBracketsColonsAndCjk()
    {
        Lyric lyric = Parse(Join(
            "[ti:Song [Live]: 日本語 <3]",
            "[ar:  Spaced Out  ]",
            "[by:]",
            "[al:a]b]"));
        Assert.Equal("Song [Live]: 日本語 <3", lyric.Metadata["ti"]);
        Assert.Equal("Spaced Out", lyric.Metadata["ar"]);
        Assert.Equal("", lyric.Metadata["by"]);
        Assert.Equal("a]b", lyric.Metadata["al"]);
        Assert.True(lyric.Lines.Count == 0);
    }

    [Fact]
    public void HeaderCanAlsoAppearAfterTheLyrics()
    {
        Lyric lyric = Parse(Join("[00:01.00]x", "[by:late]"));
        Assert.Equal("late", lyric.Metadata["by"]);
        Assert.Single(lyric.Lines);
    }

    [Fact]
    public void RepeatedHeaderKeepsTheLastValueInTheFirstPosition()
    {
        Lyric lyric = Parse(Join("[ti:one]", "[ar:x]", "[ti:two]"));
        Assert.Equal(new List<string> { "ti", "ar" }, lyric.Metadata.Keys.ToList());
        Assert.Equal("two", lyric.Metadata["ti"]);
    }

    [Fact]
    public void TimestampLinesAreNeverHeaders()
    {
        Lyric lyric = Parse("[00:01.00]hello");
        Assert.True(lyric.Metadata.Count == 0);
        Assert.Single(lyric.Lines);
    }

    [Fact]
    public void HeaderFollowedByTrailingTextIsPlainText()
    {
        Lyric lyric = Parse("[ti:x]junk");
        Assert.True(lyric.Metadata.Count == 0);
        Assert.Equal("[ti:x]junk", lyric.Lines[0].Line);
    }

    [Theory]
    [InlineData("[offset:700]", 700L)]
    [InlineData("[offset:+700]", 700L)]
    [InlineData("[offset:-250]", -250L)]
    [InlineData("[offset: 700 ]", 700L)]
    [InlineData("[OFFSET:5]", 5L)]
    [InlineData("[offset:0]", 0L)]
    [InlineData("[offset:-0]", 0L)]
    [InlineData("[offset:0000000000000000000000012]", 12L)]
    public void OffsetForms(string header, long expected)
    {
        Lyric lyric = Parse(header + "\n[00:01.00]x");
        Assert.Equal(expected, lyric.OffsetMs);
        Assert.True(lyric.Warnings.Count == 0, Show(lyric.Warnings));
    }

    [Theory]
    [InlineData("[offset:abc]")]
    [InlineData("[offset:]")]
    [InlineData("[offset:1.5]")]
    [InlineData("[offset:700ms]")]
    [InlineData("[offset:--5]")]
    [InlineData("[offset:+]")]
    [InlineData("[offset:7 00]")]
    [InlineData("[offset:\u0663]")] // an Arabic-Indic digit is not an ASCII digit
    public void GarbageOffsetIsZeroWithAWarning(string header)
    {
        Lyric lyric = Parse(header + "\n[00:01.00]x");
        AssertEquals(0L, lyric.OffsetMs, header);
        Assert.True(Warned(lyric, "offset"), header + " " + Show(lyric.Warnings));
        Assert.Single(lyric.Lines);
    }

    [Fact]
    public void HugeOffsetIsClamped()
    {
        Lyric lyric = Parse("[offset:-99999999999999999999999999]\n[00:01.00]x");
        Assert.Equal(-MaxTimeMs, lyric.OffsetMs);
        Assert.True(Warned(lyric, "offset"));
        Assert.Equal(MaxTimeMs, Parse("[offset:99999999999999999999999999]").OffsetMs);
    }

    [Fact]
    public void OffsetIsNotAppliedToTheTimes()
    {
        Lyric lyric = Parse("[offset:700]\n[00:01.000]x");
        Assert.Equal(1000L, lyric.Lines[0].StartTimeMs);
    }

    // ---------------------------------------------------------------- whitespace and encodings

    [Fact]
    public void TrailingWhitespaceBlankLinesAndIndentationAreIgnored()
    {
        Lyric lyric = Parse("  \t[00:01.00]  hello world \t \n\n   \n\t\n[00:02.00]second\u00A0\u3000\n\n\n");
        Assert.Equal(2, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "hello world", 1000, 2000);
        AssertLine(lyric.Lines[1], "second", 2000, 2000);
    }

    [Fact]
    public void ExoticWhitespaceIsTrimmedFromLineText()
    {
        Lyric lyric = Parse("[00:01.00]\u00A0\u3000\u2003hello\u3000\u200B\u00A0");
        Assert.Equal("hello", lyric.Lines[0].Line);
    }

    [Fact]
    public void TrailingWhitespaceAfterAFinalTagDoesNotCreateAWord()
    {
        Lyric lyric = Parse("[00:01.000]<00:01.000>a<00:02.000>   \u00A0");
        Assert.Single(lyric.Lines[0].WordTimeStamps);
        AssertWord(lyric.Lines[0], 0, "a", 1000, 2000);
    }

    [Fact]
    public void SpaceBetweenTheLineTagAndTheFirstWordTagIsIgnored()
    {
        Lyric lyric = Parse("[00:01.000] <00:01.000>a <00:02.000>b<00:03.000>");
        AssertWord(lyric.Lines[0], 0, "a ", 1000, 2000);
        Assert.Equal("a b", lyric.Lines[0].Line);
    }

    [Fact]
    public void ATaggedWordKeepsItsLeadingSpaceExactly()
    {
        // seen in a real LDDC file: "[00:17.303]<00:17.303> V12<00:18.792>"
        Lyric lyric = Parse("[00:17.303]<00:17.303> V12<00:18.792>");
        AssertWord(lyric.Lines[0], 0, " V12", 17303, 18792);
        Assert.Equal(" V12", lyric.Lines[0].Line);
    }

    [Fact]
    public void AnUntaggedFirstWordDoesNotKeepTheSpaceAfterTheLineTag()
    {
        Lyric lyric = Parse("[00:01.000]  Careful, <00:02.000>x<00:03.000>");
        AssertWord(lyric.Lines[0], 0, "Careful, ", 1000, 2000);
    }

    [Fact]
    public void ASingleWordTranslationKeepsItsExactText()
    {
        // seen in real LDDC files: a tab (and spaces) before the closing tag
        Lyric lyric = Parse(Join(
            "[00:01.000]<00:01.000>Hello<00:02.000>",
            "[00:01.000]<00:01.000> 你好\t  <00:02.000>"));
        Assert.Equal(new List<string> { " 你好\t  " }, lyric.Lines[0].Translations);
        // a line synced translation cannot carry trailing whitespace
        Assert.Equal(new List<string> { "b" }, Parse("[00:01.00]a\n[00:01.00]  b \t").Lines[0].Translations);
    }

    [Fact]
    public void NullEmptyAndBlankTextGiveAnEmptyNoSyncLyric()
    {
        foreach (string text in new string[] { null, "", "   ", "\n\n", "\r\n \r\n\t", "\uFEFF", "\uFEFF\uFEFF\n" })
        {
            Lyric lyric = LrcParser.Parse(text, "x.lrc");
            Assert.Equal(SyncType.NoSync, lyric.GetSyncType());
            Assert.True(lyric.Lines.Count == 0);
            Assert.True(lyric.Warnings.Count == 0);
            Assert.Equal("x.lrc", lyric.Path);
            Assert.Equal(0L, lyric.OffsetMs);
        }
        Assert.Null(LrcParser.Parse("", null).Path);
    }

    [Fact]
    public void OnlyHeadersGiveNoSyncWithMetadataAndNoLines()
    {
        Lyric lyric = Parse("[ti:x]\n[ar:y]\n[offset:5]");
        Assert.Equal(SyncType.NoSync, lyric.GetSyncType());
        Assert.True(lyric.Lines.Count == 0);
        Assert.Equal(2, lyric.Metadata.Count);
        Assert.Equal(5L, lyric.OffsetMs);
    }

    // ---------------------------------------------------------------- repeated lines

    [Fact]
    public void AdjacentTagsAtTheStartRepeatTheLine()
    {
        Lyric lyric = Parse(Join("[00:10.00][00:30.00]chorus", "[00:20.00]verse"));
        Assert.Equal(3, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "chorus", 10000, 20000);
        AssertLine(lyric.Lines[1], "verse", 20000, 30000);
        AssertLine(lyric.Lines[2], "chorus", 30000, 30000);
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
    }

    [Fact]
    public void RepeatTagsMayBeSeparatedBySpacesAndNeedNotBeInOrder()
    {
        Lyric lyric = Parse("[00:30.00] [00:10.00]\t[00:20.00]chorus");
        Assert.Equal(3, lyric.Lines.Count);
        Assert.Equal(new[] { 10000L, 20000L, 30000L }, new[] { lyric.Lines[0].StartTimeMs,
            lyric.Lines[1].StartTimeMs, lyric.Lines[2].StartTimeMs });
    }

    [Fact]
    public void RepeatedLinesKeepTheirTranslationsAndFileOrderOnTies()
    {
        Lyric lyric = Parse(Join(
            "[00:10.00][00:30.00]chorus",
            "[00:10.00][00:30.00]合唱",
            "[00:20.00]verse",
            "[00:20.00]诗"));
        Assert.Equal(3, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "chorus", 10000, 20000);
        Assert.Equal(new List<string> { "合唱" }, lyric.Lines[0].Translations);
        AssertLine(lyric.Lines[1], "verse", 20000, 30000);
        Assert.Equal(new List<string> { "诗" }, lyric.Lines[1].Translations);
        AssertLine(lyric.Lines[2], "chorus", 30000, 30000);
        Assert.Equal(new List<string> { "合唱" }, lyric.Lines[2].Translations);
    }

    [Fact]
    public void UntimedTextAfterARepeatedLineIsSharedByEveryCopy()
    {
        Lyric lyric = Parse(Join("[00:10.00][00:30.00]chorus", "translation"));
        Assert.Equal(new List<string> { "translation" }, lyric.Lines[0].Translations);
        Assert.Equal(new List<string> { "translation" }, lyric.Lines[1].Translations);
    }

    [Fact]
    public void RepeatedEnhancedLineShiftsItsWordTimes()
    {
        Lyric lyric = Parse("[00:10.00][00:30.00]<00:10.00>a <00:10.50>b<00:11.00>");
        LyricLine second = lyric.Lines[1];
        AssertLine(second, "a b", 30000, 31000);
        AssertWord(second, 0, "a ", 30000, 30500);
        AssertWord(second, 1, "b", 30500, 31000);
        AssertWord(lyric.Lines[0], 0, "a ", 10000, 10500);
    }

    [Fact]
    public void OutOfOrderFileIsNotSortedWithoutRepeatTags()
    {
        Lyric lyric = Parse(Join("[00:20.00]b", "[00:10.00]a", "[00:30.00]c"));
        Assert.Equal("b", lyric.Lines[0].Line);
        Assert.Equal("a", lyric.Lines[1].Line);
        // the next different start is earlier, so the end is raised to the start
        AssertLine(lyric.Lines[0], "b", 20000, 20000);
        AssertLine(lyric.Lines[1], "a", 10000, 30000);
        AssertLine(lyric.Lines[2], "c", 30000, 30000);
    }

    // ---------------------------------------------------------------- literal angle brackets in lyric text

    [Theory]
    [InlineData("I <3 you")]
    [InlineData("<(^o^)> hi")]
    [InlineData("a < b and c > d")]
    [InlineData("<3")]
    [InlineData("<>")]
    [InlineData("<<3>>")]
    [InlineData("x <12:> y")]
    [InlineData("<:30> z")]
    [InlineData("tags [Chorus] and [x2] stay")]
    [InlineData("<abc:def>")]
    [InlineData("3 <- 4 ->")]
    public void LiteralAnglesAndBracketsInLineSyncedTextStayText(string text)
    {
        Lyric lyric = Parse("[00:01.00]" + text);
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        Assert.Equal(text, lyric.Lines[0].Line);
        Assert.False(lyric.Lines[0].IsWordSynced);
    }

    [Fact]
    public void LiteralAnglesInsideWordSyncedTextStayInTheWords()
    {
        Lyric lyric = Parse("[00:01.000]<00:01.000>I <3 <00:02.000>you <(^o^)><00:03.000>");
        LyricLine line = lyric.Lines[0];
        Assert.Equal(2, line.WordTimeStamps.Length);
        AssertWord(line, 0, "I <3 ", 1000, 2000);
        AssertWord(line, 1, "you <(^o^)>", 2000, 3000);
    }

    // ---------------------------------------------------------------- translations

    [Fact]
    public void ThirdSameTimeLineIsAnotherTranslationOfTheSameLine()
    {
        Lyric lyric = Parse(Join(
            "[00:01.00]Hello",
            "[00:01.00]你好",
            "[00:01.00]annyeong",
            "[00:02.00]Bye"));
        Assert.Equal(2, lyric.Lines.Count);
        Assert.Equal(new List<string> { "你好", "annyeong" }, lyric.Lines[0].Translations);
        Assert.True(lyric.Lines[1].Translations.Count == 0);
    }

    [Fact]
    public void SameTimeLineWithTwoTimedWordsIsKeptAsItsOwnLine()
    {
        Lyric lyric = Parse(Join(
            "[00:01.000]<00:01.000>Hello<00:02.000>",
            "[00:01.000]<00:01.000>Oh <00:01.500>yeah<00:02.000>",
            "[00:01.000]<00:01.000>哦耶<00:02.000>"));
        Assert.Equal(2, lyric.Lines.Count);
        AssertLine(lyric.Lines[1], "Oh yeah", 1000, 2000);
        Assert.True(lyric.Lines[0].Translations.Count == 0);
        // a third same-time line follows the line it came after
        Assert.Equal(new List<string> { "哦耶" }, lyric.Lines[1].Translations);
    }

    [Fact]
    public void EmptySameTimeLineIsDropped()
    {
        Lyric lyric = Parse(Join("[00:01.00]Hello", "[00:01.00]", "[00:01.00]   ", "[00:02.00]Bye"));
        Assert.Equal(2, lyric.Lines.Count);
        Assert.True(lyric.Lines[0].Translations.Count == 0);
    }

    [Fact]
    public void ASameTimeLineAfterAnEmptyMarkerIsNotItsTranslation()
    {
        Lyric lyric = Parse(Join("[00:05.00]", "[00:05.00]Hello"));
        Assert.Equal(2, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "", 5000, 5000);
        AssertLine(lyric.Lines[1], "Hello", 5000, 5000);
    }

    [Fact]
    public void OnlyTheImmediatelyPrecedingLineCanBeTheParent()
    {
        Lyric lyric = Parse(Join("[00:01.00]a", "[00:02.00]b", "[00:01.00]c"));
        Assert.Equal(3, lyric.Lines.Count);
        foreach (LyricLine line in lyric.Lines)
        {
            Assert.True(line.Translations.Count == 0);
        }
    }

    [Fact]
    public void TranslationCanMixLineSyncedAndSingleWordSyncedLines()
    {
        Lyric lyric = Parse(Join(
            "[00:01.000]plain parent",
            "[00:01.000]<00:01.000>one span<00:02.000>",
            "[00:03.000]<00:03.000>timed <00:03.500>parent<00:04.000>",
            "[00:03.000]plain translation"));
        Assert.Equal(2, lyric.Lines.Count);
        Assert.Equal(new List<string> { "one span" }, lyric.Lines[0].Translations);
        Assert.Equal(new List<string> { "plain translation" }, lyric.Lines[1].Translations);
        Assert.True(lyric.Lines[1].IsWordSynced);
    }

    [Fact]
    public void EmptyTimedLineIsKeptAsAnInstrumentalMarker()
    {
        Lyric lyric = Parse(Join("[00:01.00]a", "[01:23.456]", "[01:30.000]b", "[02:00.000]"));
        Assert.Equal(4, lyric.Lines.Count);
        AssertLine(lyric.Lines[1], "", 83456, 90000);
        AssertLine(lyric.Lines[3], "", 120000, 120000);
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
    }

    // ---------------------------------------------------------------- untimed text

    [Fact]
    public void FileWithoutTimeTagsIsNoSyncWithOneLinePerNonEmptyLine()
    {
        Lyric lyric = Parse(Join("[offset:700]", "[ti:Song]", "  first line  ", "", "second line", "   ", "[Verse 1]"));
        Assert.Equal(SyncType.NoSync, lyric.GetSyncType());
        Assert.Equal(700L, lyric.OffsetMs);
        Assert.Equal("Song", lyric.Metadata["ti"]);
        Assert.Equal(3, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "first line", 0, 0);
        AssertLine(lyric.Lines[1], "second line", 0, 0);
        AssertLine(lyric.Lines[2], "[Verse 1]", 0, 0);
        foreach (LyricLine line in lyric.Lines)
        {
            Assert.False(line.IsWordSynced);
            Assert.NotNull(line.WordTimeStamps);
            Assert.True(line.Translations.Count == 0);
        }
        Assert.True(lyric.Warnings.Count == 0);
    }

    [Fact]
    public void SameTextRepeatedInAPlainFileIsNotMerged()
    {
        Lyric lyric = Parse(Join("la", "la", "la"));
        Assert.Equal(3, lyric.Lines.Count);
    }

    [Fact]
    public void UntimedTextAfterATimedLineBecomesItsTranslation()
    {
        Lyric lyric = Parse(Join("[00:01.00]hello", "你好", "[00:02.00]bye", "再见", "さようなら"));
        Assert.Equal(2, lyric.Lines.Count);
        Assert.Equal(new List<string> { "你好" }, lyric.Lines[0].Translations);
        Assert.Equal(new List<string> { "再见", "さようなら" }, lyric.Lines[1].Translations);
        Assert.True(Warned(lyric, "3 untimed"), Show(lyric.Warnings));
    }

    [Fact]
    public void UntimedTextBeforeTheFirstTimedLineIsKeptAtZeroAndNeverBecomesAParent()
    {
        Lyric lyric = Parse(Join("Some Title", "[00:00.00]First", "[00:02.00]Second"));
        Assert.Equal(3, lyric.Lines.Count);
        AssertLine(lyric.Lines[0], "Some Title", 0, 0);
        AssertLine(lyric.Lines[1], "First", 0, 2000);
        Assert.True(lyric.Lines[1].Translations.Count == 0);
        Assert.Equal(SyncType.LineSync, lyric.GetSyncType());
        Assert.True(Warned(lyric, "before the first timed line"), Show(lyric.Warnings));
    }

    // ---------------------------------------------------------------- sync type

    [Fact]
    public void SyncTypeFollowsTheMostDetailedLine()
    {
        Assert.Equal(SyncType.NoSync, Parse("just text").GetSyncType());
        Assert.Equal(SyncType.LineSync, Parse("[00:01.00]a\n[00:02.00]b").GetSyncType());
        Assert.Equal(SyncType.WordSync, Parse("[00:01.00]a\n[00:02.00]<00:02.00>b<00:03.00>").GetSyncType());
        Assert.Equal(SyncType.LineSync, Parse("[00:01.00]").GetSyncType());
    }

    // ---------------------------------------------------------------- usesEnhancedSyntax

    [Fact]
    public void DetectsTheStandardSyntax()
    {
        Assert.True(LrcParser.UsesEnhancedSyntax("[00:00.583]<00:00.583>Dancefloor<00:01.494>"));
        Assert.True(LrcParser.UsesEnhancedSyntax(Reference));
        Assert.True(LrcParser.UsesEnhancedSyntax(Reference.Replace("\n", "\r\n")));
        Assert.True(LrcParser.UsesEnhancedSyntax("\uFEFF[00:00.583]<00:00.583>x"));
        Assert.True(LrcParser.UsesEnhancedSyntax("  [00:00.583] \t<00:00.583>x"));
        Assert.True(LrcParser.UsesEnhancedSyntax("[0:1]<0:1>"));
        Assert.True(LrcParser.UsesEnhancedSyntax("[00:01:50]<00:01:50>x"));
        Assert.True(LrcParser.UsesEnhancedSyntax("[00:01.0]<00:01.0>"));
        // one enhanced line among legacy ones is enough
        Assert.True(LrcParser.UsesEnhancedSyntax("[00:01.00]plain\n[00:02.00]a [00:03.00]b[00:04.00]\n[00:05.00]<00:05.00>c"));
    }

    [Fact]
    public void DoesNotMistakeOtherSyntaxesForTheStandardOne()
    {
        Assert.False(LrcParser.UsesEnhancedSyntax(null));
        Assert.False(LrcParser.UsesEnhancedSyntax(""));
        Assert.False(LrcParser.UsesEnhancedSyntax("[ti:x]\n[offset:0]"));
        Assert.False(LrcParser.UsesEnhancedSyntax("[00:03.631]Careful, 'cause I Bite"));
        Assert.False(LrcParser.UsesEnhancedSyntax("[00:03.631]Careful, [00:04.225]'cause [00:04.860]Bite[00:05.738]"));
        Assert.False(LrcParser.UsesEnhancedSyntax("[00:03.631]I <3 you"));
        Assert.False(LrcParser.UsesEnhancedSyntax("[00:03.631]x<00:04.000>"));
        Assert.False(LrcParser.UsesEnhancedSyntax("<00:03.631>x"));
        Assert.False(LrcParser.UsesEnhancedSyntax("[00:03.631]<00:03.631"));
        Assert.False(LrcParser.UsesEnhancedSyntax("[00:03.631]<abc>"));
        Assert.False(LrcParser.UsesEnhancedSyntax("text [00:03.631]<00:03.631>x"));
    }

    // ---------------------------------------------------------------- warnings

    [Fact]
    public void WarningsNameTheLineAndAreCapped()
    {
        StringBuilder text = new StringBuilder("[offset:zzz]\n");
        for (int i = 0; i < 500; i++)
        {
            text.Append("[99999999999:00.00]x\n");
        }
        Lyric lyric = Parse(text.ToString());
        Assert.True(lyric.Warnings[0].StartsWith("line 1:", StringComparison.Ordinal), lyric.Warnings[0]);
        Assert.Equal(101, lyric.Warnings.Count);
        Assert.Equal("further warnings suppressed", lyric.Warnings[100]);
    }

    [Fact]
    public void CleanInputHasNoWarnings()
    {
        Assert.True(Parse(Reference).Warnings.Count == 0);
        Assert.True(Parse("[00:01.00]a\n[00:02.00]b").Warnings.Count == 0);
    }

    // ---------------------------------------------------------------- round trips

    [Fact]
    public void ParseFormatParseIsStableForEveryDialect()
    {
        string[] samples =
        {
            "[offset:700]\n[00:03.631]Careful\n[00:03.631]小心\n[00:05.738]Bite\n[00:08.000]",
            "[00:03.631]Careful, [00:04.225]'cause [00:04.480]I [00:04.860]Bite[00:05.738]\n[00:06.000]x",
            "[ti:T]\n[by:b]\n[00:01.000]<00:01.000>a <00:01.500>b<00:02.000>\n[00:01.000]<00:01.000>翻译<00:02.000>",
            "[00:09.000]<00:09.000>a <00:10.000><00:10.500>b<00:11.000>\n[00:12.000]plain\n[00:13.000]<00:13.000>z<00:14.000>",
            "[offset:-250]\n[00:10.00][00:30.00]chorus\n[00:20.00]verse",
        };
        foreach (string sample in samples)
        {
            Lyric first = Parse(sample);
            Lyric second = Parse(Format(first));
            AssertEquals(Dump(first), Dump(second), sample);
            AssertEquals(Dump(first), Dump(Parse(Format(second))), sample);
        }
    }

    // ---------------------------------------------------------------- robustness

    [Fact]
    public void HugeInputsParseQuicklyAndCorrectly()
    {
        AssertTimeoutPreemptively(TimeSpan.FromSeconds(20), () =>
        {
            // one line with 100k words
            StringBuilder big = new StringBuilder("[00:00.000]");
            for (int i = 0; i < 100_000; i++)
            {
                big.Append('<').Append(Stamp(i)).Append(">w").Append(i).Append(' ');
            }
            big.Append('<').Append(Stamp(100_000)).Append('>');
            Lyric words = Parse(big.ToString());
            Assert.Equal(100_000, words.Lines[0].WordTimeStamps.Length);
            Assert.Equal(100_000L, words.Lines[0].EndTimeMs);

            // a 5 MB line of text, a million-digit tag, and nothing but opening brackets
            Assert.Equal(5_000_000, Parse("[00:01.00]" + new string('a', 5_000_000)).Lines[0].Line.Length);
            Assert.Equal(MaxTimeMs, Parse("[" + new string('9', 1_000_000) + ":00]x").Lines[0].StartTimeMs);
            Assert.Single(Parse(new string('[', 200_000)).Lines);
            Assert.Single(Parse(new string('<', 200_000)).Lines);
            Assert.Single(Parse("[1:" + new string('1', 200_000)).Lines);

            // 100k lines sharing one start time must stay linear
            StringBuilder same = new StringBuilder();
            for (int i = 0; i < 100_000; i++)
            {
                same.Append("[00:01.000]<00:01.000>a <00:01.500>b<00:02.000>\n");
            }
            same.Append("[00:05.000]end");
            Lyric lyric = Parse(same.ToString());
            Assert.Equal(100_001, lyric.Lines.Count);
            Assert.Equal(2000L, lyric.Lines[0].EndTimeMs);

            // many repeat tags on one line
            StringBuilder repeats = new StringBuilder();
            for (int i = 0; i < 20_000; i++)
            {
                repeats.Append('[').Append(Stamp(i * 10L)).Append(']');
            }
            Assert.Equal(20_000, Parse(repeats + "x").Lines.Count);
        });
    }

    /// <summary>java.util.Random (the 48-bit LCG), so the random corpus below is the same one the Java test walks through.</summary>
    private sealed class JavaRandom
    {
        private const long Mask = (1L << 48) - 1;
        private long _seed;

        public JavaRandom(long seed)
        {
            _seed = (seed ^ 0x5DEECE66DL) & Mask;
        }

        private int Next(int bits)
        {
            _seed = (_seed * 0x5DEECE66DL + 0xBL) & Mask;
            return (int)(_seed >> (48 - bits));
        }

        public int NextInt(int bound)
        {
            int r = Next(31);
            int m = bound - 1;
            if ((bound & m) == 0)
            {
                r = (int)((bound * (long)r) >> 31);
            }
            else
            {
                for (int u = r; u - (r = u % bound) + m < 0; u = Next(31))
                {
                }
            }
            return r;
        }
    }

    [Fact]
    public void RandomGarbageNeverThrowsAndAlwaysSatisfiesTheInvariants()
    {
        string[] pieces = { "[", "]", "<", ">", ":", ".", ",", "0", "1", "9", "00:01.000", "[00:02.5]", "<00:03.0>",
            "[00:04.00]", "[00:01.00][00:02.00]", " ", "  ", "\t", "\n", "\r", "\r\n", "\uFEFF", "\u00A0", "a", "word ",
            "歌", "🎵", "-", "+", "[offset:", "[ti:", "offset", "ti:x]", "[offset:-5]", "[by:me]", "99999999999999999999",
            "[99:99:99]", "<99:99.999>" };
        JavaRandom random = new JavaRandom(42);
        for (int round = 0; round < 20_000; round++)
        {
            StringBuilder text = new StringBuilder();
            int count = random.NextInt(40);
            for (int i = 0; i < count; i++)
            {
                text.Append(pieces[random.NextInt(pieces.Length)]);
            }
            string input = text.ToString();
            Lyric lyric = DoesNotThrow(() => Parse(input), input);
            DoesNotThrow(() => LrcParser.UsesEnhancedSyntax(input), input);
            AssertInvariants(lyric, input);
            // the formatter-stand-in must also cope with whatever came out
            DoesNotThrow(() => Parse(Format(lyric)), input);
        }
    }

    private static string TrimEnd(string s)
    {
        int end = s.Length;
        while (end > 0 && (char.IsWhiteSpace(s[end - 1]) || char.IsSeparator(s[end - 1]) || s[end - 1] == '\u200B'))
        {
            end--;
        }
        return s.Substring(0, end);
    }

    private static void AssertInvariants(Lyric lyric, string input)
    {
        Assert.NotNull(lyric.Lines);
        bool anyWordSynced = false;
        foreach (LyricLine line in lyric.Lines)
        {
            string where = "in " + input.Replace("\n", "\\n").Replace("\r", "\\r") + " line '" + line.Line + "'";
            Assert.True(line.StartTimeMs >= 0 && line.StartTimeMs <= MaxTimeMs, where);
            Assert.True(line.EndTimeMs >= line.StartTimeMs && line.EndTimeMs <= MaxTimeMs, where);
            Assert.True(line.WordTimeStamps != null, where);
            AssertEquals(TrimEnd(line.Line), line.Line, where);
            anyWordSynced |= line.IsWordSynced;
            StringBuilder joined = new StringBuilder();
            foreach (WordTimeStamp word in line.WordTimeStamps)
            {
                Assert.False(word.Word.Length == 0, where);
                Assert.True(word.EndTimeMs >= word.StartTimeMs, where);
                Assert.True(word.StartTimeMs >= 0 && word.EndTimeMs <= MaxTimeMs, where);
                joined.Append(word.Word);
            }
            if (line.IsWordSynced)
            {
                AssertEquals(TrimEnd(joined.ToString()), line.Line, where);
            }
            foreach (string translation in line.Translations)
            {
                Assert.False(translation.Length == 0, where);
            }
        }
        if (lyric.Lines.Count == 0)
        {
            Assert.Equal(SyncType.NoSync, lyric.GetSyncType());
        }
        if (anyWordSynced)
        {
            Assert.Equal(SyncType.WordSync, lyric.GetSyncType());
        }
        else
        {
            Assert.True(lyric.GetSyncType() != SyncType.WordSync);
        }
        Assert.True(Math.Abs(lyric.OffsetMs) <= MaxTimeMs);
        Assert.False(lyric.Metadata.ContainsKey("offset"));
        Assert.True(lyric.Warnings.Count <= 101);
    }
}
