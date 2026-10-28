using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;

namespace MusicManager.Tests.InterfaceAdapter.Standardize;

/// <summary>the sentences the review page shows above its lists</summary>
public class StandardizePresenterTest
{
    private readonly StandardizeViewModel _viewModel;
    private readonly StandardizePresenter _presenter;
    private readonly List<string> _events;

    public StandardizePresenterTest()
    {
        _viewModel = new StandardizeViewModel();
        _presenter = new StandardizePresenter(_viewModel);
        _events = new List<string>();
        _viewModel.AddPropertyChangeListener(e => _events.Add(e.PropertyName));
    }

    // ---- helpers ----

    private static SongLyricsResult Result(LyricsAction action, int deleted)
    {
        List<string> files = new List<string>();
        for (int i = 0; i < deleted; i++)
        {
            files.Add("C:\\music\\Album\\Song.old" + i);
        }
        return new SongLyricsResult("C:\\music\\Album\\Song.flac", action, "", files);
    }

    private static RomajiSummary Summary(int unique, int cached, int requests, int added, int replaced, int missing,
        string stopped)
    {
        return new RomajiSummary(true, unique, cached, requests, added, replaced, missing, stopped);
    }

    private string LyricsSummary(bool dry, RomajiSummary romaji, params SongLyricsResult[] results)
    {
        _presenter.Present(new StandardizeLyricsOutputData(new List<SongLyricsResult>(results), dry, romaji));
        return _viewModel.State.LyricsSummary;
    }

    private static string[] Lines(string text)
    {
        return JavaString.Split(text, "\n");
    }

