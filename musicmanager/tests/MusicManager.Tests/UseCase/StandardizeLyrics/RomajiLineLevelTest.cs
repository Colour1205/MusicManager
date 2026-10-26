using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

/// <summary>
/// Romaji in line-level files (LDDC's "[t]text[t]" output and plain "[t]text" files, where no line has two timed words):
/// which lines need romaji, how the romaji line above a line is folded back into it, how it is written and verified.
/// </summary>
public class RomajiLineLevelTest
{
    // ---- helpers ----

    /// <summary>a line with the given words, each 100 ms long</summary>
    private static LyricLine Timed(long start, params string[] texts)
    {
        WordTimeStamp[] words = new WordTimeStamp[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            words[i] = new WordTimeStamp(texts[i], start + i * 100L, start + i * 100L + 100);
        }
        LyricLine line = new LyricLine(JavaString.StripTrailing(string.Join("", texts)), start, start + texts.Length * 100L);
        line.WordTimeStamps = words;
        return line;
    }

    /// <summary>a line-synced line: text only</summary>
    private static LyricLine Plain(long start, long end, string text)
    {
        LyricLine line = new LyricLine(text, start, end);
        line.WordTimeStamps = new WordTimeStamp[0];
        return line;
    }

    private static Lyric LyricOf(SyncType type, params LyricLine[] lines)
    {
        return new Lyric(type, new List<LyricLine>(lines), null);
    }

    // ---- which files are line-level, which lines need romaji ----

    [Fact]
    public void AFileWithNoMultiWordLineIsLineLevel()
    {
        Assert.True(RomajiScripts.IsLineLevel(LyricOf(SyncType.WordSync, Timed(0, "안녕 세상"), Timed(5000, "Hello"))));
        Assert.True(RomajiScripts.IsLineLevel(LyricOf(SyncType.LineSync, Plain(0, 100, "안녕"), Plain(100, 200, "x"))));
    }

    [Fact]
    public void AFileWithAMultiWordLineUntimedOrEmptyLyricsAreNotLineLevel()
    {
        Assert.False(RomajiScripts.IsLineLevel(LyricOf(SyncType.WordSync, Timed(0, "안녕 세상"), Timed(5000, "a ", "b"))));
        Assert.False(RomajiScripts.IsLineLevel(LyricOf(SyncType.NoSync, Plain(0, 0, "안녕"))));
        Assert.False(RomajiScripts.IsLineLevel(LyricOf(SyncType.LineSync)));
        Assert.False(RomajiScripts.IsLineLevel(null));
    }

    [Fact]
    public void InALineLevelFileAnyKoreanOrJapaneseLineNeedsRomaji()
    {
        Assert.True(RomajiScripts.NeedsRomaji(Timed(0, "안녕 세상"), true));
        Assert.True(RomajiScripts.NeedsRomaji(Plain(0, 100, "こんにちは"), true));
        Assert.False(RomajiScripts.NeedsRomaji(Plain(0, 100, "你好世界"), true), "Chinese alone is not Korean or Japanese");
        Assert.False(RomajiScripts.NeedsRomaji(Plain(0, 100, "Hello world"), true));
        Assert.False(RomajiScripts.NeedsRomaji(null, true));
    }

    [Fact]
    public void InAWordSyncedFileALineStillNeedsTwoTimedWords()
    {
        Assert.False(RomajiScripts.NeedsRomaji(Timed(0, "안녕 세상"), false));
        Assert.False(RomajiScripts.NeedsRomaji(Plain(0, 100, "안녕 세상"), false));
        Assert.True(RomajiScripts.NeedsRomaji(Timed(0, "안녕 ", "세상"), false));
        Assert.True(RomajiScripts.NeedsRomaji(Timed(0, "안녕 ", "세상")), "the one-argument form is the word-synced rule");
    }

    // ---- folding the romaji line above a line (it arrives as the line, and the original as its translation) ----

    [Fact]
    public void ALatinLineWithAKoreanTranslationIsTheRomajiOfThatKoreanLine()
    {
        LyricLine romaji = Timed(1000, "annyeong sesang");
        romaji.AddTranslation("안녕 세상");
        romaji.AddTranslation("你好世界");
        Lyric lyric = LyricOf(SyncType.WordSync, romaji, Timed(5000, "Hello world"));

        RomajiGrouper.Group(lyric);

        Assert.Equal(2, lyric.Lines.Count);
        LyricLine original = lyric.Lines[0];
        Assert.Equal("안녕 세상", original.Line);
        Assert.Equal(1000, original.StartTimeMs);
        Assert.Equal(1100, original.EndTimeMs);
        Assert.Equal(1, original.WordTimeStamps.Length);
        Assert.Equal("안녕 세상", original.WordTimeStamps[0].Word);
        Assert.Equal(1000, original.WordTimeStamps[0].StartTimeMs);
        Assert.Equal(1100, original.WordTimeStamps[0].EndTimeMs);
        Assert.Equal(romaji.WordTimeStamps, original.RomanizedWords);
        Assert.Equal(new List<string> { "你好世界" }, original.Translations); // the other translations stay translations
        Assert.True(RomajiScripts.HasLatinRomanization(original));
        Assert.False(lyric.Lines[1].HasRomanization());
    }

