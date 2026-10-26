using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

/// <summary>the script rules, the folding of an LDDC transliteration line into its original, and the word normalizer</summary>
public class RomajiTest
{
    // ---- helpers ----

    private static WordTimeStamp[] Words(long start, params string[] texts)
    {
        WordTimeStamp[] words = new WordTimeStamp[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            words[i] = new WordTimeStamp(texts[i], start + i * 100L, start + i * 100L + 90);
        }
        return words;
    }

    private static LyricLine Line(long start, params string[] texts)
    {
        LyricLine line = new LyricLine(JavaString.StripTrailing(string.Join("", texts)), start, start + texts.Length * 100L);
        line.WordTimeStamps = Words(start, texts);
        return line;
    }

    private static Lyric LyricOf(params LyricLine[] lines)
    {
        return new Lyric(SyncType.WordSync, new List<LyricLine>(lines), null);
    }

    // ---- scripts ----

    [Fact]
    public void HangulAndKanaAreKoreanOrJapaneseButChineseAndLatinAreNot()
    {
        Assert.True(RomajiScripts.HasHangulOrKana("문득 달이"));
        Assert.True(RomajiScripts.HasHangulOrKana("私は"));
        Assert.True(RomajiScripts.HasHangulOrKana("カタカナ"));
        Assert.True(RomajiScripts.HasHangulOrKana("by：소연"), "a mixed word counts");
        Assert.False(RomajiScripts.HasHangulOrKana("偶尔月亮升起来的时候"), "Chinese alone is not Korean or Japanese");
        Assert.False(RomajiScripts.HasHangulOrKana("Hello"));
        Assert.False(RomajiScripts.HasHangulOrKana(""));
        Assert.False(RomajiScripts.HasHangulOrKana(null));
    }

    [Fact]
    public void CjkIncludesChineseCharactersToo()
    {
        Assert.True(RomajiScripts.HasCjk("门的 塔里"));
        Assert.True(RomajiScripts.HasCjk("dal이"));
        Assert.False(RomajiScripts.HasCjk("mundeuk dari (Yeah) 123 - !"));
    }

    [Fact]
    public void ALineNeedsRomajiOnlyWhenItIsKoreanOrJapaneseAndHasSeveralTimedWords()
    {
        Assert.True(RomajiScripts.NeedsRomaji(Line(0, "문득 ", "달이")));
        Assert.False(RomajiScripts.NeedsRomaji(Line(0, "문득")), "one word is not worth a romaji line");
        Assert.False(RomajiScripts.NeedsRomaji(Line(0, "你好 ", "世界")));
        Assert.False(RomajiScripts.NeedsRomaji(Line(0, "Hello ", "world")));
        Assert.False(RomajiScripts.NeedsRomaji(new LyricLine("문득 달이", 0, 100)), "a line without word timing");
        Assert.False(RomajiScripts.NeedsRomaji(null));
    }

    [Fact]
    public void RomanizationCountsAsLatinOnlyWithoutHangulKanaOrChineseCharacters()
    {
        Assert.True(RomajiScripts.IsLatin(Words(0, "mundeuk ", "dari")));
        Assert.True(RomajiScripts.IsLatin(Words(0, "Recorded ", "by：soyeon")));
        Assert.False(RomajiScripts.IsLatin(Words(0, "门的 ", "塔里")), "the LDDC transliteration in Chinese characters");
        Assert.False(RomajiScripts.IsLatin(Words(0, "mundeuk ", "달이")));
        Assert.False(RomajiScripts.IsLatin(Words(0, "- ", "!")), "no letters at all");
    }

    [Fact]
    public void ALineHasLatinRomanizationOnlyWhenItWasGivenOne()
    {
        LyricLine original = Line(0, "문득 ", "달이");
        Assert.False(RomajiScripts.HasLatinRomanization(original));
        original.RomanizedWords = Words(0, "mundeuk ", "dari");
        Assert.True(RomajiScripts.HasLatinRomanization(original));
        original.RomanizedWords = Words(0, "门的 ", "塔里");
        Assert.False(RomajiScripts.HasLatinRomanization(original));
    }

    // ---- Chinese characters: only Korean and Japanese are romanized ----

    /// <summary>"Know My Name (以我之名) i-dle (아이들)": a Korean line with Chinese characters in it</summary>
    private static LyricLine KoreanWithChinese()
    {
        return Line(0, "Know ", "My ", "Name ", "(以我之名) ", "i-dle ", "(아이들)");
    }