    /// <summary>the lines of the romaji part: everything from the line that starts with "Romaji:" on</summary>
    private static List<string> RomajiLines(string summary)
    {
        List<string> all = new List<string>(Lines(summary));
        int first = -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].StartsWith("Romaji:", StringComparison.Ordinal))
            {
                first = i;
                break;
            }
        }
        return first < 0 ? new List<string>() : all.GetRange(first, all.Count - first);
    }

    private static readonly SongLyricsResult Built = Result(LyricsAction.ConvertedFromTtml, 1);

    // ---- romaji in a preview ----

    [Fact]
    public void APreviewSaysHowManyLinesAreAddedAndHowManyOldChineseCharacterLinesAreReplaced()
    {
        string text = LyricsSummary(true, Summary(25, 5, 2, 30, 12, 0, null), Built);

        Assert.Equal(new List<string>
        {
            "Romaji: 18 lines will be added, 12 old Chinese-character lines will be replaced with Latin letters",
            "API will be called with ~20 different lines in ~2 requests (5 already saved from before)"
        },
            RomajiLines(text));
    }

    [Fact]
    public void APreviewThatOnlyAddsDoesNotTalkAboutReplacing()
    {
        string text = LyricsSummary(true, Summary(30, 0, 1, 30, 0, 0, null), Built);

        Assert.Equal("Romaji: 30 lines will be added", RomajiLines(text)[0]);
        Assert.False(text.Contains("Chinese"), text);
    }

    [Fact]
    public void APreviewThatOnlyReplacesDoesNotInventAddedLines()
    {
        string text = LyricsSummary(true, Summary(40, 0, 1, 40, 40, 0, null), Built);

        Assert.Equal("Romaji: 40 old Chinese-character lines will be replaced with Latin letters", RomajiLines(text)[0]);
        Assert.False(text.Contains("will be added"), text);
    }

    [Fact]
    public void OneLineIsNotMadePlural()
    {
        Assert.Equal("Romaji: 1 line will be added", RomajiLines(LyricsSummary(true, Summary(1, 0, 1, 1, 0, 0, null), Built))[0]);
        Assert.Equal("Romaji: 1 old Chinese-character line will be replaced with Latin letters",
            RomajiLines(LyricsSummary(true, Summary(1, 0, 1, 1, 1, 0, null), Built))[0]);
        Assert.Contains("~1 different line in ~1 request", LyricsSummary(true, Summary(1, 0, 1, 1, 0, 0, null), Built));
    }

    [Fact]
    public void APreviewWithNothingToDoSaysSo()
    {
        string text = LyricsSummary(true, Summary(0, 0, 0, 0, 0, 0, null), Built);

        Assert.Equal(new List<string> { "Romaji: no Korean or Japanese lines need it" }, RomajiLines(text));
    }

    [Fact]
    public void APreviewWhereEverythingIsSavedNeedsNoRequest()
    {
        string text = LyricsSummary(true, Summary(12, 12, 0, 12, 0, 0, null), Built);

        Assert.Equal("Romaji: 12 lines will be added", RomajiLines(text)[0]);
        Assert.Equal("Claude will not be needed: every line was saved from an earlier run", RomajiLines(text)[1]);
    }

    [Fact]
    public void APreviewWithoutLineCountsStillSaysWhatClaudeIsAsked()
    {
        // a summary from a use case that only knows what it would ask
        string text = LyricsSummary(true, Summary(12, 2, 1, 0, 0, 0, null), Built);

        Assert.Equal(new List<string> { "Romaji: API will be called with ~10 different lines in ~1 request (2 already saved from before)" },
            RomajiLines(text));
    }

    // ---- romaji after a real run ----

    [Fact]
    public void ARealRunSaysWhatWasAddedAndReplaced()
    {
        string text = LyricsSummary(false, Summary(25, 5, 2, 30, 12, 0, null), Built);

        Assert.Equal(new List<string> { "Romaji: 18 lines added, 12 old Chinese-character lines replaced with Latin letters" },
            RomajiLines(text));
    }

    [Fact]
    public void ARealRunReportsMissingLinesAndWhyItStopped()
    {
        string text = LyricsSummary(false, Summary(25, 0, 2, 20, 0, 5, "The rate limit was reached"), Built);

        Assert.Equal(new List<string> { "Romaji: 20 lines added, 5 missing (run it again to finish). The rate limit was reached" },
            RomajiLines(text));
    }

    [Fact]
    public void ARealRunThatAddedNothingSaysZero()
    {
        Assert.Equal(new List<string> { "Romaji: 0 lines added" }, RomajiLines(LyricsSummary(false, Summary(0, 0, 0, 0, 0, 0, null), Built)));
    }

    [Fact]
    public void WithoutRomajiThereIsNoRomajiLine()
    {
        string text = LyricsSummary(true, RomajiSummary.None, Built);

        Assert.True(RomajiLines(text).Count == 0, text);
    }

    [Fact]
    public void TheChangesSentenceNeverCountsALineTwice()
    {
        Assert.Equal("18 lines added, 12 old Chinese-character lines replaced with Latin letters",
            StandardizePresenter.RomajiChanges(30, 12, false));
        Assert.Equal("", StandardizePresenter.RomajiChanges(0, 0, true));
        // more replaced than added cannot happen, but must not go negative
        Assert.Equal("5 old Chinese-character lines will be replaced with Latin letters",
            StandardizePresenter.RomajiChanges(5, 9, true));
    }

    // ---- the rest of the lyrics summary and rows are as they were ----

    [Fact]
    public void TheHeadlineAndTheBreakdownAreUnchanged()
    {
        string text = LyricsSummary(true, RomajiSummary.None, Built, Result(LyricsAction.ConvertedFromTtml, 2),
            Result(LyricsAction.AlreadyStandard, 0), Result(LyricsAction.NoLyrics, 0));

        string[] lines = Lines(text);
        Assert.Equal("2 songs will get a standard .lrc, 3 deprecated files will be deleted", lines[0]);
        Assert.Equal("2 from .ttml  \u00B7  1 already standard  \u00B7  1 without lyrics", lines[1]);
    }

    [Fact]
    public void TheRowsStillDescribeEachSong()
    {
        _presenter.Present(new StandardizeLyricsOutputData(new List<SongLyricsResult>
        {
            Result(LyricsAction.ConvertedFromTtml, 2),
            new SongLyricsResult("C:\\music\\Album\\Bad.flac", LyricsAction.Failed, "empty", new List<string>())
        }, true, RomajiSummary.None));

        List<string[]> rows = _viewModel.State.LyricsRows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Album / Song", rows[0][0]);
        Assert.Equal("Will build a word-synced .lrc from the .ttml", rows[0][1]);
        Assert.Equal("Will delete 2 deprecated files", rows[0][3]);
        Assert.Equal("", rows[0][4]);
        Assert.Equal("error", rows[1][4]);
        Assert.Equal(new List<string> { "lyrics" }, _events);
    }

    [Fact]
    public void TheRowsOfARealRunStayInThePastTense()
    {
        _presenter.Present(new StandardizeLyricsOutputData(new List<SongLyricsResult>
        {
            Result(LyricsAction.ConvertedFromTtml, 2),
            Result(LyricsAction.RomajiAdded, 0)
        }, false, RomajiSummary.None));

        List<string[]> rows = _viewModel.State.LyricsRows;
        Assert.Equal("Built word-synced .lrc from .ttml", rows[0][1]);
        Assert.Equal("Deleted 2 deprecated files", rows[0][3]);
        Assert.Equal("Added romaji to the existing .lrc", rows[1][1]);
    }

    [Fact]
    public void ThePreviewWordingOfEveryActionLooksAhead()
    {
        foreach (LyricsAction action in Enum.GetValues<LyricsAction>())
        {
            string text = action.GetPreviewDescription();
            Assert.False(string.IsNullOrWhiteSpace(text), action.ToString());
            Assert.DoesNotContain("Built ", text);
            Assert.DoesNotContain("Rewrote", text);
            Assert.DoesNotContain("Added", text);
            Assert.DoesNotContain("kept", text);
        }
    }

    // ---- artists ----

    private static ArtistPlan Plan(HashSet<string> scope, params string[] artistsOfSongs)
    {
        List<SongArtistTags> songs = new List<SongArtistTags>();
        OrderedDictionary<string, List<ParsedArtist>> parsed = new OrderedDictionary<string, List<ParsedArtist>>();
        for (int i = 0; i < artistsOfSongs.Length; i++)
        {
            songs.Add(new SongArtistTags("song" + i + ".flac", new List<string> { artistsOfSongs[i] },
                new List<string>(), new List<string>()));
            parsed[artistsOfSongs[i]] = new List<ParsedArtist>
            {
                new ParsedArtist(artistsOfSongs[i], Regex.Replace(artistsOfSongs[i], " \\(.*", ""), false)
            };
        }
        return ArtistStandardizer.BuildPlan(songs, parsed, "; ", true, scope);
    }

    private string ArtistsSummary(ArtistPlan plan)
    {
        _presenter.PresentAnalysis(new StandardizeArtistsAnalysisOutputData(plan, ArtistStandardizer.ComputeUpdates(plan)));
        return _viewModel.State.ArtistsSummary;
    }

    [Fact]
    public void WithoutAScopeTheSentenceIsAboutEverySong()
    {
        ArtistPlan plan = Plan(null, "aespa", "aespa (\uc5d0\uc2a4\ud30c)", "TWICE");

        string text = ArtistsSummary(plan);

        Assert.Equal("2 artists found, 1 of 3 songs will change", Lines(text)[0]);
        Assert.Equal("Grouped by AI. Check the names marked in red.", Lines(text)[1]);
    }

    [Fact]
    public void WithAScopeTheSentenceIsAboutTheSelectedSongsAndTheArtistsOfTheWholeFolder()
    {
        ArtistPlan plan = Plan(new HashSet<string> { "song1.flac", "song2.flac", "song3.flac" }, "aespa",
            "aespa (\uc5d0\uc2a4\ud30c)", "TWICE", "aespa (\uc5d0\uc2a4\ud30c)");

        string text = ArtistsSummary(plan);

        Assert.Equal("2 artists found in the folder, 2 of 3 selected songs will change", Lines(text)[0]);
    }

    [Fact]
    public void OneSelectedSongReadsInTheSingular()
    {
        ArtistPlan plan = Plan(new HashSet<string> { "song1.flac" }, "aespa", "aespa (\uc5d0\uc2a4\ud30c)");

        Assert.Equal("1 artist found in the folder, 1 of 1 selected song will change", Lines(ArtistsSummary(plan))[0]);
    }

    [Fact]
    public void ASelectionWithNothingToChangeSaysZero()
    {
        ArtistPlan plan = Plan(new HashSet<string> { "song0.flac" }, "aespa", "aespa (\uc5d0\uc2a4\ud30c)");

        Assert.Equal("1 artist found in the folder, 0 of 1 selected song will change", Lines(ArtistsSummary(plan))[0]);
    }

    [Fact]
    public void WarningsAreCountedOnTheSecondLine()
    {
        ArtistPlan plan = Plan(null, "aespa");
        plan.Warnings.Add("check this");

        Assert.Equal("Grouped by AI. Check the names marked in red.  \u00B7  1 warning(s)", Lines(ArtistsSummary(plan))[1]);
        Assert.Equal(new List<string> { "artistsAnalysis" }, _events);
    }

    // ---- file names ----

    [Fact]
    public void RenamedFilesAreToldInOneSentenceWithThePlacesThatWereLeftAsTheyAre()
    {
        _presenter.PresentRenamed(new StandardizeArtistsRenameOutputData(3, 1, new List<SongRenameResult>()));

        Assert.Equal("Renamed 3 songs' files to \"Artist - Title\"", _viewModel.State.ArtistsRenamedSummary);
        Assert.Equal(new List<string> { "artistsRenamed" }, _events);
    }

    [Fact]
    public void OneRenamedSongIsSingular()
    {
        _presenter.PresentRenamed(new StandardizeArtistsRenameOutputData(1, 0, new List<SongRenameResult>()));

        Assert.Equal("Renamed 1 song's files to \"Artist - Title\"", _viewModel.State.ArtistsRenamedSummary);
    }

    [Fact]
    public void SongsThatWereNotRenamedAreListedWithTheirReasonsAndTheListIsCut()
    {
        List<SongRenameResult> skipped = new List<SongRenameResult>();
        for (int i = 1; i <= 7; i++)
        {
            skipped.Add(new SongRenameResult(SongRenameResult.Kind.Exists, "C:\\music\\a" + i + ".flac", "C:\\music\\a" + i + ".flac", "taken " + i));
        }

        _presenter.PresentRenamed(new StandardizeArtistsRenameOutputData(2, 0, skipped));

        string[] lines = Lines(_viewModel.State.ArtistsRenamedSummary);
        Assert.Equal("Renamed 2 songs' files to \"Artist - Title\", 7 songs were left as they are:", lines[0]);
        Assert.Equal("a1.flac: taken 1", lines[1]);
        Assert.Equal("a5.flac: taken 5", lines[5]);
        Assert.Equal("... and 2 more", lines[6]);
    }

    [Fact]
    public void NothingRenamedAndNothingSkippedSaysNothing()
    {
        _presenter.PresentRenamed(new StandardizeArtistsRenameOutputData(0, 4, new List<SongRenameResult>()));

        Assert.Equal("", _viewModel.State.ArtistsRenamedSummary);
    }

    [Fact]
    public void TheAnalysisCarriesHowManySongsHaveFilesToRename()
    {
        ArtistPlan plan = ArtistStandardizer.BuildPlan(new List<SongArtistTags>(), new Dictionary<string, List<ParsedArtist>>(), "; ", false);
        List<SongRename> renames = new List<SongRename> { new SongRename("a.flac", "A - B"), new SongRename("b.flac", "A - C") };

        _presenter.PresentAnalysis(new StandardizeArtistsAnalysisOutputData(plan, new List<SongArtistUpdate>(), renames));

        Assert.Equal(2, _viewModel.State.ArtistRenameCount);
    }

    // ---- albums ----

    private const string AlbumRoot = @"C:\lib";

    private static SongAlbumTags AlbumSong(string folder, string file, string album)
    {
        return new SongAlbumTags(Path.Combine(AlbumRoot, folder, file), album, new List<string> { "IU" }, new List<string>(), file);
    }

    private static AlbumPlan AlbumPlanOf(bool usedAi, IEnumerable<string> scope, params SongAlbumTags[] songs)
    {
        var parsed = new Dictionary<string, ParsedAlbum>();
        foreach (SongAlbumTags song in songs)
        {
            parsed[AlbumStandardizer.RawOf(song, AlbumRoot)] = new ParsedAlbum(AlbumStandardizer.CleanLocally(song.Album), false);
        }
        return AlbumStandardizer.BuildPlan(songs, parsed, usedAi, AlbumRoot, scope);
    }

    private void PresentAlbums(AlbumPlan plan)
    {
        _presenter.PresentAlbumsAnalysis(new StandardizeAlbumsAnalysisOutputData(plan, AlbumStandardizer.ComputeUpdates(plan),
                AlbumStandardizer.ComputeFolderMoves(plan)));
    }

    [Fact]
    public void TheAlbumAnalysisIsInTheStateWithItsSummary()
    {
        AlbumPlan plan = AlbumPlanOf(true, null,
                AlbumSong("Palette [FLAC]", "1.flac", "Palette [FLAC]"), AlbumSong("Palette [FLAC]", "2.flac", "Palette [FLAC]"),
                AlbumSong("Lilac", "1.flac", "Lilac"));

        PresentAlbums(plan);

        StandardizeState state = _viewModel.State;
        Assert.Equal(new[] { "albumsAnalysis" }, _events.ToArray());
        Assert.Same(plan, state.AlbumPlan);
        Assert.Equal(2, state.AlbumUpdates.Count);
        Assert.Single(state.AlbumFolderMoves);
        string[] lines = Lines(state.AlbumsSummary);
        Assert.Equal("2 albums found, 2 of 3 songs will change", lines[0]);
        Assert.Equal("Grouped by AI. Check the albums marked in red.", lines[1]);
        Assert.Equal("2 songs move to the folder named after their albums", lines[2]);
    }

    [Fact]
    public void TheAlbumSummaryOfSomeSelectedSongsIsAboutThemOnly()
    {
        SongAlbumTags first = AlbumSong("Palette [FLAC]", "1.flac", "Palette [FLAC]");
        AlbumPlan plan = AlbumPlanOf(false, new[] { first.Path }, first, AlbumSong("Palette [FLAC]", "2.flac", "Other"));

        PresentAlbums(plan);

        string[] lines = Lines(_viewModel.State.AlbumsSummary);
        Assert.Equal("2 albums found in the folder, 1 of 1 selected song will change", lines[0]);
        Assert.Equal("Grouped without AI, so review every name carefully.", lines[1]);
        Assert.Equal("1 song moves to the folder named after its album", lines[2]);
    }

    [Fact]
    public void AnAlbumLibraryThatIsInOrderSaysNothingMoves()
    {
        PresentAlbums(AlbumPlanOf(true, null, AlbumSong("Palette", "1.flac", "Palette")));

        string[] lines = Lines(_viewModel.State.AlbumsSummary);
        Assert.Equal("1 album found, 0 of 1 song will change", lines[0]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void TheWrittenAlbumTagsAreCounted()
    {
        _presenter.PresentAlbumsApplied(new StandardizeAlbumsApplyOutputData(3, 5, new List<string>()));

        Assert.Equal("Updated 3 album tags, 5 were already right", _viewModel.State.AlbumsAppliedSummary);
        Assert.Equal(new[] { "albumsApplied" }, _events.ToArray());

        _presenter.PresentAlbumsApplied(new StandardizeAlbumsApplyOutputData(1, 0, new List<string> { "x", "y" }));

        Assert.Equal("Updated 1 album tag, 0 were already right, 2 could not be written", _viewModel.State.AlbumsAppliedSummary);
    }

    [Fact]
    public void TheMovedSongsAreCountedAndWhatStayedIsListed()
    {
        var skipped = new List<AlbumFolderMoveResult.Skipped>();
        for (int i = 1; i <= 7; i++)
        {
            skipped.Add(new AlbumFolderMoveResult.Skipped(@"C:\lib\Heat\a" + i + ".flac", "taken " + i));
        }
        var moved = new Dictionary<string, string> { { @"C:\lib\old\a.flac", @"C:\lib\new\a.flac" } };

        _presenter.PresentAlbumsMoved(new StandardizeAlbumsMoveOutputData(2, 9, skipped, moved));

        string[] lines = Lines(_viewModel.State.AlbumsMovedSummary);
        Assert.Equal("Moved 9 songs into the folders named after their albums (2 folders were renamed), 7 songs were left in their folders:", lines[0]);
        Assert.Equal("a1.flac: taken 1", lines[1]);
        Assert.Equal("a5.flac: taken 5", lines[5]);
        Assert.Equal("... and 2 more", lines[6]);
        Assert.Same(moved, _viewModel.State.AlbumMovedPaths);
        Assert.Equal(new[] { "albumsMoved" }, _events.ToArray());
    }

    [Fact]
    public void OneMovedSongAndNoRenamedFolderIsSaidSimply()
    {
        _presenter.PresentAlbumsMoved(new StandardizeAlbumsMoveOutputData(0, 1, new List<AlbumFolderMoveResult.Skipped>(),
                new Dictionary<string, string>()));

        Assert.Equal("Moved 1 song into the folders named after their albums", _viewModel.State.AlbumsMovedSummary);
    }

    [Fact]
    public void NothingMovedAndNothingSkippedSaysNothing()
    {
        _presenter.PresentAlbumsMoved(new StandardizeAlbumsMoveOutputData(0, 0, new List<AlbumFolderMoveResult.Skipped>(),
                new Dictionary<string, string>()));

        Assert.Equal("", _viewModel.State.AlbumsMovedSummary);
    }
}