    [Fact]
    public void AChineseCharacterLineWithAJapaneseTranslationIsOldRomaji()
    {
        LyricLine romaji = Timed(0, "空你基哇");
        romaji.AddTranslation("こんにちは");
        Lyric lyric = LyricOf(SyncType.WordSync, romaji);

        RomajiGrouper.Group(lyric);

        Assert.Equal(1, lyric.Lines.Count);
        LyricLine original = lyric.Lines[0];
        Assert.Equal("こんにちは", original.Line);
        Assert.True(original.HasRomanization());
        Assert.False(RomajiScripts.HasLatinRomanization(original), "Chinese characters are not Latin romaji");
    }

    [Fact]
    public void ALineSyncedLineWithoutWordsGetsAOneWordRomajiOverItsTime()
    {
        LyricLine romaji = Plain(2000, 4000, "annyeong sesang");
        romaji.AddTranslation("안녕 세상");
        Lyric lyric = LyricOf(SyncType.LineSync, romaji, Plain(5000, 6000, "Hello world"));

        RomajiGrouper.Group(lyric);

        LyricLine original = lyric.Lines[0];
        Assert.Equal("안녕 세상", original.Line);
        Assert.Equal(2000, original.StartTimeMs);
        Assert.Equal(4000, original.EndTimeMs);
        Assert.True(original.WordTimeStamps.Length == 0, "the original stays line-synced");
        Assert.Equal(1, original.RomanizedWords.Length);
        Assert.Equal("annyeong sesang", original.RomanizedWords[0].Word);
        Assert.Equal(2000, original.RomanizedWords[0].StartTimeMs);
        Assert.Equal(4000, original.RomanizedWords[0].EndTimeMs);
    }

    [Fact]
    public void ASingleLineLyricIsFoldedToo()
    {
        LyricLine romaji = Plain(0, 0, "annyeong");
        romaji.AddTranslation("안녕");
        Lyric lyric = LyricOf(SyncType.LineSync, romaji);

        RomajiGrouper.Group(lyric);

        Assert.Equal(1, lyric.Lines.Count);
        Assert.Equal("안녕", lyric.Lines[0].Line);
    }

    [Fact]
    public void ShapesThatAreNotARomajiLineAreLeftAlone()
    {
        LyricLine koreanWithEnglish = Plain(0, 100, "안녕 세상"); // the usual order: original, then its translation
        koreanWithEnglish.AddTranslation("Hello world");
        LyricLine englishWithChinese = Plain(200, 300, "Hello world");
        englishWithChinese.AddTranslation("你好世界");
        LyricLine duet = Plain(400, 500, "가나다");
        duet.AddTranslation("라마바");
        LyricLine punctuation = Plain(600, 700, "- !");
        punctuation.AddTranslation("안녕");
        LyricLine untranslated = Plain(800, 900, "Hello");
        Lyric lyric = LyricOf(SyncType.LineSync, koreanWithEnglish, englishWithChinese, duet, punctuation, untranslated);

        RomajiGrouper.Group(lyric);

        Assert.Equal(5, lyric.Lines.Count);
        foreach (LyricLine line in lyric.Lines)
        {
            Assert.False(line.HasRomanization(), line.Line);
        }
        Assert.Same(koreanWithEnglish, lyric.Lines[0]);
    }

    [Fact]
    public void AWordSyncedFileDoesNotFoldATranslation()
    {
        // some line has two timed words, so the Korean one-word line under an English line is a translation
        LyricLine english = Timed(0, "Hello ", "world");
        english.AddTranslation("안녕 세상");
        Lyric lyric = LyricOf(SyncType.WordSync, english, Timed(5000, "Good ", "night"));

        RomajiGrouper.Group(lyric);

        Assert.Same(english, lyric.Lines[0]);
        Assert.False(english.HasRomanization());
        Assert.Equal(new List<string> { "안녕 세상" }, english.Translations);
    }