    [Fact]
    public void KoreanIsToldFromJapaneseByKana()
    {
        Assert.True(RomajiScripts.HasKana("私は"));
        Assert.True(RomajiScripts.HasKana("カタカナ"));
        Assert.True(RomajiScripts.HasKana("真面目そう"));
        Assert.False(RomajiScripts.HasKana("문득 달이"));
        Assert.False(RomajiScripts.HasKana("你好世界"), "Chinese characters are not kana");
        Assert.False(RomajiScripts.HasKana(null));
    }

    [Fact]
    public void TheChineseCharactersOfATextAreItsHanSequence()
    {
        Assert.Equal("以我之名", RomajiScripts.HanSequence("Know My Name (以我之名) i-dle (아이들)"));
        Assert.Equal("", RomajiScripts.HanSequence("Know My Name (aideul)"));
        Assert.Equal("", RomajiScripts.HanSequence(null));
        Assert.Equal("\U00020000", RomajiScripts.HanSequence("a\U00020000b")); // a supplementary plane character stays whole
        Assert.Equal("\U00006A02", RomajiScripts.HanSequence("\U0000F914")); // a compatibility ideograph is the character it stands for
        Assert.Equal("\U00006A02", RomajiScripts.HanSequence("\U00006A02"));
    }

    [Fact]
    public void ARomajiLineThatKeepsAKoreanLinesChineseCharactersIsLatinRomanization()
    {
        LyricLine original = KoreanWithChinese();
        original.RomanizedWords = Words(0, "Know ", "My ", "Name ", "(以我之名) ", "i-dle ", "(aideul)");
        Assert.True(RomajiScripts.HasLatinRomanization(original));
    }

    [Fact]
    public void ARomajiLineThatTurnedTheChineseCharactersIntoPinyinIsNotLatinRomanization()
    {
        LyricLine original = KoreanWithChinese();
        original.RomanizedWords = Words(0, "Know ", "My ", "Name ", "(yiwozhiming) ", "i-dle ", "(aideul)");
        Assert.False(RomajiScripts.HasLatinRomanization(original), "the Chinese characters have to stay");
    }

    [Fact]
    public void OldRomajiInChineseCharactersIsStillNotLatinRomanizationOfAKoreanLineWithChineseCharacters()
    {
        LyricLine original = KoreanWithChinese();
        original.RomanizedWords = Words(0, "Know ", "My ", "Name ", "(以我之名) ", "i-dle ", "(啊一的)");
        Assert.False(RomajiScripts.HasLatinRomanization(original), "the transliteration of the Hangul is in Chinese characters");
    }

    [Fact]
    public void KeepingTheChineseCharactersInTheWrongOrderOrNotAtAllIsNotLatinRomanization()
    {
        LyricLine twoWords = Line(0, "以我 ", "之名 ", "소연");
        twoWords.RomanizedWords = Words(0, "以我 ", "之名 ", "soyeon");
        Assert.True(RomajiScripts.HasLatinRomanization(twoWords));
        twoWords.RomanizedWords = Words(0, "之名 ", "以我 ", "soyeon");
        Assert.False(RomajiScripts.HasLatinRomanization(twoWords), "same characters, other order");
        twoWords.RomanizedWords = Words(0, "以我 ", "soyeon ", "soyeon");
        Assert.False(RomajiScripts.HasLatinRomanization(twoWords), "a character is missing");
        twoWords.RomanizedWords = Words(0, "以我 ", "之名 之 ", "soyeon");
        Assert.False(RomajiScripts.HasLatinRomanization(twoWords), "a character was added");
    }

    [Fact]
    public void ALineOfKeptChineseCharactersWithoutAnyOtherLetterIsNotRomanization()
    {
        LyricLine original = Line(0, "田小娟 ", "소연");
        original.RomanizedWords = Words(0, "田小娟 ", "-");
        Assert.False(RomajiScripts.HasLatinRomanization(original), "nothing was romanized");
    }

    [Fact]
    public void AJapaneseLinesKanjiAreRomanizedSoNoChineseCharacterMayStay()
    {
        LyricLine original = Line(0, "真", "面", "目", "そ", "う");
        original.RomanizedWords = Words(0, "ma", "ji", "me", "so", "u");
        Assert.True(RomajiScripts.HasLatinRomanization(original));
        original.RomanizedWords = Words(0, "真", "ji", "me", "so", "u");
        Assert.False(RomajiScripts.HasLatinRomanization(original), "kanji kept in a Japanese line");
    }

