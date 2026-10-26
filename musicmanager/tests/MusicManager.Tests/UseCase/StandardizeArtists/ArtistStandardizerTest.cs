using System.Text;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;
using Xunit.Sdk;

namespace MusicManager.Tests.UseCase.StandardizeArtists;

public class ArtistStandardizerTest
{
    // ------------------------------------------------------------------ helpers

    private static int _songCounter = 0;

    private static List<string> L(params string[] values)
    {
        return new List<string>(values);
    }

    private static SongArtistTags Song(List<string> artist, List<string> albumArtist, List<string> artists)
    {
        return new SongArtistTags("song-" + (_songCounter++) + ".flac", artist, albumArtist, artists);
    }

    /// <summary>a song with one ARTIST value and (optionally) one ALBUMARTIST value</summary>
    private static SongArtistTags Song(string artist, string albumArtist)
    {
        return Song(artist == null ? L() : L(artist), albumArtist == null ? L() : L(albumArtist), L());
    }

    private static SongArtistTags Song(string artist)
    {
        return Song(artist, null);
    }

    private static List<SongArtistTags> Repeat(int times, string artist, string albumArtist)
    {
        List<SongArtistTags> songs = new List<SongArtistTags>();
        for (int i = 0; i < times; i++)
        {
            songs.Add(Song(artist, albumArtist));
        }
        return songs;
    }

    private static List<SongArtistTags> Songs(params object[] parts)
    {
        List<SongArtistTags> songs = new List<SongArtistTags>();
        foreach (object part in parts)
        {
            if (part is SongArtistTags single)
            {
                songs.Add(single);
            }
            else
            {
                songs.AddRange((IEnumerable<SongArtistTags>)part);
            }
        }
        return songs;
    }

    /// <summary>a ParsedArtist the AI was sure about</summary>
    private static ParsedArtist Ok(string text, string english)
    {
        return new ParsedArtist(text, english, false);
    }

    private static ParsedArtist Unsure(string text, string english)
    {
        return new ParsedArtist(text, english, true);
    }

    private static void Put(IDictionary<string, List<ParsedArtist>> parsed, string raw, params ParsedArtist[] artists)
    {
        parsed[raw] = new List<ParsedArtist>(artists);
    }

    private static ArtistGroup Group(ArtistPlan plan, string canonicalName)
    {
        foreach (ArtistGroup group in plan.Groups)
        {
            if (group.CanonicalName.Equals(canonicalName))
            {
                return group;
            }
        }
        throw new XunitException("no group named '" + canonicalName + "' in ["
                + string.Join(", ", plan.Groups.Select(g => g.CanonicalName)) + "]");
    }

    private static SongArtistUpdate UpdateFor(List<SongArtistUpdate> updates, SongArtistTags song)
    {
        foreach (SongArtistUpdate update in updates)
        {
            if (update.AudioPath.Equals(song.Path))
            {
                return update;
            }
        }
        return null;
    }

    private static FieldChange Change(SongArtistUpdate update, string field)
    {
        foreach (FieldChange change in update.Changes)
        {
            if (change.Field.Equals(field))
            {
                return change;
            }
        }
        return null;
    }

    private static List<string> VariantTexts(ArtistGroup group)
    {
        return group.Variants.Select(v => v.Text).ToList();
    }

    private static int VariantCount(ArtistGroup group, string text)
    {
        foreach (ArtistVariant variant in group.Variants)
        {
            if (variant.Text.Equals(text))
            {
                return variant.Count;
            }
        }
        throw new XunitException("no variant '" + text + "' in [" + string.Join(", ", VariantTexts(group)) + "]");
    }

    private static List<string> Texts(List<ParsedArtist> artists)
    {
        return artists.Select(a => a.Text).ToList();
    }

    private static List<string> Englishes(List<ParsedArtist> artists)
    {
        return artists.Select(a => a.English).ToList();
    }

    private static List<ParsedArtist> ParseOne(string raw)
    {
        var result = ArtistStandardizer.ParseLocally(new List<string> { raw });
        Assert.True(result.Count == 1, "one raw value gives one entry: " + raw);
        return result.Values.First();
    }

    /// <summary>the tags as they would be after the update was written to the file</summary>
    private static SongArtistTags Apply(SongArtistTags song, SongArtistUpdate update)
    {
        if (update == null)
        {
            return song;
        }
        return new SongArtistTags(song.Path,
                update.ArtistJoined == null ? song.Artist : L(update.ArtistJoined),
                update.AlbumArtistJoined == null ? song.AlbumArtist : L(update.AlbumArtistJoined),
                update.ArtistsList == null ? song.Artists : update.ArtistsList);
    }

    private static ArtistPlan Plan(List<SongArtistTags> songs, IDictionary<string, List<ParsedArtist>> parsed)
    {
        return ArtistStandardizer.BuildPlan(songs, parsed, ArtistStandardizer.DefaultSeparator, true);
    }

