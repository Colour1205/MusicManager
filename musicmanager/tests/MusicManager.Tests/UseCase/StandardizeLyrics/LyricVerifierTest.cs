using System.Text;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

public class LyricVerifierTest
{
    private static WordTimeStamp W(string text, long start, long end)
    {
        return new WordTimeStamp(text, start, end);
    }

    /// <summary>a word-synced line whose text is the words joined, with optional translations</summary>
    private static LyricLine Words(long start, long end, string translation, params WordTimeStamp[] words)
    {
        StringBuilder text = new StringBuilder();
        foreach (WordTimeStamp word in words)
        {
            text.Append(word.Word);
        }
        LyricLine line = new LyricLine(JavaString.StripTrailing(text.ToString()), start, end);
        line.WordTimeStamps = words;
        if (translation != null)
        {
            line.AddTranslation(translation);
        }
        return line;
    }

    private static LyricLine Plain(long start, long end, string text)
    {
        return new LyricLine(text, start, end);
    }

    private static Lyric LyricOf(params LyricLine[] lines)
    {
        return new Lyric(SyncType.WordSync, new List<LyricLine>(lines), "song.lrc");
    }

    private static LyricLine Cobra(long shift, long wordEndShift)
    {
        return Words(1000, 3000, null, W("Hello ", 1000 + shift, 1500 + wordEndShift), W("world", 1500 + shift, 3000 + wordEndShift));
    }

    // ---- equivalent ----

