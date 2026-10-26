using System.Collections.Generic;
using System.Text;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using MusicManager.View.Library;
using Xunit;

namespace MusicManager.Tests.View.Library;

/// <summary>What the review page counts as work, how it orders and words things.</summary>
public class ReviewTextTest
{
    private static SongLyricsResult Result(string name, LyricsAction action)
    {
        return new SongLyricsResult("/lib/" + name + ".flac", action, "", new List<string>());
    }

    private static SongLyricsResult WithDeleted(string name, LyricsAction action, int deleted)
    {
        var files = new List<string>();
        for (int i = 0; i < deleted; i++)
        {
            files.Add("/lib/" + name + i + ".ttml");
        }
        return new SongLyricsResult("/lib/" + name + ".flac", action, "", files);
    }

    private static SongLyricsResult WithRomaji(string name, LyricsAction action, int added, int replaced)
    {
        return new SongLyricsResult("/lib/" + name + ".flac", action, "", new List<string>(), added, replaced, 0, 0);
    }

    private static StandardizeLyricsOutputData Preview(params SongLyricsResult[] results)
    {
        return new StandardizeLyricsOutputData(new List<SongLyricsResult>(results), true);
    }

    // ---- what counts as work

    [Fact]
    public void OnlyActionsThatWriteOrDeleteAreWork()
    {
        Assert.True(ReviewText.IsWork(LyricsAction.ConvertedFromTtml));
        Assert.True(ReviewText.IsWork(LyricsAction.ConvertedFromHmrc));
        Assert.True(ReviewText.IsWork(LyricsAction.NormalizedLrc));
        Assert.True(ReviewText.IsWork(LyricsAction.LineSyncedFromDeprecated));
        Assert.True(ReviewText.IsWork(LyricsAction.RomajiAdded));
        Assert.False(ReviewText.IsWork(LyricsAction.AlreadyStandard));
        Assert.False(ReviewText.IsWork(LyricsAction.KeptLineSynced));
        Assert.False(ReviewText.IsWork(LyricsAction.NoLyrics));
        Assert.False(ReviewText.IsWork(LyricsAction.Failed));
    }

    [Fact]
    public void APreviewWithNothingToDoHasNoWork()
    {
        Assert.False(ReviewText.HasLyricsWork(null));
        Assert.False(ReviewText.HasLyricsWork(Preview()));
        Assert.False(ReviewText.HasLyricsWork(Preview(Result("a", LyricsAction.AlreadyStandard),
                Result("b", LyricsAction.KeptLineSynced), Result("c", LyricsAction.NoLyrics),
                Result("d", LyricsAction.Failed))));
    }

    [Fact]
    public void RomajiForAnExistingLrcIsWork()
    {
        Assert.True(ReviewText.HasLyricsWork(Preview(Result("a", LyricsAction.AlreadyStandard),
                WithRomaji("b", LyricsAction.RomajiAdded, 12, 3))));
    }

    [Fact]
    public void DeletingFilesIsWorkEvenWhenTheLrcStays()
    {
        Assert.True(ReviewText.HasLyricsWork(Preview(WithDeleted("a", LyricsAction.KeptLineSynced, 2))));
    }

    // ---- order of the cards

    [Fact]
    public void FailuresComeFirstThenTheSongsThatChangeThenTheRestEachInTheirOwnOrder()
    {
        StandardizeLyricsOutputData data = Preview(Result("a", LyricsAction.AlreadyStandard),
                Result("b", LyricsAction.ConvertedFromTtml), Result("c", LyricsAction.Failed),
                Result("d", LyricsAction.NoLyrics), Result("e", LyricsAction.RomajiAdded),
                Result("f", LyricsAction.Failed));
        var rows = new List<string[]>();
        foreach (string name in new[] { "a", "b", "c", "d", "e", "f" })
        {
            rows.Add(new[] { name });
        }

        List<string[]> ordered = ReviewText.OrderRows(rows, data);

        var names = new StringBuilder();
        foreach (string[] row in ordered)
        {
            names.Append(row[0]);
        }
        Assert.Equal("cfbead", names.ToString());
        Assert.True(6 == rows.Count, "the presenter's rows are not changed");
    }

    [Fact]
    public void RowsThatDoNotMatchTheResultsAreLeftAlone()
    {
        var rows = new List<string[]>();
        rows.Add(new[] { "a" });
        Assert.Same(rows, ReviewText.OrderRows(rows, Preview(Result("a", LyricsAction.Failed), Result("b", LyricsAction.Failed))));
        Assert.Same(rows, ReviewText.OrderRows(rows, null));
    }