    [Fact]
    public void ACompatibilityIdeographAndTheCharacterItStandsForAreTheSameChineseCharacter()
    {
        LyricLine original = Line(0, "\U0000F914 ", "소연");
        original.RomanizedWords = Words(0, "\U00006A02 ", "soyeon");
        Assert.True(RomajiScripts.HasLatinRomanization(original));
    }

    [Fact]
    public void ALineWithoutWordTimingIsComparedByItsText()
    {
        LyricLine original = new LyricLine("田小娟 소연", 0, 100);
        original.RomanizedWords = Words(0, "田小娟 soyeon");
        Assert.True(RomajiScripts.HasLatinRomanization(original));
        original.RomanizedWords = Words(0, "tianxiaojuan soyeon");
        Assert.False(RomajiScripts.HasLatinRomanization(original));
    }

    [Fact]
    public void HangulOrKanaLeftInTheRomanizationIsNeverLatin()
    {
        LyricLine original = KoreanWithChinese();
        original.RomanizedWords = Words(0, "Know ", "My ", "Name ", "(以我之名) ", "i-dle ", "(아이들)");
        Assert.False(RomajiScripts.HasLatinRomanization(original));
    }

    [Fact]
    public void AKoreanLineWithChineseCharactersAndItsKeptRomajiAreFoldedTogether()
    {
        LyricLine romaji = Line(0, "Know ", "My ", "Name ", "(以我之名) ", "i-dle ", "(aideul)");
        LyricLine original = KoreanWithChinese();
        Lyric lyric = LyricOf(romaji, original);

        RomajiGrouper.Group(lyric);

        Assert.Equal(1, lyric.Lines.Count);
        Assert.Same(original, lyric.Lines[0]);
        Assert.True(RomajiScripts.HasLatinRomanization(lyric.Lines[0]));
    }

    // ---- grouping ----

    [Fact]
    public void AnLddcTransliterationLineIsFoldedIntoTheOriginalBelowIt()
    {
        LyricLine transliteration = Line(1000, "门的 ", "塔里");
        LyricLine original = Line(1000, "문득 ", "달이");
        original.AddTranslation("偶尔月亮升起来的时候");
        Lyric lyric = LyricOf(transliteration, original);

        RomajiGrouper.Group(lyric);

        Assert.Equal(1, lyric.Lines.Count);
        LyricLine merged = lyric.Lines[0];
        Assert.Same(original, merged);
        Assert.Equal("문득 달이", merged.Line);
        Assert.Equal(transliteration.WordTimeStamps, merged.RomanizedWords);
        Assert.Equal(new List<string> { "偶尔月亮升起来的时候" }, merged.Translations);
    }

    [Fact]
    public void ALatinRomajiLineIsFoldedToo()
    {
        Lyric lyric = LyricOf(Line(0, "mundeuk ", "dari"), Line(0, "문득 ", "달이"));

        RomajiGrouper.Group(lyric);

        Assert.Equal(1, lyric.Lines.Count);
        Assert.True(RomajiScripts.HasLatinRomanization(lyric.Lines[0]));
    }

    [Fact]
    public void EveryPairInAFileIsFolded()
    {
        Lyric lyric = LyricOf(Line(0, "a ", "b"), Line(0, "가 ", "나"), Line(5000, "c ", "d"), Line(5000, "다 ", "라"),
                Line(9000, "English ", "only"));

        RomajiGrouper.Group(lyric);

        Assert.Equal(3, lyric.Lines.Count);
        Assert.True(lyric.Lines[0].HasRomanization());
        Assert.True(lyric.Lines[1].HasRomanization());
        Assert.False(lyric.Lines[2].HasRomanization());
    }