    /// <summary>assertEquals(expected, actual, message)</summary>
    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        try
        {
            Assert.Equal(expected, actual);
        }
        catch (XunitException e)
        {
            throw new XunitException(message + " ==> " + e.Message);
        }
    }

    /// <summary>assertTimeoutPreemptively: the work is abandoned (not just reported) when it takes too long</summary>
    private static T AssertTimeoutPreemptively<T>(TimeSpan timeout, Func<T> work)
    {
        Task<T> task = Task.Run(work);
        if (!task.Wait(timeout))
        {
            throw new XunitException("execution timed out after " + (long)timeout.TotalMilliseconds + " ms");
        }
        return task.GetAwaiter().GetResult();
    }

    /// <summary>java.util.Random, so that the random inputs of the tests are the same as the Java tests' inputs</summary>
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

    // ------------------------------------------------------------------ normalizeRaw

    [Fact]
    public void NormalizeRawHandlesNullEmptyAndBlank()
    {
        Assert.Equal("", ArtistStandardizer.NormalizeRaw(null));
        Assert.Equal("", ArtistStandardizer.NormalizeRaw(""));
        Assert.Equal("", ArtistStandardizer.NormalizeRaw("   \t\r\n  "));
    }

    [Fact]
    public void NormalizeRawTrimsAndCollapsesWhitespaceRuns()
    {
        Assert.Equal("NewJeans", ArtistStandardizer.NormalizeRaw("NewJeans  "));
        Assert.Equal("New Jeans", ArtistStandardizer.NormalizeRaw("  New \t\t Jeans \r\n"));
        Assert.Equal("A B", ArtistStandardizer.NormalizeRaw("A\r\nB"));
    }

    [Fact]
    public void NormalizeRawTreatsExoticSpacesAsWhitespace()
    {
        Assert.Equal("A B C D", ArtistStandardizer.NormalizeRaw("A\u00A0\u3000B\u2003C\u202FD"));
        Assert.Equal("X", ArtistStandardizer.NormalizeRaw("\u00A0\u3000X\u00A0"));
        Assert.Equal("A B", ArtistStandardizer.NormalizeRaw("A\u0085B")); // NEL
        Assert.Equal("A B", ArtistStandardizer.NormalizeRaw("A\u2028B")); // line separator
    }

    [Fact]
    public void NormalizeRawComposesToNfc()
    {
        Assert.Equal("café", ArtistStandardizer.NormalizeRaw("cafe\u0301"));
        Assert.Equal("ROSÉ", ArtistStandardizer.NormalizeRaw("ROSE\u0301"));
        // decomposed Hangul jamo (what macOS file systems and some taggers produce) become syllables
        Assert.Equal("한", ArtistStandardizer.NormalizeRaw("\u1112\u1161\u11AB"));
    }

    [Fact]
    public void NormalizeRawDropsInvisibleCharacters()
    {
        Assert.Equal("IU", ArtistStandardizer.NormalizeRaw("\uFEFFIU"));
        Assert.Equal("IU", ArtistStandardizer.NormalizeRaw("I\u200BU"));
        // NUL pads some tags and separates the values of an ID3v2.4 frame: it must not glue two names together
        Assert.Equal("A B", ArtistStandardizer.NormalizeRaw("A\u0000B"));
        Assert.Equal("IU", ArtistStandardizer.NormalizeRaw("IU\u0000\u0000"));
        Assert.Equal("IU", ArtistStandardizer.NormalizeRaw("\u0000IU"));
    }

    [Fact]
    public void NormalizeRawKeepsInnerCjkAndSurrogatePairs()
    {
        Assert.Equal("G.E.M. 邓紫棋", ArtistStandardizer.NormalizeRaw("G.E.M.  邓紫棋"));
        Assert.Equal("🎵 Mix", ArtistStandardizer.NormalizeRaw("🎵\u00A0\u00A0Mix"));
        Assert.Equal("𠮷野家", ArtistStandardizer.NormalizeRaw(" 𠮷野家 "));
    }

    [Fact]
    public void NormalizeRawIsIdempotent()
    {
        string[] samples = { "  a  b ", "e\u0301\u00A0x", "\uFEFF\u3000\u1112\u1161\u11AB ", "A\r\n\tB", "", "\u200B" };
        foreach (string sample in samples)
        {
            string once = ArtistStandardizer.NormalizeRaw(sample);
            Assert.Equal(once, ArtistStandardizer.NormalizeRaw(once));
        }
    }

    // ------------------------------------------------------------------ countRawValues

    [Fact]
    public void CountRawValuesCountsEveryValueOfEveryField()
    {
        List<SongArtistTags> songs = new List<SongArtistTags>
        {
            Song(L("IU", "IU"), L("IU"), L("IU")), // 4 values on one song, all counted
            Song(L("TWICE"), L("TWICE (트와이스)"), L()),
        };
        var counts = ArtistStandardizer.CountRawValues(songs);
        Assert.Equal(4, counts["IU"]);
        Assert.Equal(1, counts["TWICE"]);
        Assert.Equal(1, counts["TWICE (트와이스)"]);
        Assert.Equal(3, counts.Count);
    }

    [Fact]
    public void CountRawValuesMergesSpellingsThatNormalizeTheSame()
    {
        List<SongArtistTags> songs = new List<SongArtistTags> { Song("NewJeans  ", "NewJeans"), Song(" NewJeans"),
                Song("New\u00A0Jeans") };
        var counts = ArtistStandardizer.CountRawValues(songs);
        Assert.Equal(3, counts["NewJeans"]);
        Assert.Equal(1, counts["New Jeans"]);
        Assert.Equal(2, counts.Count);
    }

    [Fact]
    public void CountRawValuesSkipsBlanksAndNulls()
    {
        List<string> withNulls = new List<string>();
        withNulls.Add(null);
        withNulls.Add("  ");
        withNulls.Add("\u200B");
        withNulls.Add("IU");
        List<SongArtistTags> songs = new List<SongArtistTags>();
        songs.Add(Song(withNulls, L(""), L()));
        songs.Add(null);
        songs.Add(Song(L(), L(), L()));
        var counts = ArtistStandardizer.CountRawValues(songs);
        Assert.Equal(new Dictionary<string, int> { { "IU", 1 } }, counts.ToDictionary(e => e.Key, e => e.Value));
    }

    [Fact]
    public void CountRawValuesIsSortedByCountThenText()
    {
        List<SongArtistTags> songs = Songs(Repeat(3, "b", null), Repeat(3, "a", null), Repeat(5, "z", null),
                Repeat(1, "c", null));
        Assert.Equal(L("z", "a", "b", "c"), new List<string>(ArtistStandardizer.CountRawValues(songs).Keys));
    }

    [Fact]
    public void CountRawValuesToleratesNullAndEmptyInput()
    {
        Assert.True(ArtistStandardizer.CountRawValues(null).Count == 0);
        Assert.True(ArtistStandardizer.CountRawValues(new List<SongArtistTags>()).Count == 0);
    }

    // ------------------------------------------------------------------ isEnglishOnly

    [Theory]
    [InlineData("ROSÉ")]
    [InlineData("(G)I-DLE")]
    [InlineData("G.E.M.")]
    [InlineData("$ign")]
    [InlineData("Tyler, The Creator")]
    [InlineData("Beyoncé")]
    [InlineData("Björk")]
    [InlineData("Ñandú")]
    [InlineData("2NE1")]
    [InlineData("pH-1")]
    [InlineData("Ke$ha")]
    [InlineData("O'Neil")]
    [InlineData("Łukasz")]
    [InlineData("ROSE\u0301")]
    [InlineData("  IU  ")]
    [InlineData("K/DA")]
    [InlineData("Wiz_H")]
    [InlineData("Ø")]
    [InlineData("ß")]
    [InlineData("f(x)")]
    [InlineData("AC/DC")]
    public void IsEnglishOnlyAcceptsLatinNames(string name)
    {
        Assert.True(ArtistStandardizer.IsEnglishOnly(name), name);
    }

    [Theory]
    [InlineData("G.E.M. 邓紫棋")]
    [InlineData("Billlie (빌리)")]
    [InlineData("i-dle (아이들)")]
    [InlineData("陈奕迅")]
    [InlineData("セレーナ・ゴメス")]
    [InlineData("Иван")]
    [InlineData("محمد")]
    [InlineData("ΣΑΣ")]
    [InlineData("2NE1 투애니원")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    [InlineData("!!!")]
    [InlineData("—")]
    [InlineData("Аlan Walker" /* Cyrillic A */)]
    public void IsEnglishOnlyRejectsEverythingElse(string name)
    {
        Assert.False(ArtistStandardizer.IsEnglishOnly(name), name);
    }

    [Fact]
    public void IsEnglishOnlyRejectsNull()
    {
        Assert.False(ArtistStandardizer.IsEnglishOnly(null));
    }

    // ------------------------------------------------------------------ looseKey

    [Fact]
    public void LooseKeyKnownValues()
    {
        Assert.Equal("rose", ArtistStandardizer.LooseKey("ROSÉ"));
        Assert.Equal("rose", ArtistStandardizer.LooseKey("Rose"));
        Assert.Equal("kda", ArtistStandardizer.LooseKey("K/DA"));
        Assert.Equal("kda", ArtistStandardizer.LooseKey("K-DA"));
        Assert.Equal("gem", ArtistStandardizer.LooseKey("G.E.M."));
        Assert.Equal("gidle", ArtistStandardizer.LooseKey("(G)I-DLE"));
        Assert.Equal("idle", ArtistStandardizer.LooseKey("i-dle"));
        Assert.Equal("陈奕迅", ArtistStandardizer.LooseKey("陈奕迅"));
        Assert.StartsWith("hearts2hearts", ArtistStandardizer.LooseKey("Hearts2Hearts (하츠투하츠)"), StringComparison.Ordinal);
    }

    [Fact]
    public void LooseKeyIsEmptyWhenThereIsNothingToKeep()
    {
        Assert.Equal("", ArtistStandardizer.LooseKey(null));
        Assert.Equal("", ArtistStandardizer.LooseKey(""));
        Assert.Equal("", ArtistStandardizer.LooseKey("  \t "));
        Assert.Equal("", ArtistStandardizer.LooseKey("!!! ... ---"));
    }

    [Theory]
    [InlineData("ROSÉ", "ROSE")]
    [InlineData("ROSÉ", "rose")]
    [InlineData("K/DA", "KDA")]
    [InlineData("K/DA", "K-DA")]
    [InlineData("Tyler, The Creator", "tyler the creator")]
    [InlineData("ＲＯＳＥ", "rose")]
    [InlineData("ﬁnal", "final")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("Beyoncé", "Beyonce")]
    [InlineData("G.E.M.", "g e m")]
    public void LooseKeyIgnoresAccentsCasePunctuationAndWidth(string a, string b)
    {
        Assert.Equal(ArtistStandardizer.LooseKey(b), ArtistStandardizer.LooseKey(a));
    }

    [Fact]
    public void LooseKeyComposedAndDecomposedAreTheSame()
    {
        Assert.Equal(ArtistStandardizer.LooseKey("한국"),
                ArtistStandardizer.LooseKey("\u1112\u1161\u11AB\u1100\u116E\u11A8"));
        Assert.Equal(ArtistStandardizer.LooseKey("É"), ArtistStandardizer.LooseKey("E\u0301"));
    }

    [Fact]
    public void LooseKeyKeepsDifferentNamesApart()
    {
        Assert.NotEqual(ArtistStandardizer.LooseKey("MINNIE"), ArtistStandardizer.LooseKey("(G)I-DLE"));
        Assert.NotEqual(ArtistStandardizer.LooseKey("(G)I-DLE"), ArtistStandardizer.LooseKey("i-dle"));
        // kana dakuten is part of the letter: two different Japanese names must not collapse
        Assert.NotEqual(ArtistStandardizer.LooseKey("ゴメス"), ArtistStandardizer.LooseKey("コメス"));
        Assert.NotEqual(ArtistStandardizer.LooseKey("周杰伦"), ArtistStandardizer.LooseKey("周杰倫"));
    }

    // ------------------------------------------------------------------ parseLocally: splitting

    [Fact]
    public void ParseLocallyLeavesSingleArtistsAlone()
    {
        List<ParsedArtist> parsed = ParseOne("IU");
        Assert.Single(parsed);
        Assert.Equal("IU", parsed[0].Text);
        Assert.Equal("IU", parsed[0].English);
        Assert.False(parsed[0].IsLowConfidence);
        Assert.False(ParseOne("ROSÉ")[0].IsLowConfidence);
    }

    [Fact]
    public void ParseLocallySplitsOnSemicolons()
    {
        List<ParsedArtist> parsed = ParseOne("HUNTR/X; EJAE; AUDREY NUNA; REI AMI; KPop Demon Hunters Cast");
        Assert.Equal(L("HUNTR/X", "EJAE", "AUDREY NUNA", "REI AMI", "KPop Demon Hunters Cast"), Texts(parsed));
        Assert.Equal(L("Anyma", "LISA"), Texts(ParseOne("Anyma;LISA")));
    }

    [Fact]
    public void ParseLocallySplitsOnAmpersands()
    {
        Assert.Equal(L("TZUYU", "pH-1"), Texts(ParseOne("TZUYU & pH-1")));
        Assert.Equal(L("LE SSERAFIM", "ILLIT", "KATSEYE"), Texts(ParseOne("LE SSERAFIM&ILLIT&KATSEYE")));
        Assert.Equal(L("Olivia Rodrigo", "Robert Smith"), Texts(ParseOne("Olivia Rodrigo & Robert Smith")));
    }

    [Fact]
    public void ParseLocallySplitsOnCjkAndFullWidthSeparators()
    {
        Assert.Equal(L("G.E.M. 邓紫棋", "张靓颖"),
                Texts(ParseOne("G.E.M. 邓紫棋、张靓颖")));
        Assert.Equal(L("薄之谦", "张靓颖"), Texts(ParseOne("薄之谦，张靓颖")));
        Assert.Equal(L("A一", "B二"), Texts(ParseOne("A一；B二")));
    }

    [Fact]
    public void ParseLocallySplitsOnFeatAndFtCaseInsensitively()
    {
        Assert.Equal(L("IU", "Suga"), Texts(ParseOne("IU feat. Suga")));
        Assert.Equal(L("IU", "Suga"), Texts(ParseOne("IU FEAT. Suga")));
        Assert.Equal(L("IU", "Suga"), Texts(ParseOne("IU Ft. Suga")));
        // a bracketed credit is part of one name, not a separator: the user reviews it
        Assert.Equal(L("IU (feat. Suga)"), Texts(ParseOne("IU (feat. Suga)")));
    }

    [Fact]
    public void ParseLocallySplitsOnSpacedX()
    {
        Assert.Equal(L("(G)I-DLE", "Dimitri Vegas", "Like Mike"), Texts(ParseOne("(G)I-DLE x Dimitri Vegas x Like Mike")));
        Assert.Equal(L("A", "B"), Texts(ParseOne("A X B")));
        Assert.Equal(L("Lil Nas X"), Texts(ParseOne("Lil Nas X"))); // trailing X is a name, not a separator
        Assert.Equal(L("Lil Nas X", "Jack Harlow"), Texts(ParseOne("Lil Nas X x Jack Harlow")));
        Assert.Equal(L("Lil Nas X", "Jack Harlow"), Texts(ParseOne("Lil Nas X & Jack Harlow")));
        Assert.Equal(L("Max"), Texts(ParseOne("Max"))); // a plain x inside a word
    }

    [Fact]
    public void ParseLocallySplitsOnSpacedSlash()
    {
        Assert.Equal(L("JISOO", "Zayn"), Texts(ParseOne("JISOO / Zayn")));
        Assert.Equal(L("A", "B"), Texts(ParseOne("A / B"))); // even when the pieces are short
    }

    [Fact]
    public void ParseLocallySplitsOnBareSlashOnlyWhenPiecesAreLongEnough()
    {
        Assert.Equal(L("Alan Walker", "Au-Ra", "Tomine Harket"), Texts(ParseOne("Alan Walker/Au-Ra/Tomine Harket")));
        Assert.Equal(L("HIGH4", "IU"), Texts(ParseOne("HIGH4/IU")));
        Assert.Equal(L("ITZY", "彩领"), Texts(ParseOne("ITZY/彩领")));
        Assert.Equal(L("A/B"), Texts(ParseOne("A/B"))); // pieces shorter than 2 characters
        Assert.Equal(L("Foo/"), Texts(ParseOne("Foo/")));
        Assert.Equal(L("/Foo"), Texts(ParseOne("/Foo")));
        Assert.Equal(L("Foo//Bar"), Texts(ParseOne("Foo//Bar")));
    }

    [Fact]
    public void ParseLocallyNeverSplitsProtectedNames()
    {
        foreach (string protectedName in new string[] { "K/DA", "HUNTR/X", "AC/DC", "Tyler, The Creator", "Earth, Wind & Fire",
                "Simon & Garfunkel", "Mumford & Sons", "Florence + the Machine", "Years & Years", "Hall & Oates",
                "tyler, the creator", "AC/dc", "SIMON & GARFUNKEL" })
        {
            AssertEqual(L(protectedName), Texts(ParseOne(protectedName)), protectedName);
        }
    }

    [Fact]
    public void ParseLocallyKeepsProtectedNamesInsideLongerValues()
    {
        Assert.Equal(L("K/DA", "League of Legends"), Texts(ParseOne("K/DA & League of Legends")));
        Assert.Equal(L("K/DA", "DJ Sona", "League of Legends", "Sophie Powers"),
                Texts(ParseOne("K/DA & DJ Sona & League of Legends & Sophie Powers")));
        Assert.Equal(L("Simon & Garfunkel", "Paul Simon"), Texts(ParseOne("Simon & Garfunkel; Paul Simon")));
        Assert.Equal(L("Tyler, The Creator", "Frank Ocean"), Texts(ParseOne("Tyler, The Creator & Frank Ocean")));
        Assert.Equal(L("Earth, Wind & Fire", "Foo Bar"), Texts(ParseOne("Earth, Wind & Fire, Foo Bar")));
        Assert.Equal(L("AC/DC", "Foo Bar"), Texts(ParseOne("AC/DC/Foo Bar")));
        Assert.Equal(L("KPop Demon Hunters Cast", "HUNTR/X", "Saja Boys"),
                Texts(ParseOne("KPop Demon Hunters Cast, HUNTR/X, Saja Boys")));
    }

    [Fact]
    public void ParseLocallyProtectsWholeWordsOnly()
    {
        Assert.Equal(L("Hall", "Oatesy"), Texts(ParseOne("Hall & Oatesy")));
        Assert.Equal(L("BAC", "DC"), Texts(ParseOne("BAC/DC")));
    }

    [Fact]
    public void ParseLocallySplitsOnCommasBetweenRealNames()
    {
        Assert.Equal(L("Matt Champion", "Jennie"), Texts(ParseOne("Matt Champion, Jennie")));
        Assert.Equal(L("High4", "IU"), Texts(ParseOne("High4, IU")));
        Assert.Equal(L("Foo Bar", "Baz Quux", "Zed Alpha"), Texts(ParseOne("Foo Bar, Baz Quux, Zed Alpha")));
    }

    [Fact]
    public void ParseLocallyKeepsCommasThatAreNotSeparators()
    {
        Assert.Equal(L("1,000 Maniacs"), Texts(ParseOne("1,000 Maniacs")));
        Assert.Equal(L("Foo, A"), Texts(ParseOne("Foo, A")));
        Assert.Equal(L("A, Foo"), Texts(ParseOne("A, Foo")));
    }

    [Fact]
    public void ParseLocallyDropsCommasLeftAtTheEdgesOfAPiece()
    {
        Assert.Equal(L("IU"), Texts(ParseOne("IU,")));
        Assert.Equal(L("IU"), Texts(ParseOne(", IU")));
        Assert.Equal(L("Foo Bar", "Baz Quux"), Texts(ParseOne("Foo Bar, Baz Quux,")));
        Assert.Equal(L("Foo Bar", "Zed"), Texts(ParseOne("Foo Bar,; Zed")));
        Assert.Equal(L(",,,"), Texts(ParseOne(",,,")));
    }

    [Fact]
    public void ParseLocallyDoesNotSplitOnAnd()
    {
        Assert.Equal(L("Robynn And Kendy"), Texts(ParseOne("Robynn And Kendy")));
    }

    [Fact]
    public void ParseLocallySplitsTheHugeRealWorldString()
    {
        string raw = "钟镇涛/梁汉文/谭咏麟/陈慧娴/陈晓东/许志安"
                + "/李幸倪/刘浩龙/麦家瑜/符致逸/欧阳靖/G.E.M. 邓紫棋"
                + "/Robynn And Kendy/AGA/罗钧满/周残廷[重复]/赵浚承/gold mountain/Artem Ansheles";
        List<ParsedArtist> parsed = ParseOne(raw);
        Assert.Equal(19, parsed.Count);
        Assert.Equal("G.E.M. 邓紫棋", parsed[11].Text);
        Assert.Equal("G.E.M.", parsed[11].English);
        Assert.Equal("周残廷[重复]", parsed[15].Text);
        Assert.Equal("周残廷", parsed[15].English);
        Assert.Equal("Artem Ansheles", parsed[18].Text);
    }

    [Fact]
    public void ParseLocallyNormalizesTheKeyAndTheWhitespaceInsideValues()
    {
        var result = ArtistStandardizer.ParseLocally(new List<string> { " IU \u00A0&\t NewJeans " });
        Assert.Equal(L("IU & NewJeans"), new List<string>(result.Keys));
        Assert.Equal(L("IU", "NewJeans"), Texts(result["IU & NewJeans"]));
    }

    [Fact]
    public void ParseLocallyIgnoresBlankNullAndRepeatedValues()
    {
        Assert.True(ArtistStandardizer.ParseLocally(null).Count == 0);
        Assert.True(ArtistStandardizer.ParseLocally(new List<string>()).Count == 0);
        Assert.True(ArtistStandardizer.ParseLocally(new List<string> { null, "", "   ", "\u200B" }).Count == 0);
        var result = ArtistStandardizer.ParseLocally(new List<string> { "B", " B ", "A", "B", "A " });
        Assert.Equal(L("B", "A"), new List<string>(result.Keys));
    }

    [Fact]
    public void ParseLocallyKeepsValuesThatAreOnlySeparators()
    {
        Assert.Equal(L(";;;"), Texts(ParseOne(";;;")));
        Assert.Equal(L("&"), Texts(ParseOne("&")));
        Assert.Equal(L(" / ".Trim()), Texts(ParseOne(" / ")));
        Assert.Equal(L("A"), Texts(ParseOne("A;")));
        Assert.Equal(L("A"), Texts(ParseOne(";;A;;")));
    }

    // ------------------------------------------------------------------ parseLocally: English names

    [Fact]
    public void ParseLocallyStripsJunkMarkersFromTheEnglishNameOnly()
    {
        ParsedArtist chinese = ParseOne("周残廷[重复]")[0];
        Assert.Equal("周残廷[重复]", chinese.Text);
        Assert.Equal("周残廷", chinese.English);
        Assert.True(chinese.IsLowConfidence);

        ParsedArtist latin = ParseOne("Foo (duplicate)")[0];
        Assert.Equal("Foo (duplicate)", latin.Text);
        Assert.Equal("Foo", latin.English);
        Assert.Equal("Foo", ParseOne("Foo [DUP]")[0].English);
        Assert.Equal("Foo", ParseOne("Foo（重复）")[0].English);
        Assert.Equal("Billlie", ParseOne("Billlie (빌리) [重复]")[0].English);
        Assert.Equal("Foo (live)", ParseOne("Foo (live)")[0].English); // other brackets are real text
    }

    [Fact]
    public void ParseLocallyNeverReturnsAnEmptyEnglishName()
    {
        ParsedArtist onlyJunk = ParseOne("[重复]")[0];
        Assert.Equal("[重复]", onlyJunk.Text);
        Assert.Equal("[重复]", onlyJunk.English);
    }

    [Fact]
    public void ParseLocallyStripsTrailingTranslations()
    {
        Assert.Equal("Billlie", ParseOne("Billlie (빌리)")[0].English);
        Assert.Equal("i-dle", ParseOne("i-dle (아이들)")[0].English);
        Assert.Equal("ITZY", ParseOne("ITZY(있지)")[0].English);
        Assert.Equal("Hearts2Hearts", ParseOne("Hearts2Hearts (하츠투하츠)")[0].English);
        Assert.Equal("(G)I-DLE", ParseOne("(G)I-DLE (아이들)")[0].English);
        Assert.Equal("f(x)", ParseOne("f(x) (에프엑스)")[0].English);
        Assert.Equal("Billlie", ParseOne("Billlie - 빌리")[0].English);
        // the original stays in text, and the piece is flagged because it still needs a human look
        ParsedArtist billlie = ParseOne("Billlie (빌리)")[0];
        Assert.Equal("Billlie (빌리)", billlie.Text);
        Assert.True(billlie.IsLowConfidence);
    }

    [Fact]
    public void ParseLocallyTakesTheLatinNameOutOfLeadingTranslations()
    {
        Assert.Equal("LISA", ParseOne("리사 (LISA)")[0].English);
        Assert.Equal("ALDrain", ParseOne("얼드레인 (ALDrain)")[0].English);
        Assert.Equal("G.NA", ParseOne("지나(G.NA)")[0].English);
        Assert.Equal("ILLIT", ParseOne("아일릿(ILLIT)")[0].English);
        Assert.Equal("ROSÉ", ParseOne("로제 (ROSÉ)")[0].English);
    }

    [Fact]
    public void ParseLocallyKeepsOnlyTheLatinPartOfMixedNames()
    {
        Assert.Equal("G.E.M.", ParseOne("G.E.M.邓紫棋")[0].English);
        Assert.Equal("G.E.M.", ParseOne("G.E.M. 邓紫棋")[0].English);
        Assert.Equal("F.I.R.", ParseOne("F.I.R.飞儿乐团")[0].English);
        Assert.Equal("Wiz_H", ParseOne("Wiz_H张子豪")[0].English);
        Assert.Equal("Perfume", ParseOne("Perfume パフューム")[0].English);
        Assert.Equal("Foo Bar", ParseOne("Foo 한글 Bar")[0].English);
        Assert.Equal("G.E.M.邓紫棋", ParseOne("G.E.M.邓紫棋")[0].Text);
    }

    [Fact]
    public void ParseLocallyLeavesNamesWithoutLatinLettersAlone()
    {
        Assert.Equal("민니 ((여자)아이들)", ParseOne("민니 ((여자)아이들)")[0].English);
        Assert.Equal("陈奕迅", ParseOne("陈奕迅")[0].English);
        Assert.Equal("セレーナ・ゴメス", ParseOne("セレーナ・ゴメス")[0].English);
        Assert.True(ParseOne("陈奕迅")[0].IsLowConfidence);
    }

    [Fact]
    public void ParseLocallyDoesNotMangleLookAlikeLetters()
    {
        // a Cyrillic "A" typed into an otherwise Latin name must not be silently cut out
        ParsedArtist parsed = ParseOne("Аlan Walker")[0];
        Assert.Equal("Аlan Walker", parsed.English);
        Assert.True(parsed.IsLowConfidence);
    }

    [Fact]
    public void ParseLocallyFlagsLowConfidenceWhenThePieceIsNotEnglishOnly()
    {
        foreach (ParsedArtist artist in ParseOne("ITZY/彩领"))
        {
            AssertEqual(!artist.Text.Equals("ITZY"), artist.IsLowConfidence, artist.Text);
        }
        Assert.True(ParseOne("G.E.M.邓紫棋")[0].IsLowConfidence);
        Assert.False(ParseOne("Tyler, The Creator")[0].IsLowConfidence);
    }

    [Fact]
    public void ParseLocallyHandlesPathologicalInputQuickly()
    {
        StringBuilder many = new StringBuilder();
        for (int i = 0; i < 20_000; i++)
        {
            many.Append("Ab Cd, Ef; G/H & ");
        }
        string huge = many.ToString();
        List<ParsedArtist> parsed = AssertTimeoutPreemptively(TimeSpan.FromSeconds(5), () => ParseOne(huge));
        Assert.True(parsed.Count > 10_000);
        string noSeparators = new string('x', 300_000);
        Assert.Single(AssertTimeoutPreemptively(TimeSpan.FromSeconds(5), () => ParseOne(noSeparators)));
    }

    // ------------------------------------------------------------------ buildPlan: grouping and canonical names

    [Fact]
    public void BuildPlanMergesItzyAndItzyAndPicksTheMostCommonSpelling()
    {
        List<SongArtistTags> songs = Songs(Repeat(3, "ITZY", "ITZY (있지)"), Song("Itzy", "ITZY"), Song("ITZY"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "ITZY", Ok("ITZY", "ITZY"));
        Put(parsed, "Itzy", Ok("Itzy", "ITZY"));
        Put(parsed, "ITZY (있지)", Ok("ITZY (있지)", "ITZY"));

        ArtistPlan plan = Plan(songs, parsed);

        Assert.Single(plan.Groups);
        ArtistGroup group = plan.Groups[0];
        Assert.Equal("ITZY", group.CanonicalName);
        Assert.False(group.IsSuggestedByAi);
        Assert.False(group.IsLowConfidence);
        Assert.Equal(5, group.SongCount);
        Assert.Equal(L("ITZY", "ITZY (있지)", "Itzy"), VariantTexts(group));
        Assert.Equal(5, VariantCount(group, "ITZY")); // 4 ARTIST values + 1 ALBUMARTIST
        Assert.Equal(3, VariantCount(group, "ITZY (있지)"));
        Assert.Equal(1, VariantCount(group, "Itzy"));
        Assert.True(plan.Warnings.Count == 0);
    }

    [Fact]
    public void BuildPlanGroupsNewJeansWithTrailingSpacesAsOneRawValue()
    {
        List<SongArtistTags> songs = Songs(Song("NewJeans  ", "NewJeans  "), Song("NewJeans", "NewJeans"), Song("NewJeans"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "NewJeans  ", Ok("NewJeans  ", "NewJeans")); // the AI echoed the raw spelling back untrimmed
        ArtistPlan plan = Plan(songs, parsed);

        Assert.Equal(L("NewJeans"), new List<string>(plan.GroupKeysByRaw.Keys));
        Assert.Single(plan.Groups);
        Assert.Equal("NewJeans", plan.Groups[0].CanonicalName);
        Assert.Equal(5, VariantCount(plan.Groups[0], "NewJeans"));
        Assert.True(plan.Warnings.Count == 0, "the untrimmed key was recognised, so nothing fell back to local parsing");

        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);
        Assert.Single(updates);
        SongArtistUpdate update = updates[0];
        Assert.Equal(songs[0].Path, update.AudioPath);
        Assert.Equal("NewJeans", update.ArtistJoined);
        Assert.Equal("NewJeans", update.AlbumArtistJoined);
        Assert.Null(update.ArtistsList);
        Assert.Equal("NewJeans  ", Change(update, "ARTIST").Before);
        Assert.Equal("NewJeans", Change(update, "ARTIST").After);
    }

    [Fact]
    public void BuildPlanPrefersTheEnglishOnlyAlbumArtistOverCjkSpellingsOfGem()
    {
        List<SongArtistTags> songs = Songs(Repeat(5, "G.E.M. 邓紫棋", null), Repeat(2, "G.E.M.", "G.E.M."),
                Song("G.E.M.邓紫棋"), Song("邓紫棋"),
                Song("Namewee/G.E.M. 邓紫棋"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "G.E.M. 邓紫棋", Unsure("G.E.M. 邓紫棋", "G.E.M."));
        Put(parsed, "G.E.M.", Ok("G.E.M.", "G.E.M."));
        Put(parsed, "G.E.M.邓紫棋", Unsure("G.E.M.邓紫棋", "G.E.M."));
        Put(parsed, "邓紫棋", Unsure("邓紫棋", "G.E.M."));
        Put(parsed, "Namewee/G.E.M. 邓紫棋", Ok("Namewee", "Namewee"),
                Unsure("G.E.M. 邓紫棋", "G.E.M."));

        ArtistPlan plan = Plan(songs, parsed);

        ArtistGroup gem = Group(plan, "G.E.M.");
        Assert.True(!gem.IsSuggestedByAi, "an English-only spelling exists in the library");
        Assert.True(gem.IsLowConfidence, "some contributing parse was unsure");
        Assert.Equal(10, gem.SongCount);
        // 6 uses of "G.E.M. 邓紫棋" (5 + the collaboration), 4 of "G.E.M." (2 ARTIST + 2 ALBUMARTIST)
        Assert.Equal(6, VariantCount(gem, "G.E.M. 邓紫棋"));
        Assert.Equal(4, VariantCount(gem, "G.E.M."));
        Assert.Equal(1, VariantCount(gem, "邓紫棋"));
        Assert.Equal("Namewee", Group(plan, "Namewee").CanonicalName);

        // the Chinese spelling is the most common one, but only English-only spellings can win
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);
        SongArtistUpdate first = UpdateFor(updates, songs[0]);
        Assert.Equal("G.E.M.", first.ArtistJoined);
        SongArtistUpdate collab = UpdateFor(updates, songs[songs.Count - 1]);
        Assert.Equal("Namewee; G.E.M.", collab.ArtistJoined);
        Assert.Equal(L("Namewee", "G.E.M."), collab.ArtistsList);
        Assert.True(UpdateFor(updates, songs[5]) == null, "already standard");
    }

    /// <summary>a Chinese-language artist, whose official credit stays Chinese</summary>
    private static ParsedArtist Chinese(string text, string official)
    {
        return new ParsedArtist(text, official, false, true);
    }

    [Fact]
    public void AChineseLanguageArtistKeepsTheOfficialChineseCreditAsTheName()
    {
        List<SongArtistTags> songs = Songs(Repeat(5, "G.E.M. 邓紫棋", null), Repeat(2, "G.E.M.", "G.E.M."),
                Song("G.E.M.邓紫棋"), Song("Namewee/G.E.M. 邓紫棋"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "G.E.M. 邓紫棋", Chinese("G.E.M. 邓紫棋", "G.E.M. 邓紫棋"));
        Put(parsed, "G.E.M.", Chinese("G.E.M.", "G.E.M. 邓紫棋"));
        Put(parsed, "G.E.M.邓紫棋", Chinese("G.E.M.邓紫棋", "G.E.M. 邓紫棋"));
        Put(parsed, "Namewee/G.E.M. 邓紫棋", Ok("Namewee", "Namewee"), Chinese("G.E.M. 邓紫棋", "G.E.M. 邓紫棋"));

        ArtistPlan plan = Plan(songs, parsed);

        ArtistGroup gem = Group(plan, "G.E.M. 邓紫棋");
        Assert.True(!gem.IsSuggestedByAi, "the library already writes the official credit");
        Assert.False(gem.IsLowConfidence);
        Assert.Equal(9, gem.SongCount);
        Assert.Equal(6, VariantCount(gem, "G.E.M. 邓紫棋"));
        Assert.Equal(4, VariantCount(gem, "G.E.M."));

        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);
        Assert.Equal("G.E.M. 邓紫棋", UpdateFor(updates, songs[5]).ArtistJoined); // was "G.E.M."
        Assert.Equal("G.E.M. 邓紫棋", UpdateFor(updates, songs[7]).ArtistJoined); // was "G.E.M.邓紫棋"
        SongArtistUpdate collaboration = UpdateFor(updates, songs[songs.Count - 1]);
        Assert.Equal(L("Namewee", "G.E.M. 邓紫棋"), collaboration.ArtistsList);
        Assert.True(UpdateFor(updates, songs[0]) == null, "already the official credit");
    }

    [Fact]
    public void AnOfficialChineseNameTheLibraryNeverWroteIsASuggestionButNotUnsure()
    {
        List<SongArtistTags> songs = Songs(Song("周杰倫"), Song("周杰倫"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "周杰倫", Chinese("周杰倫", "周杰伦"));

        ArtistGroup group = Plan(songs, parsed).Groups[0];

        Assert.Equal("周杰伦", group.CanonicalName);
        Assert.True(group.IsSuggestedByAi);
        Assert.True(!group.IsLowConfidence, "the source was sure of the official name");
    }

    [Fact]
    public void ATypoInTheLibraryDoesNotBeatTheCorrectedOfficialName()
    {
        List<SongArtistTags> songs = Songs(Song("Celing Dion"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Celing Dion", Ok("Celing Dion", "Celine Dion"));

        ArtistGroup group = Plan(songs, parsed).Groups[0];

        Assert.Equal("Celine Dion", group.CanonicalName);
        Assert.True(group.IsSuggestedByAi);
        Assert.Equal(L("Celing Dion", "Celine Dion"), VariantTexts(group));
    }

    [Fact]
    public void ADifferentCaseOfTheOfficialNameStillCountsAsTheSameSpelling()
    {
        List<SongArtistTags> songs = Songs(Repeat(3, "Itzy", null), Song("ITZY"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Itzy", Ok("Itzy", "ITZY"));
        Put(parsed, "ITZY", Ok("ITZY", "ITZY"));

        ArtistGroup group = Plan(songs, parsed).Groups[0];

        AssertEqual("Itzy", group.CanonicalName, "the most common spelling of the same name wins");
        Assert.False(group.IsSuggestedByAi);
    }

    [Fact]
    public void BuildPlanMergesRoseSpellingsAndKeepsTheMostCommon()
    {
        List<SongArtistTags> songs = Songs(Repeat(3, "ROSÉ", "ROSÉ"), Repeat(2, "Rose", null), Song("ROSE"),
                Song("ROSÉ", "로제 (ROSÉ)"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "ROSÉ", Ok("ROSÉ", "ROSÉ"));
        Put(parsed, "Rose", Ok("Rose", "ROSÉ"));
        Put(parsed, "ROSE", Ok("ROSE", "ROSÉ"));
        Put(parsed, "로제 (ROSÉ)", Ok("로제 (ROSÉ)", "ROSÉ"));

        ArtistPlan plan = Plan(songs, parsed);

        Assert.Single(plan.Groups);
        Assert.Equal("ROSÉ", plan.Groups[0].CanonicalName);
        Assert.Equal(7, plan.Groups[0].SongCount);
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);
        Assert.Equal("ROSÉ", UpdateFor(updates, songs[3]).ArtistJoined);
        Assert.Equal("ROSÉ", UpdateFor(updates, songs[5]).ArtistJoined);
        Assert.Equal("ROSÉ", UpdateFor(updates, songs[6]).AlbumArtistJoined);
    }

    [Fact]
    public void BuildPlanTieBreakPrefersTheSpellingTheAiProposed()
    {
        // ROSE and ROSÉ are used once each; the AI named the artist "ROSÉ", so that spelling wins the tie
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "ROSE", Ok("ROSE", "ROSÉ"));
        Put(parsed, "ROSÉ", Ok("ROSÉ", "ROSÉ"));
        ArtistPlan plan = Plan(Songs(Song("ROSE"), Song("ROSÉ")), parsed);
        Assert.Equal("ROSÉ", plan.Groups[0].CanonicalName);
    }

    [Fact]
    public void BuildPlanTieBreakThenPrefersTheShorterSpelling()
    {
        // none of the spellings is the proposed name "A.B.C.", all used once
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "abcde", Ok("abcde", "A.B.C."));
        Put(parsed, "abc", Ok("abc", "A.B.C."));
        Put(parsed, "abcd", Ok("abcd", "A.B.C."));
        ArtistPlan plan = Plan(Songs(Song("abcde"), Song("abc"), Song("abcd")), parsed);
        Assert.Single(plan.Groups);
        Assert.Equal("abc", plan.Groups[0].CanonicalName);
        Assert.False(plan.Groups[0].IsSuggestedByAi);
    }

    [Fact]
    public void BuildPlanTieBreakFinallyUsesPlainAlphabeticalOrder()
    {
        // equal count, equal length, none proposed: case-sensitive order, so "ABC" < "Abc" < "abc" whatever the input order
        string[][] orders = { new[] { "abc", "Abc", "ABC" }, new[] { "ABC", "abc", "Abc" }, new[] { "Abc", "ABC", "abc" } };
        foreach (string[] order in orders)
        {
            var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
            List<SongArtistTags> songs = new List<SongArtistTags>();
            foreach (string spelling in order)
            {
                Put(parsed, spelling, Ok(spelling, "A.B.C."));
                songs.Add(Song(spelling));
            }
            AssertEqual("ABC", Plan(songs, parsed).Groups[0].CanonicalName, "[" + string.Join(", ", order) + "]");
        }
    }

    [Fact]
    public void BuildPlanKdaAndKdaAreOneArtistAndTheMostCommonSpellingWins()
    {
        List<SongArtistTags> songs = Songs(Repeat(2, "K-DA", "K/DA"), Repeat(2, "K/DA", null));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "K-DA", Ok("K-DA", "K/DA"));
        Put(parsed, "K/DA", Ok("K/DA", "K/DA"));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.Single(plan.Groups);
        Assert.Equal("K/DA", plan.Groups[0].CanonicalName); // 4 uses vs 2
        Assert.Equal(4, plan.Groups[0].SongCount);
        Assert.Equal("K/DA", UpdateFor(ArtistStandardizer.ComputeUpdates(plan), songs[0]).ArtistJoined);
    }

    [Fact]
    public void BuildPlanIdleAndGidleAreOneArtistWhenTheAiSaysSoAndTheMostCommonWins()
    {
        List<SongArtistTags> songs = Songs(Repeat(3, "(G)I-DLE", "(G)I-DLE"), Repeat(2, "i-dle", "i-dle (아이들)"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "(G)I-DLE", Ok("(G)I-DLE", "(G)I-DLE"));
        Put(parsed, "i-dle", Ok("i-dle", "(G)I-DLE"));
        Put(parsed, "i-dle (아이들)", Ok("i-dle (아이들)", "(G)I-DLE"));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.Single(plan.Groups);
        Assert.Equal("(G)I-DLE", plan.Groups[0].CanonicalName);
        Assert.Equal("(G)I-DLE", UpdateFor(ArtistStandardizer.ComputeUpdates(plan), songs[3]).ArtistJoined);
    }

    [Fact]
    public void BuildPlanNeverMergesGroupsWhoseLooseKeysDiffer()
    {
        // MINNIE is a member of (G)I-DLE: related, but a separate artist. And without the AI's help
        // "i-dle" is not "(G)I-DLE" either.
        List<SongArtistTags> songs = Songs(Repeat(2, "(G)I-DLE", null), Repeat(2, "민니 ((여자)아이들)", "MINNIE"),
                Repeat(2, "i-dle", null));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "(G)I-DLE", Ok("(G)I-DLE", "(G)I-DLE"));
        Put(parsed, "민니 ((여자)아이들)", Ok("민니 ((여자)아이들)", "MINNIE"));
        Put(parsed, "MINNIE", Ok("MINNIE", "MINNIE"));
        Put(parsed, "i-dle", Ok("i-dle", "i-dle"));
        ArtistPlan plan = Plan(songs, parsed);

        Assert.Equal(3, plan.Groups.Count);
        Assert.Equal("MINNIE", Group(plan, "MINNIE").CanonicalName);
        Assert.Equal(2, Group(plan, "MINNIE").SongCount);
        Assert.Equal(2, Group(plan, "(G)I-DLE").SongCount);
        Assert.Equal(2, Group(plan, "i-dle").SongCount);
        Assert.NotEqual(Group(plan, "MINNIE").Key, Group(plan, "(G)I-DLE").Key);
        Assert.Single(plan.GroupKeysByRaw["민니 ((여자)아이들)"]);
    }

    [Fact]
    public void BuildPlanUsesTheAiNameWhenNoEnglishSpellingExists()
    {
        List<SongArtistTags> songs = Songs(Repeat(2, "Billlie (빌리)", "Billlie (빌리)"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Billlie (빌리)", Ok("Billlie (빌리)", "Billlie"));
        ArtistPlan plan = Plan(songs, parsed);

        ArtistGroup group = plan.Groups[0];
        Assert.Equal("Billlie", group.CanonicalName);
        Assert.True(group.IsSuggestedByAi);
        Assert.True(group.IsLowConfidence, "a suggestion is always worth a look, even if the parse was sure");
        Assert.Equal(4, VariantCount(group, "Billlie (빌리)"));
        AssertEqual(0, VariantCount(group, "Billlie"), "the proposed name is offered as a variant nobody wrote yet");

        SongArtistUpdate update = UpdateFor(ArtistStandardizer.ComputeUpdates(plan), songs[0]);
        Assert.Equal("Billlie", update.ArtistJoined);
        Assert.Equal("Billlie", update.AlbumArtistJoined);
    }

    [Fact]
    public void BuildPlanUsesTheMostFrequentlyProposedEnglishName()
    {
        List<SongArtistTags> songs = Songs(Song("아이유"), Song("아이유 (IU)"), Song("アイユ"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        // "IU" and "I.U." have the same loose key, so this is one artist; "IU" was proposed twice, "I.U." once
        Put(parsed, "아이유", Ok("아이유", "IU"));
        Put(parsed, "아이유 (IU)", Ok("아이유 (IU)", "I.U."));
        Put(parsed, "アイユ", Ok("アイユ", "IU"));
        ArtistPlan plan = Plan(songs, parsed);
        ArtistGroup group = plan.Groups[0];
        Assert.Single(plan.Groups);
        Assert.Equal("IU", group.CanonicalName);
        Assert.True(group.IsSuggestedByAi);
    }

    [Fact]
    public void BuildPlanFallsBackToTheFirstProposalWhenNothingIsEnglish()
    {
        List<SongArtistTags> songs = Songs(Repeat(2, "陈奕迅", null), Song("陳奕迅"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "陈奕迅", Unsure("陈奕迅", "陈奕迅"));
        Put(parsed, "陳奕迅", Unsure("陳奕迅", "陈奕迅"));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.Single(plan.Groups);
        ArtistGroup group = plan.Groups[0];
        Assert.Equal("陈奕迅", group.CanonicalName);
        Assert.True(group.IsLowConfidence);
        Assert.True(!group.IsSuggestedByAi, "the name is the library's own spelling, nothing was suggested");
    }

    [Fact]
    public void BuildPlanFoldsVariousArtistsSynonymsTogether()
    {
        List<SongArtistTags> songs = Songs(Song("V.A."), Repeat(3, "群星", null), Song("华语群星"),
                Repeat(4, "x", "Various Artists"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        foreach (string raw in new string[] { "V.A.", "群星", "华语群星", "Various Artists" })
        {
            Put(parsed, raw, Ok(raw, "Various Artists"));
        }
        Put(parsed, "x", Ok("x", "x"));
        ArtistPlan plan = Plan(songs, parsed);
        ArtistGroup group = Group(plan, "Various Artists");
        Assert.Equal(9, group.SongCount);
        Assert.Equal(4, VariantCount(group, "Various Artists"));
        Assert.Equal(3, VariantCount(group, "群星"));
        Assert.Equal(1, VariantCount(group, "V.A."));
        // "V.A." is English-only too, but the full name is simply more common
        Assert.False(group.IsSuggestedByAi);
    }

    [Fact]
    public void BuildPlanMakesMultiArtistSongsConsistentAcrossSongs()
    {
        SongArtistTags a = Song("Alan Walker/Au-Ra");
        SongArtistTags b = Song("alan walker & AU-RA");
        List<SongArtistTags> songs = Songs(a, b, Repeat(5, "Alan Walker", null), Repeat(2, "Au-Ra", null));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Alan Walker/Au-Ra", Ok("Alan Walker", "Alan Walker"), Ok("Au-Ra", "Au-Ra"));
        Put(parsed, "alan walker & AU-RA", Ok("alan walker", "Alan Walker"), Ok("AU-RA", "Au-Ra"));
        Put(parsed, "Alan Walker", Ok("Alan Walker", "Alan Walker"));
        Put(parsed, "Au-Ra", Ok("Au-Ra", "Au-Ra"));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(Plan(songs, parsed));

        Assert.Equal("Alan Walker; Au-Ra", UpdateFor(updates, a).ArtistJoined);
        Assert.Equal("Alan Walker; Au-Ra", UpdateFor(updates, b).ArtistJoined);
        Assert.Equal(L("Alan Walker", "Au-Ra"), UpdateFor(updates, a).ArtistsList);
        Assert.Equal(L("Alan Walker", "Au-Ra"), UpdateFor(updates, b).ArtistsList);
        AssertEqual(2, updates.Count, "the single-artist songs are already fine");
    }

    [Fact]
    public void BuildPlanRecordsGroupKeysByRawInOrderWithoutDuplicates()
    {
        List<SongArtistTags> songs = Songs(Song("A & B & a"), Song("B/A"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "A & B & a", Ok("A", "AAA"), Ok("B", "BBB"), Ok("a", "aaa"));
        Put(parsed, "B/A", Ok("B", "BBB"), Ok("A", "AAA"));
        ArtistPlan plan = Plan(songs, parsed);
        List<string> keys = plan.GroupKeysByRaw["A & B & a"];
        Assert.Equal(2, keys.Count);
        Assert.Equal("aaa", keys[0]);
        Assert.Equal("bbb", keys[1]);
        Assert.Equal(L("bbb", "aaa"), plan.GroupKeysByRaw["B/A"]);
        Assert.Equal(2, plan.Groups.Count);
    }

    [Fact]
    public void BuildPlanFallsBackToTheOtherFieldWhenTextOrEnglishIsBlank()
    {
        List<SongArtistTags> songs = Songs(Song("raw one"), Song("raw two"), Song("raw three"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "raw one", Ok("IU", ""));
        Put(parsed, "raw two", Ok("   ", "IU"));
        Put(parsed, "raw three", Ok("\u00A0IU\u00A0", null));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.Single(plan.Groups);
        Assert.Equal("IU", plan.Groups[0].CanonicalName);
        Assert.Equal(3, plan.Groups[0].SongCount);
        Assert.Equal(3, VariantCount(plan.Groups[0], "IU"));
    }

    [Fact]
    public void BuildPlanUsesAnArtistNamedOnlyWithSymbols()
    {
        List<SongArtistTags> songs = Songs(Song("!!!"), Song("???"), Song("!!!"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "!!!", Ok("!!!", "!!!"));
        Put(parsed, "???", Ok("???", "???"));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.Equal(2, plan.Groups.Count);
        Assert.Equal("!!!", plan.Groups[0].CanonicalName);
        Assert.Equal(2, plan.Groups[0].SongCount);
        Assert.Equal("???", plan.Groups[1].CanonicalName);
        Assert.True(ArtistStandardizer.ComputeUpdates(plan).Count == 0);
    }

    [Fact]
    public void BuildPlanFlagsLowConfidenceFromAnyContributor()
    {
        List<SongArtistTags> songs = Songs(Song("Foo"), Song("foo"), Song("Bar"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Foo", Ok("Foo", "Foo"));
        Put(parsed, "foo", Unsure("foo", "Foo"));
        Put(parsed, "Bar", Ok("Bar", "Bar"));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.True(Group(plan, "Foo").IsLowConfidence);
        Assert.False(Group(plan, "Foo").IsSuggestedByAi);
        Assert.False(Group(plan, "Bar").IsLowConfidence);
    }

    [Fact]
    public void BuildPlanSortsGroupsBySongCountThenName()
    {
        List<SongArtistTags> songs = Songs(Repeat(3, "Zed", null), Repeat(1, "beta", null), Repeat(1, "Alpha", null),
                Repeat(1, "alpha2", null), Repeat(2, "Mid", null));
        ArtistPlan plan = ArtistStandardizer.BuildPlan(songs, new Dictionary<string, List<ParsedArtist>>(), null, false);
        List<string> names = plan.Groups.Select(g => g.CanonicalName).ToList();
        Assert.Equal(L("Zed", "Mid", "Alpha", "alpha2", "beta"), names);
    }

    [Fact]
    public void BuildPlanCountsEachSongOncePerArtist()
    {
        // the same artist in ARTIST, ALBUMARTIST and ARTISTS (and twice in ARTIST) is still one song
        List<SongArtistTags> songs = new List<SongArtistTags> { Song(L("IU", "IU"), L("IU"), L("IU", "IU")) };
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.Equal(1, plan.Groups[0].SongCount);
        Assert.Equal(5, VariantCount(plan.Groups[0], "IU"));
    }

    [Fact]
    public void BuildPlanCountsAnArtistNamedTwiceInOneValueOnce()
    {
        List<SongArtistTags> songs = Songs(Song("Alan Walker/Alan Walker"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Alan Walker/Alan Walker", Ok("Alan Walker", "Alan Walker"), Ok("Alan Walker", "Alan Walker"));
        ArtistPlan plan = Plan(songs, parsed);
        Assert.Equal(1, VariantCount(plan.Groups[0], "Alan Walker"));
        Assert.Equal(L(plan.Groups[0].Key), plan.GroupKeysByRaw["Alan Walker/Alan Walker"]);
    }

    [Fact]
    public void BuildPlanKeepsSeparatorAndFlag()
    {
        ArtistPlan plan = ArtistStandardizer.BuildPlan(new List<SongArtistTags> { Song("IU") },
                new Dictionary<string, List<ParsedArtist>>(), " / ", true);
        Assert.Equal(" / ", plan.Separator);
        Assert.True(plan.IsUsedAi);
        plan = ArtistStandardizer.BuildPlan(new List<SongArtistTags> { Song("IU") },
                new Dictionary<string, List<ParsedArtist>>(), null, false);
        Assert.Equal(ArtistStandardizer.DefaultSeparator, plan.Separator);
        Assert.False(plan.IsUsedAi);
        plan = ArtistStandardizer.BuildPlan(new List<SongArtistTags> { Song("IU") },
                new Dictionary<string, List<ParsedArtist>>(), "", false);
        Assert.Equal(ArtistStandardizer.DefaultSeparator, plan.Separator);
    }

    // ------------------------------------------------------------------ buildPlan: partial and broken parses

    [Fact]
    public void BuildPlanParsesLocallyWhatTheAiSkippedAndWarns()
    {
        List<SongArtistTags> songs = Songs(Song("IU"), Song("TZUYU & pH-1"), Song("Foo; Bar"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        ArtistPlan plan = Plan(songs, parsed);

        Assert.Equal(5, plan.Groups.Count); // IU, TZUYU, pH-1, Foo, Bar
        Assert.Single(plan.Warnings);
        Assert.True(plan.Warnings[0].Contains("2 ", StringComparison.Ordinal), plan.Warnings[0]);
        Assert.True(plan.Warnings[0].Contains("TZUYU & pH-1", StringComparison.Ordinal), plan.Warnings[0]);
        Assert.Equal(2, plan.GroupKeysByRaw["TZUYU & pH-1"].Count);
    }

    [Fact]
    public void BuildPlanTreatsAnEmptyOrUnusableAnswerAsMissing()
    {
        List<SongArtistTags> songs = Songs(Song("A one"), Song("B two"), Song("C three"), Song("D four"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        parsed["A one"] = new List<ParsedArtist>(); // would otherwise make the artist vanish from the update
        parsed["B two"] = null;
        parsed["C three"] = new List<ParsedArtist> { null, Ok("  ", " ") };
        Put(parsed, "D four", Ok("D four", "D four"));
        ArtistPlan plan = Plan(songs, parsed);

        Assert.Equal(4, plan.Groups.Count);
        Assert.Single(plan.Warnings);
        Assert.True(plan.Warnings[0].Contains("3 ", StringComparison.Ordinal), plan.Warnings[0]);
        foreach (List<string> keys in plan.GroupKeysByRaw.Values)
        {
            Assert.Single(keys);
        }
        Assert.True(ArtistStandardizer.ComputeUpdates(plan).Count == 0);
    }

    [Fact]
    public void BuildPlanIgnoresAnswersForValuesNotInTheLibraryAndNullEntries()
    {
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        Put(parsed, "never used", Ok("never used", "never used"));
        // Java: parsed.put(null, ...) -- a .NET dictionary has no null key, so there is no such entry to ignore
        Put(parsed, "   ", Ok("x", "x"));
        ArtistPlan plan = Plan(new List<SongArtistTags> { Song("IU") }, parsed);
        Assert.Single(plan.Groups);
        Assert.True(plan.Warnings.Count == 0);
    }

    [Fact]
    public void BuildPlanWithNoParseAtAllStillWorks()
    {
        List<SongArtistTags> songs = Songs(Song("ITZY/彩领", "ITZY (있지)"), Song("Itzy"));
        ArtistPlan fromNull = ArtistStandardizer.BuildPlan(songs, null, "; ", false);
        ArtistPlan fromEmpty = ArtistStandardizer.BuildPlan(songs, new Dictionary<string, List<ParsedArtist>>(), "; ", false);
        foreach (ArtistPlan plan in new List<ArtistPlan> { fromNull, fromEmpty })
        {
            Assert.Single(plan.Warnings);
            Assert.Equal("ITZY", Group(plan, "ITZY").CanonicalName);
            Assert.Equal(2, Group(plan, "ITZY").SongCount);
            Assert.Equal("ITZY; 彩领", UpdateFor(ArtistStandardizer.ComputeUpdates(plan), songs[0]).ArtistJoined);
        }
    }

    // ------------------------------------------------------------------ buildPlan: empty input

    [Fact]
    public void BuildPlanAcceptsNullAndEmptyInput()
    {
        ArtistPlan fromNull = ArtistStandardizer.BuildPlan(null, null, null, false);
        Assert.True(fromNull.Groups.Count == 0);
        Assert.True(fromNull.GroupKeysByRaw.Count == 0);
        Assert.True(fromNull.Warnings.Count == 0);
        Assert.NotNull(fromNull.Songs);
        Assert.True(ArtistStandardizer.ComputeUpdates(fromNull).Count == 0);

        ArtistPlan fromEmpty = ArtistStandardizer.BuildPlan(new List<SongArtistTags>(),
                new Dictionary<string, List<ParsedArtist>>(), ";", true);
        Assert.True(fromEmpty.Groups.Count == 0);
        Assert.True(ArtistStandardizer.ComputeUpdates(fromEmpty).Count == 0);
    }

    [Fact]
    public void BuildPlanIgnoresSongsWithoutAnyArtistInformation()
    {
        List<SongArtistTags> songs = new List<SongArtistTags>();
        songs.Add(null);
        songs.Add(Song(L(), L(), L()));
        songs.Add(Song(L("", "  "), L("\u200B"), L("\t")));
        ArtistPlan plan = ArtistStandardizer.BuildPlan(songs, new Dictionary<string, List<ParsedArtist>>(), null, false);
        Assert.True(plan.Groups.Count == 0);
        Assert.True(plan.Warnings.Count == 0);
        Assert.True(ArtistStandardizer.ComputeUpdates(plan).Count == 0);
    }

    [Fact]
    public void ComputeUpdatesAcceptsNull()
    {
        Assert.True(ArtistStandardizer.ComputeUpdates(null).Count == 0);
    }

    // ------------------------------------------------------------------ computeUpdates

    [Fact]
    public void ComputeUpdatesProducesNothingWhenEverythingIsAlreadyStandard()
    {
        List<SongArtistTags> songs = new List<SongArtistTags>
        {
            Song("IU", "IU"), Song(L("Stray Kids"), L("Stray Kids"), L("Stray Kids")),
            Song(L("A; B"), L("A; B"), L("A", "B")), Song("Billlie"),
        };
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        Put(parsed, "Stray Kids", Ok("Stray Kids", "Stray Kids"));
        Put(parsed, "A; B", Ok("A", "A"), Ok("B", "B"));
        Put(parsed, "A", Ok("A", "A"));
        Put(parsed, "B", Ok("B", "B"));
        Put(parsed, "Billlie", Ok("Billlie", "Billlie"));
        Assert.True(ArtistStandardizer.ComputeUpdates(Plan(songs, parsed)).Count == 0);
    }

    [Fact]
    public void ComputeUpdatesRewritesMultiValuedArtistAsOneJoinedValueAndFillsArtists()
    {
        SongArtistTags song = Song(L("HUNTR/X", "EJAE", "AUDREY NUNA", "REI AMI", "KPop Demon Hunters Cast"),
                L("KPop Demon Hunters Cast, HUNTR/X, Saja Boys"), L());
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        foreach (string name in new string[] { "HUNTR/X", "EJAE", "AUDREY NUNA", "REI AMI", "KPop Demon Hunters Cast" })
        {
            Put(parsed, name, Ok(name, name));
        }
        Put(parsed, "KPop Demon Hunters Cast, HUNTR/X, Saja Boys", Ok("KPop Demon Hunters Cast", "KPop Demon Hunters Cast"),
                Ok("HUNTR/X", "HUNTR/X"), Ok("Saja Boys", "Saja Boys"));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(Plan(new List<SongArtistTags> { song }, parsed));

        Assert.Single(updates);
        SongArtistUpdate update = updates[0];
        Assert.Equal(song.Path, update.AudioPath);
        Assert.Equal("HUNTR/X; EJAE; AUDREY NUNA; REI AMI; KPop Demon Hunters Cast", update.ArtistJoined);
        Assert.Equal(L("HUNTR/X", "EJAE", "AUDREY NUNA", "REI AMI", "KPop Demon Hunters Cast"), update.ArtistsList);
        Assert.Equal("KPop Demon Hunters Cast; HUNTR/X; Saja Boys", update.AlbumArtistJoined);

        Assert.Equal(3, update.Changes.Count);
        Assert.Equal("HUNTR/X | EJAE | AUDREY NUNA | REI AMI | KPop Demon Hunters Cast", Change(update, "ARTIST").Before);
        Assert.Equal("HUNTR/X; EJAE; AUDREY NUNA; REI AMI; KPop Demon Hunters Cast", Change(update, "ARTIST").After);
        Assert.Equal("", Change(update, "ARTISTS").Before);
        Assert.Equal("HUNTR/X | EJAE | AUDREY NUNA | REI AMI | KPop Demon Hunters Cast", Change(update, "ARTISTS").After);
        Assert.Equal("KPop Demon Hunters Cast, HUNTR/X, Saja Boys", Change(update, "ALBUMARTIST").Before);
    }

    [Fact]
    public void ComputeUpdatesDropsDuplicateArtistsInsideOneSongIgnoringCase()
    {
        SongArtistTags twoValues = Song(L("IU", "iu"), L(), L());
        SongArtistTags oneValue = Song("ITZY/Itzy");
        SongArtistTags mixed = Song(L("Foo", "Bar", "foo", "BAR", "Baz"), L(), L());
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        Put(parsed, "iu", Ok("iu", "IU"));
        Put(parsed, "ITZY/Itzy", Ok("ITZY", "ITZY"), Ok("Itzy", "ITZY"));
        Put(parsed, "Foo", Ok("Foo", "Foo"));
        Put(parsed, "foo", Ok("foo", "Foo"));
        Put(parsed, "Bar", Ok("Bar", "Bar"));
        Put(parsed, "BAR", Ok("BAR", "Bar"));
        Put(parsed, "Baz", Ok("Baz", "Baz"));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(
                Plan(new List<SongArtistTags> { twoValues, oneValue, mixed }, parsed));

        SongArtistUpdate first = UpdateFor(updates, twoValues);
        Assert.Equal("IU", first.ArtistJoined);
        Assert.True(first.ArtistsList == null, "one artist and no ARTISTS field: nothing to write there");
        Assert.Equal("ITZY", UpdateFor(updates, oneValue).ArtistJoined);
        SongArtistUpdate third = UpdateFor(updates, mixed);
        Assert.Equal("Foo; Bar; Baz", third.ArtistJoined);
        Assert.Equal(L("Foo", "Bar", "Baz"), third.ArtistsList);
    }

    [Fact]
    public void ComputeUpdatesAppliesTheArtistsRules()
    {
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        Put(parsed, "A & B", Ok("A", "A"), Ok("B", "B"));
        Put(parsed, "B & A", Ok("B", "B"), Ok("A", "A"));
        Put(parsed, "old", Ok("old", "old"));
        Put(parsed, "A", Ok("A", "A"));
        Put(parsed, "B", Ok("B", "B"));

        SongArtistTags oneNameNoArtists = Song(L("IU"), L(), L());
        SongArtistTags oneNameStaleArtists = Song(L("IU"), L(), L("old"));
        SongArtistTags oneNameCurrentArtists = Song(L("IU"), L(), L("IU"));
        SongArtistTags twoNamesSameList = Song(L("A; B"), L(), L("A", "B"));
        SongArtistTags twoNamesOtherOrder = Song(L("A; B"), L(), L("B", "A"));
        SongArtistTags twoNamesNoArtists = Song(L("A & B"), L(), L());
        SongArtistTags albumArtistOnly = Song(L(), L("B & A"), L());
        SongArtistTags nothingToGoOn = Song(L(), L(), L());
        List<SongArtistTags> songs = new List<SongArtistTags> { oneNameNoArtists, oneNameStaleArtists,
                oneNameCurrentArtists, twoNamesSameList, twoNamesOtherOrder, twoNamesNoArtists, albumArtistOnly,
                nothingToGoOn };
        parsed["A; B"] = new List<ParsedArtist> { Ok("A", "A"), Ok("B", "B") };
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(Plan(songs, parsed));

        Assert.Null(UpdateFor(updates, oneNameNoArtists));
        Assert.Equal(L("IU"), UpdateFor(updates, oneNameStaleArtists).ArtistsList);
        Assert.Null(UpdateFor(updates, oneNameStaleArtists).ArtistJoined);
        Assert.Equal("old", Change(UpdateFor(updates, oneNameStaleArtists), "ARTISTS").Before);
        Assert.Null(UpdateFor(updates, oneNameCurrentArtists));
        Assert.Null(UpdateFor(updates, twoNamesSameList));
        Assert.Equal(L("A", "B"), UpdateFor(updates, twoNamesOtherOrder).ArtistsList);
        Assert.Equal("B | A", Change(UpdateFor(updates, twoNamesOtherOrder), "ARTISTS").Before);
        Assert.Equal(L("A", "B"), UpdateFor(updates, twoNamesNoArtists).ArtistsList);
        Assert.Equal("A; B", UpdateFor(updates, twoNamesNoArtists).ArtistJoined);
        // without ARTIST the song's ARTISTS come from ALBUMARTIST, and ARTIST itself is left alone
        SongArtistUpdate fromAlbum = UpdateFor(updates, albumArtistOnly);
        Assert.Null(fromAlbum.ArtistJoined);
        Assert.Equal(L("B", "A"), fromAlbum.ArtistsList);
        Assert.Equal("B; A", fromAlbum.AlbumArtistJoined);
        Assert.Null(UpdateFor(updates, nothingToGoOn));
    }

    [Fact]
    public void ComputeUpdatesLeavesArtistAloneWhenTheSongHasNone()
    {
        SongArtistTags song = Song(L(), L("Itzy"), L());
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Itzy", Ok("Itzy", "ITZY"));
        Put(parsed, "ITZY", Ok("ITZY", "ITZY"));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(Plan(Songs(song, Repeat(3, "ITZY", null)), parsed));
        SongArtistUpdate update = UpdateFor(updates, song);
        Assert.Null(update.ArtistJoined);
        Assert.Null(update.ArtistsList);
        Assert.Equal("ITZY", update.AlbumArtistJoined);
        Assert.Single(update.Changes);
    }

    [Fact]
    public void ComputeUpdatesRewritesArtistsOnlySongsWithTheirStandardNames()
    {
        SongArtistTags song = Song(L(), L(), L("aespa", "Aespa", "karina"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "aespa", Ok("aespa", "aespa"));
        Put(parsed, "Aespa", Ok("Aespa", "aespa"));
        Put(parsed, "karina", Ok("karina", "KARINA"));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(Plan(new List<SongArtistTags> { song }, parsed));
        Assert.Single(updates);
        Assert.Equal(L("aespa", "karina"), updates[0].ArtistsList);
        Assert.Null(updates[0].ArtistJoined);
    }

    [Fact]
    public void ComputeUpdatesHonoursCanonicalNamesEditedAfterBuildPlan()
    {
        List<SongArtistTags> songs = Songs(Song("ITZY", "ITZY (있지)"), Song("Itzy"), Song("ITZY/Lia"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "ITZY", Ok("ITZY", "ITZY"));
        Put(parsed, "Itzy", Ok("Itzy", "ITZY"));
        Put(parsed, "ITZY (있지)", Ok("ITZY (있지)", "ITZY"));
        Put(parsed, "ITZY/Lia", Ok("ITZY", "ITZY"), Ok("Lia", "Lia"));
        ArtistPlan plan = Plan(songs, parsed);

        Assert.Equal("ITZY", UpdateFor(ArtistStandardizer.ComputeUpdates(plan), songs[1]).ArtistJoined);

        Group(plan, "ITZY").CanonicalName = "  Itzy\u00A0 ";
        List<SongArtistUpdate> edited = ArtistStandardizer.ComputeUpdates(plan);
        Assert.True(UpdateFor(edited, songs[1]) == null, "Itzy is now the standard spelling");
        Assert.Equal("Itzy", UpdateFor(edited, songs[0]).ArtistJoined);
        Assert.Equal("Itzy", UpdateFor(edited, songs[0]).AlbumArtistJoined);
        Assert.Equal("Itzy; Lia", UpdateFor(edited, songs[2]).ArtistJoined);
        Assert.Equal(L("Itzy", "Lia"), UpdateFor(edited, songs[2]).ArtistsList);

        Group(plan, "Lia").CanonicalName = "리아";
        Assert.Equal("Itzy; 리아", UpdateFor(ArtistStandardizer.ComputeUpdates(plan), songs[2]).ArtistJoined);
    }

    [Fact]
    public void ComputeUpdatesTreatsTwoGroupsEditedToTheSameNameAsOneArtist()
    {
        SongArtistTags song = Song("Foo/Bar");
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Foo/Bar", Ok("Foo", "Foo"), Ok("Bar", "Bar"));
        ArtistPlan plan = Plan(new List<SongArtistTags> { song }, parsed);
        Group(plan, "Bar").CanonicalName = "foo";
        SongArtistUpdate update = ArtistStandardizer.ComputeUpdates(plan)[0];
        Assert.Equal("Foo", update.ArtistJoined);
        Assert.Null(update.ArtistsList);
    }

    [Fact]
    public void ComputeUpdatesNeverErasesAnArtistWhoseNameWasCleared()
    {
        SongArtistTags odd = Song("Itzy");
        List<SongArtistTags> songs = Songs(Repeat(3, "ITZY", null), odd);
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "ITZY", Ok("ITZY", "ITZY"));
        Put(parsed, "Itzy", Ok("Itzy", "ITZY"));
        ArtistPlan plan = Plan(songs, parsed);
        foreach (string cleared in new string[] { "   ", "", null })
        {
            plan.Groups[0].CanonicalName = cleared;
            // falls back to the artist's most common variant instead of writing an empty ARTIST
            List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);
            Assert.Single(updates);
            Assert.Equal("ITZY", UpdateFor(updates, odd).ArtistJoined);
        }
    }

    [Fact]
    public void ComputeUpdatesUsesTheCustomSeparator()
    {
        SongArtistTags song = Song("A & B");
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "A & B", Ok("A", "A"), Ok("B", "B"));
        ArtistPlan plan = ArtistStandardizer.BuildPlan(new List<SongArtistTags> { song }, parsed, " / ", true);
        SongArtistUpdate update = ArtistStandardizer.ComputeUpdates(plan)[0];
        Assert.Equal("A / B", update.ArtistJoined);
        Assert.Equal(L("A", "B"), update.ArtistsList);
        AssertEqual("A | B", Change(update, "ARTISTS").After, "the preview always uses ' | '");
        // a song already written with that separator is a no-op
        SongArtistTags standard = Song(L("A / B"), L(), L("A", "B"));
        Assert.True(ArtistStandardizer.ComputeUpdates(ArtistStandardizer.BuildPlan(new List<SongArtistTags> { standard },
                new Dictionary<string, List<ParsedArtist>>
                {
                    { "A / B", new List<ParsedArtist> { Ok("A", "A"), Ok("B", "B") } },
                    { "A", new List<ParsedArtist> { Ok("A", "A") } },
                    { "B", new List<ParsedArtist> { Ok("B", "B") } },
                },
                " / ", true)).Count == 0);
    }

    [Fact]
    public void ComputeUpdatesKeepsValuesThePlanDoesNotKnow()
    {
        // a plan whose raw map lacks a value (e.g. the tags changed between analysis and apply)
        SongArtistTags song = Song(L("  Foo   Bar "), L("Foo Bar"), L("Foo  Bar", "Baz"));
        ArtistPlan plan = new ArtistPlan("; ", false, new List<ArtistGroup>(), new Dictionary<string, List<string>>(),
                new List<SongArtistTags> { song }, new List<string>());
        SongArtistUpdate update = ArtistStandardizer.ComputeUpdates(plan)[0];
        Assert.Equal("Foo Bar", update.ArtistJoined);
        Assert.Null(update.AlbumArtistJoined);
        // ARTISTS is always rebuilt from ARTIST (one name, and the song already has ARTISTS)
        Assert.Equal(L("Foo Bar"), update.ArtistsList);
        // a plan without groups or raw map at all still works, and keeps what it does not know
        ArtistPlan bare = new ArtistPlan(null, false, null, null, new List<SongArtistTags> { Song(L("Foo Bar"), L(), L()) }, null);
        Assert.True(ArtistStandardizer.ComputeUpdates(bare).Count == 0);
    }

    [Fact]
    public void ComputeUpdatesDoesNotModifyTheSongsOrThePlan()
    {
        SongArtistTags song = Song(L("Itzy", "ITZY"), L("ITZY (있지)"), L("Itzy"));
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "Itzy", Ok("Itzy", "ITZY"));
        Put(parsed, "ITZY", Ok("ITZY", "ITZY"));
        Put(parsed, "ITZY (있지)", Ok("ITZY (있지)", "ITZY"));
        ArtistPlan plan = Plan(new List<SongArtistTags> { song }, parsed);
        List<SongArtistUpdate> first = ArtistStandardizer.ComputeUpdates(plan);
        List<SongArtistUpdate> second = ArtistStandardizer.ComputeUpdates(plan);
        Assert.Equal(L("Itzy", "ITZY"), song.Artist);
        Assert.Equal(L("ITZY (있지)"), song.AlbumArtist);
        Assert.Equal(L("Itzy"), song.Artists);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].ArtistJoined, second[0].ArtistJoined);
        Assert.Equal(first[0].ArtistsList, second[0].ArtistsList);
    }

    [Fact]
    public void ComputeUpdatesWritesOnlyTheFieldsThatChange()
    {
        SongArtistTags song = Song("IU", "아이유 (IU)");
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        Put(parsed, "아이유 (IU)", Ok("아이유 (IU)", "IU"));
        SongArtistUpdate update = ArtistStandardizer.ComputeUpdates(Plan(new List<SongArtistTags> { song }, parsed))[0];
        Assert.Null(update.ArtistJoined);
        Assert.Null(update.ArtistsList);
        Assert.Equal("IU", update.AlbumArtistJoined);
        Assert.Single(update.Changes);
        Assert.Equal("ALBUMARTIST", update.Changes[0].Field);
        Assert.Equal("아이유 (IU)", update.Changes[0].Before);
        Assert.Equal("IU", update.Changes[0].After);
    }

    [Fact]
    public void ComputeUpdatesComparesTheCurrentValueExactly()
    {
        // a trailing space, a different case or an extra empty value all count as "not standard yet"
        SongArtistTags trailing = Song("IU ");
        SongArtistTags emptyExtra = Song(L("IU", ""), L(), L());
        SongArtistTags exact = Song("IU");
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "IU", Ok("IU", "IU"));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(
                Plan(new List<SongArtistTags> { trailing, emptyExtra, exact }, parsed));
        Assert.Equal("IU", UpdateFor(updates, trailing).ArtistJoined);
        Assert.Equal("IU", UpdateFor(updates, emptyExtra).ArtistJoined);
        Assert.Equal("IU | ", Change(UpdateFor(updates, emptyExtra), "ARTIST").Before);
        Assert.Null(UpdateFor(updates, exact));
    }

    [Fact]
    public void ComputeUpdatesIsIdempotentOnceTheUpdatesAreWritten()
    {
        List<SongArtistTags> songs = Songs(
                Song("ITZY/彩领", "ITZY (있지)"), Song("Itzy", "Itzy"), Song("NewJeans  "),
                Song("G.E.M. 邓紫棋", "G.E.M."), Song("G.E.M.邓紫棋"),
                Song("K/DA & League of Legends & i-dle (아이들)", "K/DA & League of Legends"),
                Song("Tyler, The Creator & Frank Ocean"), Song(L("A", "B", "C"), L("A & B"), L("A")),
                Song("Alan Walker/Au-Ra", "Alan Walker"), Song("Lil Nas X x Jack Harlow"),
                Song("周残廷[重复]"), Song("민니 ((여자)아이들)", "MINNIE"),
                Song(L(), L("Hall & Oates"), L()), Song(L(), L(), L("aespa", "Aespa")));
        foreach (bool ai in new bool[] { false, true })
        {
            var counts = ArtistStandardizer.CountRawValues(songs);
            ArtistPlan plan = ArtistStandardizer.BuildPlan(songs, ArtistStandardizer.ParseLocally(counts.Keys), "; ", ai);
            List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);
            Assert.False(updates.Count == 0);

            List<SongArtistTags> written = new List<SongArtistTags>();
            foreach (SongArtistTags song in songs)
            {
                written.Add(Apply(song, UpdateFor(updates, song)));
            }
            var countsAfter = ArtistStandardizer.CountRawValues(written);
            ArtistPlan second = ArtistStandardizer.BuildPlan(written, ArtistStandardizer.ParseLocally(countsAfter.Keys), "; ", ai);
            List<SongArtistUpdate> again = ArtistStandardizer.ComputeUpdates(second);
            Assert.True(again.Count == 0, "second run changes: [" + string.Join(", ", again.Select(u => u.AudioPath + "["
                    + string.Join(", ", u.Changes.Select(c => c.Field + ":" + c.Before + "->" + c.After)) + "]")) + "]");
        }
    }

    // ------------------------------------------------------------------ robustness and speed

    [Fact]
    public void ParseLocallyAndPlanSurviveRandomGarbage()
    {
        string[] atoms = { "IU", "Itzy", "ITZY", "K/DA", "K-DA", "(G)I-DLE", "i-dle", "민니", "G.E.M.", "邓紫棋",
                "ROSÉ", "Rose", " ", "  ", "\u00A0", "x", "X", "&", ";", "/", ",", " / ", " feat. ", " ft. ", "[重复]",
                "(아이들)", "(", ")", "((", "))", "、", "，", "\u200B", "\uFEFF", "e\u0301", "🎵", "\uD83C",
                "\0", "\t", "\r\n", "1", "A", "Tyler, The Creator", "AC/DC", "Hall & Oates", "!!!" };
        JavaRandom random = new JavaRandom(42);
        List<string> raws = new List<string>();
        for (int i = 0; i < 4000; i++)
        {
            StringBuilder sb = new StringBuilder();
            int parts = 1 + random.NextInt(8);
            for (int p = 0; p < parts; p++)
            {
                sb.Append(atoms[random.NextInt(atoms.Length)]);
            }
            raws.Add(sb.ToString());
        }

        foreach (string raw in raws)
        {
            string once = ArtistStandardizer.NormalizeRaw(raw);
            AssertEqual(once, ArtistStandardizer.NormalizeRaw(once), "normalizeRaw must be idempotent");
        }
        var parsed = ArtistStandardizer.ParseLocally(raws);
        foreach (KeyValuePair<string, List<ParsedArtist>> entry in parsed)
        {
            Assert.False(entry.Value.Count == 0, entry.Key);
            Assert.Equal(entry.Key, ArtistStandardizer.NormalizeRaw(entry.Key));
            foreach (ParsedArtist artist in entry.Value)
            {
                Assert.False(string.IsNullOrWhiteSpace(artist.Text), entry.Key);
                Assert.False(string.IsNullOrWhiteSpace(artist.English), entry.Key);
                Assert.True(entry.Key.Contains(artist.Text, StringComparison.Ordinal), artist.Text + " is not part of " + entry.Key);
            }
        }

        List<SongArtistTags> songs = new List<SongArtistTags>();
        for (int i = 0; i < raws.Count; i += 4)
        {
            songs.Add(Song(L(raws[i]), L(raws[i + 1]), L(raws[i + 2], raws[i + 3])));
        }
        ArtistPlan plan = ArtistStandardizer.BuildPlan(songs, parsed, "; ", false);
        foreach (ArtistGroup group in plan.Groups)
        {
            Assert.False(string.IsNullOrWhiteSpace(group.CanonicalName));
            Assert.Equal(ArtistStandardizer.NormalizeRaw(group.CanonicalName), group.CanonicalName);
            Assert.True(group.SongCount >= 1);
            Assert.False(group.Variants.Count == 0);
        }
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);
        foreach (SongArtistUpdate update in updates)
        {
            Assert.False(update.Changes.Count == 0);
            if (update.ArtistJoined != null)
            {
                Assert.False(string.IsNullOrWhiteSpace(update.ArtistJoined));
            }
            if (update.ArtistsList != null)
            {
                Assert.False(update.ArtistsList.Count == 0);
            }
        }
        // same input, same answer
        ArtistPlan again = ArtistStandardizer.BuildPlan(songs, parsed, "; ", false);
        Assert.Equal(plan.Groups.Select(g => g.CanonicalName).ToList(),
                again.Groups.Select(g => g.CanonicalName).ToList());
        Assert.Equal(updates.Count, ArtistStandardizer.ComputeUpdates(again).Count);
    }

    [Fact]
    public void BuildPlanAndUpdatesForTwentyThousandSongsAreFast()
    {
        JavaRandom random = new JavaRandom(7);
        string[] spellings = new string[600];
        for (int i = 0; i < spellings.Length; i++)
        {
            spellings[i] = i % 3 == 0 ? "Artist " + (i / 3) : i % 3 == 1 ? "artist " + (i / 3) : "아티스트 (Artist " + (i / 3) + ")";
        }
        List<SongArtistTags> songs = new List<SongArtistTags>();
        for (int i = 0; i < 20_000; i++)
        {
            string first = spellings[random.NextInt(spellings.Length)];
            string artist = random.NextInt(4) == 0 ? first + "/" + spellings[random.NextInt(spellings.Length)] : first;
            songs.Add(Song(L(artist), L(spellings[random.NextInt(spellings.Length)]), L()));
        }
        var counts = ArtistStandardizer.CountRawValues(songs);
        var parsed = ArtistStandardizer.ParseLocally(counts.Keys);

        ArtistPlan plan = AssertTimeoutPreemptively(TimeSpan.FromSeconds(2),
                () => ArtistStandardizer.BuildPlan(songs, parsed, "; ", false));
        List<SongArtistUpdate> updates = AssertTimeoutPreemptively(TimeSpan.FromSeconds(2),
                () => ArtistStandardizer.ComputeUpdates(plan));
        Assert.Equal(200, plan.Groups.Count);
        Assert.True(updates.Count > 1000);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        ArtistStandardizer.ComputeUpdates(plan);
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < 1000, "recomputing after an edit must feel instant");
    }

    // ------------------------------------------------------------------ scope (only some songs may change)

    private static Dictionary<string, List<ParsedArtist>> AespaParses()
    {
        var parsed = new Dictionary<string, List<ParsedArtist>>();
        Put(parsed, "aespa", Ok("aespa", "aespa"));
        Put(parsed, "에스파 (aespa)", Ok("에스파 (aespa)", "aespa"));
        return parsed;
    }

    private static HashSet<string> PathsOf(params SongArtistTags[] songs)
    {
        HashSet<string> paths = new HashSet<string>();
        foreach (SongArtistTags song in songs)
        {
            paths.Add(song.Path);
        }
        return paths;
    }

    [Fact]
    public void WithoutAScopeEverySongMayChange()
    {
        SongArtistTags plain = Song("aespa");
        SongArtistTags korean = Song("에스파 (aespa)");
        SongArtistTags koreanToo = Song("에스파 (aespa)");

        ArtistPlan plan = Plan(Songs(plain, korean, koreanToo), AespaParses());

        Assert.Null(plan.Scope);
        Assert.Equal(3, plan.ScopedSongCount);
        Assert.True(plan.IsInScope(plain));
        Assert.Equal(2, ArtistStandardizer.ComputeUpdates(plan).Count);
    }

    [Fact]
    public void OnlyTheSongsInTheScopeAreUpdated()
    {
        SongArtistTags plain = Song("aespa");
        SongArtistTags korean1 = Song("에스파 (aespa)");
        SongArtistTags korean2 = Song("에스파 (aespa)");

        ArtistPlan plan = ArtistStandardizer.BuildPlan(Songs(plain, korean1, korean2), AespaParses(), "; ", true,
                PathsOf(korean1));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);

        Assert.Single(updates);
        Assert.Equal(korean1.Path, updates[0].AudioPath);
        Assert.Equal("aespa", updates[0].ArtistJoined);
        Assert.True(UpdateFor(updates, korean2) == null, "inconsistent too, but not selected");
    }

    [Fact]
    public void TheNamesStillComeFromTheWholeFolder()
    {
        // 5 songs spell the artist "BTS", the one selected song spells it "bts": on its own it would look right
        List<SongArtistTags> library = new List<SongArtistTags>(Repeat(5, "BTS", null));
        SongArtistTags lower = Song("bts");
        library.Add(lower);
        var parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        Put(parsed, "BTS", Ok("BTS", "BTS"));
        Put(parsed, "bts", Ok("bts", "BTS"));

        ArtistPlan plan = ArtistStandardizer.BuildPlan(library, parsed, "; ", true, PathsOf(lower));
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);

        Assert.Equal("BTS", Group(plan, "BTS").CanonicalName);
        AssertEqual(6, Group(plan, "BTS").SongCount, "the group counts the whole folder");
        Assert.Single(updates);
        Assert.Equal("BTS", Change(updates[0], "ARTIST").After);
    }

    [Fact]
    public void AnEmptyScopeChangesNothing()
    {
        ArtistPlan plan = ArtistStandardizer.BuildPlan(Songs(Song("aespa"), Song("에스파 (aespa)")), AespaParses(), "; ",
                true, new HashSet<string>());

        Assert.True(ArtistStandardizer.ComputeUpdates(plan).Count == 0);
        Assert.Equal(0, plan.ScopedSongCount);
    }

    [Fact]
    public void PathsOfSongsThatAreNotInThePlanAreIgnored()
    {
        SongArtistTags korean = Song("에스파 (aespa)");
        HashSet<string> scope = PathsOf(korean);
        scope.Add("not-a-song-of-this-folder.flac");

        ArtistPlan plan = ArtistStandardizer.BuildPlan(Songs(Song("aespa"), korean), AespaParses(), "; ", true, scope);

        Assert.Equal(1, plan.ScopedSongCount);
        Assert.Single(ArtistStandardizer.ComputeUpdates(plan));
    }

    [Fact]
    public void TheScopeFollowsLaterEditsOfAnArtistName()
    {
        SongArtistTags plain = Song("aespa");
        SongArtistTags korean = Song("에스파 (aespa)");
        ArtistPlan plan = ArtistStandardizer.BuildPlan(Songs(plain, korean), AespaParses(), "; ", true, PathsOf(plain));
        Assert.True(ArtistStandardizer.ComputeUpdates(plan).Count == 0, "the selected song already has the name");

        plan.Groups[0].CanonicalName = "AESPA";
        List<SongArtistUpdate> updates = ArtistStandardizer.ComputeUpdates(plan);

        AssertEqual(1, updates.Count, "only the selected song, not the Korean-spelled one");
        Assert.Equal(plain.Path, updates[0].AudioPath);
        Assert.Equal("AESPA", updates[0].ArtistJoined);
    }

    [Fact]
    public void ThePlanKeepsItsOwnCopyOfTheScope()
    {
        SongArtistTags korean = Song("에스파 (aespa)");
        HashSet<string> scope = PathsOf(korean);
        ArtistPlan plan = ArtistStandardizer.BuildPlan(Songs(Song("aespa"), korean), AespaParses(), "; ", true, scope);

        scope.Clear();

        Assert.Single(plan.Scope);
        Assert.Single(ArtistStandardizer.ComputeUpdates(plan));
    }

    [Fact]
    public void TheOldBuildPlanMeansEverySong()
    {
        ArtistPlan plan = ArtistStandardizer.BuildPlan(Songs(Song("aespa"), Song("에스파 (aespa)")), AespaParses(), "; ", true);

        Assert.Null(plan.Scope);
    }
}