    // ---- names and summaries

    [Fact]
    public void ASongIsNamedByItsAlbumFolderAndItsFileNameWithoutTheExtension()
    {
        Assert.Equal("BORN PINK / BLACKPINK - Shut Down", ReviewText.DisplayName("/music/BORN PINK/BLACKPINK - Shut Down.flac"));
        Assert.Equal("Song", ReviewText.DisplayName("Song.mp3"));
        Assert.Equal("x / no extension", ReviewText.DisplayName("/x/no extension"));
    }

    [Fact]
    public void ASummaryIsSplitAtItsFirstNewline()
    {
        Assert.Equal(new[] { "headline", "line 1\nline 2" }, ReviewText.SplitSummary("headline\nline 1\nline 2"));
        Assert.Equal(new[] { "only", "" }, ReviewText.SplitSummary("only"));
        Assert.Equal(new[] { "", "" }, ReviewText.SplitSummary(null));
    }

    [Fact]
    public void SongCountsArePluralised()
    {
        Assert.Equal("1 song", ReviewText.SongCount(1));
        Assert.Equal("0 songs", ReviewText.SongCount(0));
        Assert.Equal("12 songs", ReviewText.SongCount(12));
    }

    [Fact]
    public void TheResultMessageHasAPartPerStepThatRan()
    {
        Assert.Equal("Artists: Updated 3 songs\n\nLyrics: 3 songs got a standard .lrc\n1 from .ttml",
                ReviewText.ResultMessage("Updated 3 songs", "3 songs got a standard .lrc\n1 from .ttml\n"));
        Assert.Equal("Artists: Updated 3 songs", ReviewText.ResultMessage("Updated 3 songs", ""));
        Assert.Equal("Lyrics: done", ReviewText.ResultMessage(null, "done"));
        Assert.Equal("", ReviewText.ResultMessage("", ""));
    }

    // ---- the key

    [Fact]
    public void NoMessageWhenClaudeIsNotNeededOrTheKeyIsThere()
    {
        StandardizeOptions off = new StandardizeOptions();
        off.IsRomaji = false;
        off.IsUseClaudeForArtists = false;
        Assert.Null(ReviewText.MissingKeyMessage(off, false));

        Assert.Null(ReviewText.MissingKeyMessage(new StandardizeOptions(), true));

        StandardizeOptions typed = new StandardizeOptions();
        typed.ApiKey = "sk-typed";
        Assert.Null(ReviewText.MissingKeyMessage(typed, false));
    }