    [Fact]
    public void LinesThatAreNotATransliterationAndItsOriginalAreLeftAlone()
    {
        // two Korean lines at the same time (a duet), an English line then a Chinese-only line, a different start
        Lyric duet = LyricOf(Line(0, "가 ", "나"), Line(0, "다 ", "라"));
        Lyric englishThenChinese = LyricOf(Line(0, "Hello ", "there"), Line(0, "你好 ", "世界"));
        Lyric differentStart = LyricOf(Line(0, "mundeuk ", "dari"), Line(50, "문득 ", "달이"));
        Lyric oneWordTransliteration = LyricOf(Line(0, "mun"), Line(0, "문득 ", "달이"));
        Lyric punctuationOnly = LyricOf(Line(0, "- ", "-"), Line(0, "문득 ", "달이"));

        foreach (Lyric lyric in new Lyric[] { duet, englishThenChinese, differentStart, oneWordTransliteration, punctuationOnly })
        {
            RomajiGrouper.Group(lyric);
            Assert.Equal(2, lyric.Lines.Count);
            Assert.False(lyric.Lines[0].HasRomanization());
            Assert.False(lyric.Lines[1].HasRomanization());
        }
    }

    [Fact]
    public void GroupingTwiceChangesNothingMore()
    {
        Lyric lyric = LyricOf(Line(0, "mundeuk ", "dari"), Line(0, "문득 ", "달이"));

        RomajiGrouper.Group(lyric);
        RomajiGrouper.Group(lyric);

        Assert.Equal(1, lyric.Lines.Count);
    }

    [Fact]
    public void GroupingToleratesNullAndTinyLyrics()
    {
        Assert.Null(RomajiGrouper.Group(null));
        Lyric single = LyricOf(Line(0, "가 ", "나"));
        Assert.Equal(1, RomajiGrouper.Group(single).Lines.Count);
        Assert.Equal(0, RomajiGrouper.Group(LyricOf()).Lines.Count);
    }

    // ---- normalizing words ----

    [Fact]
    public void ASpaceTimedAsAWordOfItsOwnBecomesTrailingTextOfTheWordBefore()
    {
        WordTimeStamp[] normalized = LyricNormalizer.Normalize(new WordTimeStamp[] {
                new WordTimeStamp("ALL", 0, 61), new WordTimeStamp(" ", 61, 82), new WordTimeStamp("IN", 82, 123) });

        Assert.Equal(2, normalized.Length);
        Assert.Equal("ALL ", normalized[0].Word);
        Assert.Equal(0, normalized[0].StartTimeMs);
        Assert.True(normalized[0].EndTimeMs == 61, "the word keeps its own timing, the gap stays a gap");
        Assert.Equal("IN", normalized[1].Word);
    }

    [Fact]
    public void NormalizingKeepsTextThatAlreadyEndsInASpaceAndDropsALeadingSpacer()
    {
        WordTimeStamp[] normalized = LyricNormalizer.Normalize(new WordTimeStamp[] {
                new WordTimeStamp(" ", 0, 10), new WordTimeStamp("ALL ", 10, 20), new WordTimeStamp("  ", 20, 30),
                new WordTimeStamp("", 30, 30), null, new WordTimeStamp("IN", 30, 40) });

        Assert.Equal(2, normalized.Length);
        Assert.True(normalized[0].Word == "ALL ", "no second space is added");
        Assert.Equal("IN", normalized[1].Word);
    }

    [Fact]
    public void NormalizingALyricTouchesEveryWordSyncedLine()
    {
        LyricLine line = new LyricLine("a b", 0, 100);
        line.WordTimeStamps = new WordTimeStamp[] { new WordTimeStamp("a", 0, 10), new WordTimeStamp(" ", 10, 20),
                new WordTimeStamp("b", 20, 30) };
        LyricLine plain = new LyricLine("no words", 200, 300);
        Lyric lyric = LyricOf(line, plain);

        LyricNormalizer.Normalize(lyric);

        Assert.Equal(2, line.WordTimeStamps.Length);
        Assert.Null(plain.WordTimeStamps);
        Assert.Null(LyricNormalizer.Normalize((Lyric)null));
    }

    // ---- the key of a line ----

    [Fact]
    public void ALineKeyIsMadeOfItsWordsSoARepeatedChorusIsOneKey()
    {
        RomajiLine a = RomajiLine.Of(Words(0, "문득 ", "달이"));
        RomajiLine b = RomajiLine.Of(Words(5000, "문득 ", "달이"));
        RomajiLine other = RomajiLine.Of(Words(0, "문득 ", "별이"));

        Assert.Equal(a.Key, b.Key);
        Assert.False(a.Key.Equals(other.Key));
        Assert.Equal(new List<string> { "문득 ", "달이" }, a.Words);
    }
}