    [Fact]
    public void IdenticalLyricsAreEquivalent()
    {
        Lyric a = LyricOf(Cobra(0, 0), Words(4000, 5000, "translated", W("Bye", 4000, 5000)));
        Lyric b = LyricOf(Cobra(0, 0), Words(4000, 5000, "translated", W("Bye", 4000, 5000)));

        Assert.Equal(new List<string>(), LyricVerifier.Compare(a, b));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(a, a));
    }

    [Fact]
    public void OneMillisecondEitherWayIsRoundingNotADifference()
    {
        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(Cobra(0, 0)), LyricOf(Cobra(1, 1))));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(Cobra(0, 0)), LyricOf(Cobra(-1, -1))));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(Plain(1000, 2000, "x")), LyricOf(Plain(1001, 2000, "x"))));
    }

    [Fact]
    public void MetadataOffsetAndLineEndsAreNotCompared()
    {
        Lyric a = LyricOf(Words(1000, 3000, null, W("Hi", 1000, 2000)));
        a.OffsetMs = 700;
        a.Metadata["ti"] = "One";
        Lyric b = LyricOf(Words(1000, 9999, null, W("Hi", 1000, 2000)));
        b.Metadata["ti"] = "Two";
        b.Metadata["by"] = "someone";

        Assert.Equal(new List<string>(), LyricVerifier.Compare(a, b));
    }

    [Fact]
    public void TranslationAbsorbedIntoItsLineEqualsASeparateLine()
    {
        LyricLine withTranslation = Words(1000, 2000, "小心一些", W("Careful", 1000, 2000));
        Lyric absorbed = LyricOf(withTranslation, Words(3000, 4000, null, W("Next", 3000, 4000)));
        Lyric separate = LyricOf(Words(1000, 2000, null, W("Careful", 1000, 2000)), Plain(1000, 2000, "小心一些"),
                Words(3000, 4000, null, W("Next", 3000, 4000)));

        Assert.Equal(new List<string>(), LyricVerifier.Compare(absorbed, separate));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(separate, absorbed));
    }

    [Fact]
    public void TranslationReadBackAsAOneWordLineIsEquivalent()
    {
        Lyric absorbed = LyricOf(Words(1000, 2000, "translated", W("Careful", 1000, 2000)));
        Lyric separate = LyricOf(Words(1000, 2000, null, W("Careful", 1000, 2000)),
                Words(1000, 2000, null, W("translated", 1000, 2000)));

        Assert.Equal(new List<string>(), LyricVerifier.Compare(absorbed, separate));
    }

    [Fact]
    public void ALineWithoutWordsMatchesItsSingleWordRewrite()
    {
        Lyric linePerLine = LyricOf(Plain(1000, 2000, "Hello there"));
        Lyric oneWord = LyricOf(Words(1000, 2000, null, W("Hello there", 1000, 2000)));

        Assert.Equal(new List<string>(), LyricVerifier.Compare(linePerLine, oneWord));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(oneWord, linePerLine));
    }

    [Fact]
    public void TrailingWhitespaceOfALineIsIgnored()
    {
        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(Plain(0, 1, "Hello  \t")), LyricOf(Plain(0, 1, "Hello"))));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(Plain(0, 1, "Hello")), LyricOf(Plain(0, 1, "Hello" + (char)0x3000))));
    }

    [Fact]
    public void ALinesTextOnlyDiffersInWhitespaceWhenItsWordsAreTheSame()
    {
        // a .ttml's line text can say "How  could" (two spaces) where its words are "How " and "could"; the file's line text is
        // the words joined
        LyricLine source = new LyricLine("How  could you", 1000, 2000);
        source.WordTimeStamps = new[] { W("How ", 1000, 1200), W("could ", 1200, 1500), W("you", 1500, 2000) };
        Lyric reread = LyricOf(Words(1000, 2000, null, W("How ", 1000, 1200), W("could ", 1200, 1500), W("you", 1500, 2000)));

        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(source), reread));
    }

    [Fact]
    public void AZeroWidthSpaceAtTheEndOfALineIsNotPartOfWhatIsRead()
    {
        LyricLine source = new LyricLine("Hold my head a little higher​", 1000, 2000);
        source.WordTimeStamps = new[] { W("Hold my head a little higher​", 1000, 2000) };
        LyricLine reread = new LyricLine("Hold my head a little higher", 1000, 2000);
        reread.WordTimeStamps = new[] { W("Hold my head a little higher​", 1000, 2000) };

        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(source), LyricOf(reread)));
    }

    [Fact]
    public void ALineWithAnotherLetterIsStillADifferenceWhateverTheWhitespace()
    {
        Assert.Single(LyricVerifier.Compare(LyricOf(Plain(0, 1, "How  could")), LyricOf(Plain(0, 1, "How would"))));
        Assert.Single(LyricVerifier.Compare(LyricOf(Plain(0, 1, "Hello world")), LyricOf(Plain(0, 1, "Helloworld"))));
    }

    [Fact]
    public void AWordThatChangedIsStillADifferenceEvenWhenTheLineTextsMatchOnceReadLikeAReader()
    {
        Lyric source = LyricOf(Words(1000, 2000, null, W("How ", 1000, 1500), W("could", 1500, 2000)));
        Lyric changed = LyricOf(Words(1000, 2000, null, W("How  ", 1000, 1500), W("could", 1500, 2000)));

        Assert.Single(LyricVerifier.Compare(source, changed));
    }

    [Fact]
    public void EmptyWordsAndBlankTranslationsAreIgnoredOnBothSides()
    {
        LyricLine noisy = Words(1000, 2000, null, W("", 1000, 1000), W("Hi", 1000, 2000), W("", 2000, 2000));
        noisy.Translations = new List<string> { " ", "", null };

        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(noisy), LyricOf(Words(1000, 2000, null, W("Hi", 1000, 2000)))));
    }

    [Fact]
    public void EmptyAndNullLyricsAreEquivalent()
    {
        Assert.Equal(new List<string>(), LyricVerifier.Compare(null, null));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(), LyricOf()));
        Assert.Equal(new List<string>(), LyricVerifier.Compare(new Lyric(SyncType.NoSync, null, null), LyricOf()));
        List<LyricLine> withNulls = new List<LyricLine>();
        withNulls.Add(null);
        Assert.Equal(new List<string>(), LyricVerifier.Compare(new Lyric(SyncType.NoSync, withNulls, null), LyricOf()));
    }

    // ---- different ----

    [Fact]
    public void OneWordTwoMillisecondsOffIsReported()
    {
        Lyric a = LyricOf(Cobra(0, 0));
        Lyric startOff = LyricOf(Words(1000, 3000, null, W("Hello ", 1000, 1500), W("world", 1502, 3000)));
        Lyric endOff = LyricOf(Words(1000, 3000, null, W("Hello ", 1000, 1500), W("world", 1500, 3002)));

        List<string> start = LyricVerifier.Compare(a, startOff);
        Assert.Equal(1, start.Count);
        Assert.True(start[0].Contains("world") && start[0].Contains("starts at 1502"), start[0]);

        List<string> end = LyricVerifier.Compare(a, endOff);
        Assert.Equal(1, end.Count);
        Assert.True(end[0].Contains("world") && end[0].Contains("ends at 3002"), end[0]);
    }

    [Fact]
    public void LineStartTwoMillisecondsOffIsReported()
    {
        List<string> differences = LyricVerifier.Compare(LyricOf(Plain(1000, 2000, "x")), LyricOf(Plain(1002, 2000, "x")));

        Assert.Equal(1, differences.Count);
        Assert.True(differences[0].Contains("starts at 1002"), differences[0]);
    }

    [Fact]
    public void MissingLineIsReported()
    {
        Lyric a = LyricOf(Plain(1000, 2000, "one"), Plain(3000, 4000, "two"), Plain(5000, 6000, "three"));
        Lyric b = LyricOf(Plain(1000, 2000, "one"), Plain(5000, 6000, "three"));

        List<string> differences = LyricVerifier.Compare(a, b);

        Assert.True(differences[0].Contains("expected 3 lines") && differences[0].Contains("found 2"), differences[0]);
        Assert.True(differences.Count > 1); // the shifted rows after the gap are reported too
    }

    [Fact]
    public void ExtraLineIsReported()
    {
        Lyric a = LyricOf(Plain(1000, 2000, "one"));
        Lyric b = LyricOf(Plain(1000, 2000, "one"), Plain(3000, 4000, "two"));

        Assert.Equal(1, LyricVerifier.Compare(a, b).Count);
        Assert.True(LyricVerifier.Compare(a, b)[0].Contains("found 2"));
    }

    [Fact]
    public void LostTranslationIsReported()
    {
        Lyric a = LyricOf(Words(1000, 2000, "translated", W("Hi", 1000, 2000)));
        Lyric b = LyricOf(Words(1000, 2000, null, W("Hi", 1000, 2000)));

        Assert.Equal(1, LyricVerifier.Compare(a, b).Count);
    }

    [Fact]
    public void TranslationAtAnotherTimeIsReported()
    {
        Lyric a = LyricOf(Words(1000, 2000, "translated", W("Hi", 1000, 2000)));
        Lyric b = LyricOf(Words(1000, 2000, null, W("Hi", 1000, 2000)), Plain(1500, 2000, "translated"));

        List<string> differences = LyricVerifier.Compare(a, b);

        Assert.Equal(1, differences.Count);
        Assert.True(differences[0].Contains("starts at 1500"), differences[0]);
    }

    [Fact]
    public void ChangedTextIsReported()
    {
        List<string> differences = LyricVerifier.Compare(LyricOf(Plain(0, 1, "Hello")), LyricOf(Plain(0, 1, "Hallo")));

        Assert.Equal(1, differences.Count);
        Assert.True(differences[0].Contains("Hallo"), differences[0]);
    }

    [Fact]
    public void LeadingWhitespaceIsNotADifferenceButAnotherLetterIs()
    {
        // the standard file cannot start a line with a space (a reader drops it), and no player shows one
        Assert.Empty(LyricVerifier.Compare(LyricOf(Plain(0, 1, " Hello")), LyricOf(Plain(0, 1, "Hello"))));
        Assert.Equal(1, LyricVerifier.Compare(LyricOf(Plain(0, 1, " Hello")), LyricOf(Plain(0, 1, "Hallo"))).Count);
    }

    [Fact]
    public void LineWithoutWordsStillNeedsTheSameText()
    {
        Lyric linePerLine = LyricOf(Plain(1000, 2000, "Hello there"));
        Lyric oneWord = LyricOf(Words(1000, 2000, null, W("Hello world", 1000, 2000)));

        Assert.Equal(1, LyricVerifier.Compare(linePerLine, oneWord).Count);
    }

    [Fact]
    public void WordTextIsComparedExactly()
    {
        Lyric a = LyricOf(Words(1000, 2000, null, W("Hello ", 1000, 1500), W("world", 1500, 2000)));
        LyricLine noSpaceLine = new LyricLine("Hello world", 1000, 2000); // same line text, one word lost its space
        noSpaceLine.WordTimeStamps = new WordTimeStamp[] { W("Hello", 1000, 1500), W("world", 1500, 2000) };

        List<string> differences = LyricVerifier.Compare(a, LyricOf(noSpaceLine));

        Assert.Equal(1, differences.Count);
        Assert.True(differences[0].Contains("word 1"), differences[0]);
    }

    [Fact]
    public void DifferentWordCountIsReported()
    {
        Lyric a = LyricOf(Words(1000, 2000, null, W("Hello ", 1000, 1500), W("world", 1500, 2000)));
        Lyric b = LyricOf(Words(1000, 2000, null, W("Hello world", 1000, 2000)));

        List<string> differences = LyricVerifier.Compare(a, b);

        Assert.Equal(1, differences.Count);
        Assert.True(differences[0].Contains("1 words instead of 2"), differences[0]);
    }

    [Fact]
    public void AGapBecomingAdjacentIsReported()
    {
        // "Hello" should stop at 1400 but came back ending at 1500 (gap marker lost)
        Lyric a = LyricOf(Words(1000, 2000, null, W("Hello ", 1000, 1400), W("world", 1500, 2000)));
        Lyric b = LyricOf(Words(1000, 2000, null, W("Hello ", 1000, 1500), W("world", 1500, 2000)));

        List<string> differences = LyricVerifier.Compare(a, b);

        Assert.Equal(1, differences.Count);
        Assert.True(differences[0].Contains("ends at 1500"), differences[0]);
    }

    [Fact]
    public void OneSideMissingIsReported()
    {
        Assert.Equal(new List<string> { "expected lyric is missing" }, LyricVerifier.Compare(null, LyricOf()));
        Assert.Equal(new List<string> { "actual lyric is missing" }, LyricVerifier.Compare(LyricOf(), null));
    }

    [Fact]
    public void ReportsAtMostTenDifferencesWithShortText()
    {
        List<LyricLine> good = new List<LyricLine>();
        List<LyricLine> bad = new List<LyricLine>();
        for (int i = 0; i < 50; i++)
        {
            good.Add(Plain(i * 1000L, i * 1000L + 500, "line " + i + " " + new string('x', 500)));
            bad.Add(Plain(i * 1000L + 5, i * 1000L + 500, "other " + i));
        }

        List<string> differences = LyricVerifier.Compare(new Lyric(SyncType.LineSync, good, null),
                new Lyric(SyncType.LineSync, bad, null));

        Assert.Equal(10, differences.Count);
        foreach (string difference in differences)
        {
            Assert.True(difference.Length < 150, difference);
        }

        // the count message counts towards the cap as well
        bad.RemoveAt(0);
        Assert.Equal(10, LyricVerifier.Compare(new Lyric(SyncType.LineSync, good, null),
                new Lyric(SyncType.LineSync, bad, null)).Count);
    }

    [Fact]
    public void LargeIdenticalLyricsAreQuick()
    {
        List<LyricLine> a = new List<LyricLine>();
        List<LyricLine> b = new List<LyricLine>();
        for (int i = 0; i < 5000; i++)
        {
            a.Add(Words(i * 100L, i * 100L + 90, "tr " + i, W("w" + i + " ", i * 100L, i * 100L + 50), W("x", i * 100L + 50, i * 100L + 90)));
            b.Add(Words(i * 100L, i * 100L + 90, "tr " + i, W("w" + i + " ", i * 100L, i * 100L + 50), W("x", i * 100L + 50, i * 100L + 90)));
        }

        Assert.Equal(new List<string>(), LyricVerifier.Compare(new Lyric(SyncType.WordSync, a, null), new Lyric(SyncType.WordSync, b, null)));
    }
}