    [Fact]
    public void RomajiWithoutAKeyIsReportedBeforeAnythingStarts()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsUseClaudeForArtists = false;
        string message = ReviewText.MissingKeyMessage(options, false);
        Assert.True(message.StartsWith("Romaji uses Claude", StringComparison.Ordinal), message);
        Assert.True(message.Contains("ANTHROPIC_API_KEY"), message);
    }

    [Fact]
    public void ArtistGroupingWithoutAKeyIsReported()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsRomaji = false;
        Assert.StartsWith("Grouping the artist names uses Claude", ReviewText.MissingKeyMessage(options, false), StringComparison.Ordinal);
    }

    [Fact]
    public void BothUsesAreNamedAndRomajiOnlyCountsWhileLyricsAreOn()
    {
        Assert.StartsWith("Romaji and grouping", ReviewText.MissingKeyMessage(new StandardizeOptions(), false), StringComparison.Ordinal);

        StandardizeOptions noLyrics = new StandardizeOptions();
        noLyrics.IsLyrics = false;
        Assert.StartsWith("Grouping the artist names", ReviewText.MissingKeyMessage(noLyrics, false), StringComparison.Ordinal);

        StandardizeOptions nothing = new StandardizeOptions();
        nothing.IsLyrics = false;
        nothing.IsArtists = false;
        Assert.Null(ReviewText.MissingKeyMessage(nothing, false));
    }

    [Fact]
    public void TheEnvironmentIsOnlyAskedForAKeyWhenClaudeIsUsedAndNoneWasTyped()
    {
        Assert.True(ReviewText.NeedsKeyLookup(new StandardizeOptions()));

        StandardizeOptions typed = new StandardizeOptions();
        typed.ApiKey = "sk-typed";
        Assert.False(ReviewText.NeedsKeyLookup(typed));

        StandardizeOptions noClaude = new StandardizeOptions();
        noClaude.IsRomaji = false;
        noClaude.IsUseClaudeForArtists = false;
        Assert.False(ReviewText.NeedsKeyLookup(noClaude));

        StandardizeOptions artistsOnly = new StandardizeOptions();
        artistsOnly.IsLyrics = false;
        Assert.True(ReviewText.NeedsKeyLookup(artistsOnly));
        artistsOnly.IsArtists = false;
        Assert.False(ReviewText.NeedsKeyLookup(artistsOnly));
    }

    // ---- the artist cards

    private static ArtistGroup Group(string key, string name, int songs)
    {
        return new ArtistGroup(key, name, false, false, new List<ArtistVariant> { new ArtistVariant(name, songs) }, songs);
    }

    private static SongArtistTags Song(string path, params string[] artists)
    {
        return new SongArtistTags(path, new List<string>(artists), new List<string>(), new List<string>());
    }

    [Fact]
    public void WithAScopeOnlyTheArtistsOfTheScopedSongsAreListed()
    {
        ArtistGroup aespa = Group("aespa", "aespa", 5);
        ArtistGroup irene = Group("irene", "IRENE", 3);
        ArtistGroup iu = Group("iu", "IU", 9);
        var keysByRaw = new OrderedDictionary<string, List<string>>();
        keysByRaw[ArtistStandardizer.NormalizeRaw("aespa")] = new List<string> { "aespa" };
        keysByRaw[ArtistStandardizer.NormalizeRaw("IRENE")] = new List<string> { "irene" };
        keysByRaw[ArtistStandardizer.NormalizeRaw("IU")] = new List<string> { "iu" };
        var songs = new List<SongArtistTags> { Song("/a.flac", "aespa"), Song("/b.flac", "IRENE"), Song("/c.flac", "IU") };
        var groups = new List<ArtistGroup> { iu, aespa, irene };

        ArtistPlan scoped = new ArtistPlan(";", false, groups, keysByRaw, songs, new List<string>(),
                new HashSet<string> { "/a.flac", "/b.flac" });
        Assert.Equal(new List<ArtistGroup> { aespa, irene }, ReviewText.GroupsInScope(scoped));

        ArtistPlan everything = new ArtistPlan(";", false, groups, keysByRaw, songs, new List<string>(), null);
        Assert.Same(groups, ReviewText.GroupsInScope(everything));
    }

    private static ArtistGroup Checked(string key, string name, bool suggested, bool unsure, params string[] spellings)
    {
        var variants = new List<ArtistVariant>();
        foreach (string spelling in spellings)
        {
            variants.Add(new ArtistVariant(spelling, 1));
        }
        return new ArtistGroup(key, name, suggested, unsure, variants, spellings.Length);
    }

    [Fact]
    public void TheArtistsMarkedPleaseCheckComeFirstEachPartInThePlansOrder()
    {
        ArtistGroup sureA = Checked("a", "IU", false, false, "IU");
        ArtistGroup redB = Checked("b", "K.Will", true, true, "케이윌");
        ArtistGroup sureC = Checked("c", "aespa", false, false, "aespa");
        ArtistGroup redD = Checked("d", "周杰伦", true, false, "周杰倫"); // suggested, not a spelling in the library
        ArtistGroup redE = Checked("e", "Torine", false, true, "Torine"); // not sure

        List<ArtistGroup> ordered = ReviewText.CheckFirst(new List<ArtistGroup> { sureA, redB, sureC, redD, redE });

        Assert.Equal(new List<ArtistGroup> { redB, redD, redE, sureA, sureC }, ordered);
        Assert.True(ReviewText.NeedsCheck(redB) && ReviewText.NeedsCheck(redD) && ReviewText.NeedsCheck(redE));
        Assert.False(ReviewText.NeedsCheck(sureA) || ReviewText.NeedsCheck(sureC));
    }

    [Fact]
    public void WhenNothingIsMarkedTheOrderIsUntouchedAndTheListIsACopy()
    {
        var groups = new List<ArtistGroup> { Group("a", "A", 3), Group("b", "B", 2) };

        List<ArtistGroup> ordered = ReviewText.CheckFirst(groups);

        Assert.Equal(groups, ordered);
        Assert.NotSame(groups, ordered);
    }

    [Fact]
    public void TheSearchFindsAnArtistByItsNameOrAnySpellingIgnoringCaseAndPunctuation()
    {
        ArtistGroup kwill = Checked("k", "K.Will", true, true, "케이윌", "K. Will");

        Assert.True(ReviewText.MatchesSearch(kwill, "k.will"));
        Assert.True(ReviewText.MatchesSearch(kwill, "KWILL"), "punctuation does not matter");
        Assert.True(ReviewText.MatchesSearch(kwill, "케이"), "a spelling the library uses");
        Assert.True(ReviewText.MatchesSearch(kwill, "  will  "), "the box is trimmed");
        Assert.True(ReviewText.MatchesSearch(kwill, ""), "an empty search keeps every card");
        Assert.True(ReviewText.MatchesSearch(kwill, null));
        Assert.False(ReviewText.MatchesSearch(kwill, "aespa"));
        Assert.False(ReviewText.MatchesSearch(kwill, "백지영"));
    }

    [Fact]
    public void ASpellingNoSongUsesIsNotSearchedButAPunctuationOnlySearchStillWorks()
    {
        var group = new ArtistGroup("g", "G.E.M. 邓紫棋", false, false,
                new List<ArtistVariant> { new ArtistVariant("G.E.M.", 2), new ArtistVariant("Proposed Only", 0) }, 2);

        Assert.True(ReviewText.MatchesSearch(group, "g.e.m"));
        Assert.True(ReviewText.MatchesSearch(group, "邓紫"));
        Assert.False(ReviewText.MatchesSearch(group, "proposed"), "a name proposed for the group that no song uses");
        Assert.True(ReviewText.MatchesSearch(group, "."), "punctuation alone is looked for as typed");
    }

    [Fact]
    public void TheSongsOfACardAreTheOnesItsCountSaysWhateverTagNamesTheArtist()
    {
        ArtistGroup kda = Group("kda", "K/DA", 3);
        ArtistGroup madison = Group("madison", "Madison Beer", 1);
        var keysByRaw = new OrderedDictionary<string, List<string>>();
        keysByRaw[ArtistStandardizer.NormalizeRaw("K/DA & Madison Beer")] = new List<string> { "kda", "madison" };
        keysByRaw[ArtistStandardizer.NormalizeRaw("K/DA")] = new List<string> { "kda" };
        keysByRaw[ArtistStandardizer.NormalizeRaw("Madison Beer")] = new List<string> { "madison" };
        var songs = new List<SongArtistTags>
        {
            new SongArtistTags("C:\\m\\ALL OUT\\VILLAIN.flac", new List<string> { "K/DA & Madison Beer" }, null, null),
            new SongArtistTags("C:\\m\\ALL OUT\\POP STARS.flac", new List<string> { "K/DA" }, null, null),
            new SongArtistTags("C:\\m\\Other\\Song.flac", new List<string> { "Somebody" }, new List<string> { "K/DA" }, null),
            new SongArtistTags("C:\\m\\Else\\Nope.flac", new List<string> { "Somebody" }, null, null),
        };
        ArtistPlan plan = new ArtistPlan(";", false, new List<ArtistGroup> { kda, madison }, keysByRaw, songs, new List<string>());

        List<ReviewText.ArtistSong> ofKda = ReviewText.SongsOf(plan, kda);
        List<ReviewText.ArtistSong> ofMadison = ReviewText.SongsOf(plan, madison);

        Assert.Equal(new List<string> { "ALL OUT / VILLAIN", "ALL OUT / POP STARS", "Other / Song" }, ofKda.Select(s => s.Name).ToList());
        Assert.Equal("K/DA & Madison Beer", ofKda[0].ArtistTag);
        Assert.Equal("Somebody", ofKda[2].ArtistTag); // the ARTIST tag is what is shown, the album artist is what matched
        Assert.Equal(new List<string> { "ALL OUT / VILLAIN" }, ofMadison.Select(s => s.Name).ToList());
    }

    [Fact]
    public void APlanWithoutKeysHasNoSongsToList()
    {
        ArtistPlan plan = new ArtistPlan(";", false, new List<ArtistGroup>(), null, new List<SongArtistTags>(), new List<string>());
        Assert.Empty(ReviewText.SongsOf(plan, Group("x", "X", 1)));
    }

    [Fact]
    public void AScopeWithoutArtistTagsListsNoArtists()
    {
        ArtistGroup iu = Group("iu", "IU", 9);
        var keysByRaw = new OrderedDictionary<string, List<string>>();
        keysByRaw[ArtistStandardizer.NormalizeRaw("IU")] = new List<string> { "iu" };
        ArtistPlan plan = new ArtistPlan(";", false, new List<ArtistGroup> { iu }, keysByRaw,
                new List<SongArtistTags> { Song("/c.flac", "IU") }, new List<string>(),
                new HashSet<string> { "/not-in-the-folder.flac" });
        Assert.Empty(ReviewText.GroupsInScope(plan));
    }
}