    [Fact]
    public void ALineThatAlreadyHasRomajiIsNotFoldedAgain()
    {
        LyricLine romaji = Plain(0, 100, "annyeong");
        romaji.AddTranslation("안녕");
        Lyric lyric = LyricOf(SyncType.LineSync, romaji);

        RomajiGrouper.Group(lyric);
        RomajiGrouper.Group(lyric);

        Assert.Equal(1, lyric.Lines.Count);
        Assert.Equal("안녕", lyric.Lines[0].Line);
        Assert.Equal("annyeong", lyric.Lines[0].RomanizedWords[0].Word);
    }

    [Fact]
    public void GroupingAnUntimedLyricChangesNothing()
    {
        Lyric lyric = LyricOf(SyncType.NoSync, Plain(0, 0, "annyeong"), Plain(0, 0, "안녕"));

        Assert.Null(RomajiGrouper.Group(null));
        RomajiGrouper.Group(lyric);

        Assert.Equal(2, lyric.Lines.Count);
    }

    // ---- writing it ----

    [Fact]
    public void ALineSyncedRomajiIsWrittenAsALineOfTextAboveTheOriginal()
    {
        LyricLine original = Plain(1000, 4000, "안녕 세상");
        original.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp(" annyeong sesang ", 1000, 4000) };
        original.AddTranslation("你好世界");
        Lyric lyric = LyricOf(SyncType.LineSync, original, Plain(5000, 6000, "Hello world"));

        string text = StandardLrcFormatter.Format(lyric);

        Assert.True(text.Contains("\r\n[00:01.000]annyeong sesang\r\n[00:01.000]안녕 세상\r\n[00:01.000]你好世界\r\n"
                + "[00:05.000]Hello world\r\n"), text);
        Assert.False(text.Contains("<"));
    }

    [Fact]
    public void ALineSyncedLineWithoutRomajiIsWrittenAsBefore()
    {
        Lyric lyric = LyricOf(SyncType.LineSync, Plain(1000, 4000, "안녕 세상"));

        string text = StandardLrcFormatter.Format(lyric);

        Assert.True(text.EndsWith("\r\n\r\n[00:01.000]안녕 세상\r\n", StringComparison.Ordinal), text);
    }

    [Fact]
    public void AWordSyncedRomajiLineIsStillWrittenWordSynced()
    {
        LyricLine original = Timed(1000, "안녕 세상");
        original.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesang", 1000, 1100) };
        Lyric lyric = LyricOf(SyncType.WordSync, original);

        string text = StandardLrcFormatter.Format(lyric);

        Assert.True(text.Contains("[00:01.000]<00:01.000>annyeong sesang<00:01.100>\r\n"
                + "[00:01.000]<00:01.000>안녕 세상<00:01.100>\r\n"), text);
    }

    // ---- verifying it ----

    [Fact]
    public void TheEndOfALineSyncedRomajiLineIsNotCompared()
    {
        LyricLine want = Plain(1000, 4000, "안녕 세상");
        want.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesang", 1000, 4000) };
        LyricLine got = Plain(1000, 1000, "안녕 세상");
        got.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesang", 1000, 1000) };

        Assert.Equal(new List<string>(), LyricVerifier.Compare(LyricOf(SyncType.LineSync, want), LyricOf(SyncType.LineSync, got)));
    }

    [Fact]
    public void TheTextAndStartOfALineSyncedRomajiLineAreStillCompared()
    {
        LyricLine want = Plain(1000, 4000, "안녕 세상");
        want.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesang", 1000, 4000) };
        LyricLine otherText = Plain(1000, 4000, "안녕 세상");
        otherText.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesan", 1000, 4000) };
        LyricLine otherStart = Plain(1500, 4000, "안녕 세상");
        otherStart.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesang", 1500, 4000) };
        LyricLine missing = Plain(1000, 4000, "안녕 세상");

        Lyric expected = LyricOf(SyncType.LineSync, want);
        Assert.False(LyricVerifier.Compare(expected, LyricOf(SyncType.LineSync, otherText)).Count == 0);
        Assert.False(LyricVerifier.Compare(expected, LyricOf(SyncType.LineSync, otherStart)).Count == 0);
        Assert.False(LyricVerifier.Compare(expected, LyricOf(SyncType.LineSync, missing)).Count == 0);
    }

    [Fact]
    public void TheWordTimesOfAWordSyncedRomajiLineAreStillCompared()
    {
        LyricLine want = Timed(1000, "안녕 세상");
        want.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesang", 1000, 1100) };
        LyricLine got = Timed(1000, "안녕 세상");
        got.RomanizedWords = new WordTimeStamp[] { new WordTimeStamp("annyeong sesang", 1000, 1500) };

        Assert.False(LyricVerifier.Compare(LyricOf(SyncType.WordSync, want), LyricOf(SyncType.WordSync, got)).Count == 0);
    }
}
