using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;
using Xunit.Sdk;

namespace MusicManager.Tests.UseCase.StandardizeArtists;

public class StandardizeArtistsInteractorTest
{
    private const string Path = "library";

    private FakeData _data;
    private Capture _output;
    private StandardizeArtistsInteractor _interactor;

    private sealed class FakeData : IStandardizeArtistsDataAccess
    {
        public List<SongArtistTags> Songs = new List<SongArtistTags>();
        public IDictionary<string, List<ParsedArtist>> Answer = new OrderedDictionary<string, List<ParsedArtist>>();
        public List<string> NotesToAdd = new List<string>();
        public ArtistAiException Failure;
        public string EnvironmentKey;
        public string KeyUsed;
        public string ModelUsed;
        public IDictionary<string, int> RawCountsAsked = new OrderedDictionary<string, int>();
        public int AiCalls;
        public readonly List<SongArtistUpdate> Written = new List<SongArtistUpdate>();
        public readonly List<string> Unwritable = new List<string>();

        public List<SongArtistTags> ReadArtistTags(string path)
        {
            return Songs;
        }

        public IDictionary<string, List<ParsedArtist>> ParseArtistsWithAi(IDictionary<string, int> rawCounts, string apiKey,
                string model, List<string> notes, Action<string> status)
        {
            AiCalls++;
            RawCountsAsked = rawCounts;
            KeyUsed = apiKey;
            ModelUsed = model;
            if (Failure != null)
            {
                throw Failure;
            }
            notes.AddRange(NotesToAdd);
            return Answer;
        }

        public string ReadApiKeyFromEnvironment()
        {
            return EnvironmentKey;
        }

        public bool WriteArtistTags(SongArtistUpdate update)
        {
            if (Unwritable.Contains(update.AudioPath))
            {
                return false;
            }
            Written.Add(update);
            return true;
        }

        public readonly List<(string Path, string Separator)> Renamed = new List<(string Path, string Separator)>();
        public readonly Dictionary<string, SongRenameResult.Kind> RenameOutcome = new Dictionary<string, SongRenameResult.Kind>();

        public SongRenameResult RenameSongFiles(string audioPath, string separator)
        {
            Renamed.Add((audioPath, separator));
            SongRenameResult.Kind kind = RenameOutcome.TryGetValue(audioPath, out SongRenameResult.Kind outcome)
                    ? outcome : SongRenameResult.Kind.Renamed;
            return new SongRenameResult(kind, audioPath, audioPath + ".new", kind == SongRenameResult.Kind.Renamed ? "" : "why");
        }
    }

    private sealed class Capture : IStandardizeArtistsOutputBoundary
    {
        public StandardizeArtistsAnalysisOutputData Analysis;
        public StandardizeArtistsApplyOutputData Applied;
        public string Error;
        public readonly List<string> Progress = new List<string>();

        public void PresentAnalysis(StandardizeArtistsAnalysisOutputData outputData)
        {
            Analysis = outputData;
        }

        public void PresentApplied(StandardizeArtistsApplyOutputData outputData)
        {
            Applied = outputData;
        }

        public StandardizeArtistsRenameOutputData Renamed;

        public void PresentRenamed(StandardizeArtistsRenameOutputData outputData)
        {
            Renamed = outputData;
        }

        public void PresentProgress(string message, int done, int total)
        {
            Progress.Add(message);
        }

        public void PresentError(string message)
        {
            Error = message;
        }
    }

    public StandardizeArtistsInteractorTest()
    {
        _data = new FakeData();
        _output = new Capture();
        _interactor = new StandardizeArtistsInteractor(_data, _output);
    }

    private static SongArtistTags Song(string path, string artist)
    {
        return new SongArtistTags(path, new List<string> { artist }, new List<string>(), new List<string>());
    }

    private void LibraryOf(params string[] artists)
    {
        _data.Songs = new List<SongArtistTags>();
        for (int i = 0; i < artists.Length; i++)
        {
            _data.Songs.Add(Song("song" + i + ".flac", artists[i]));
        }
    }

    private static ParsedArtist Artist(string text, string name)
    {
        return new ParsedArtist(text, name, false);
    }

    private void Answer(string raw, params ParsedArtist[] artists)
    {
        _data.Answer[raw] = new List<ParsedArtist>(artists);
    }

    private void Analyze(bool useAi, string typedKey)
    {
        _interactor.Analyze(new StandardizeArtistsAnalyzeInputData(Path, "; ", useAi, typedKey, "model-x"));
    }

    // ---- analyze ----

    [Fact]
    public void AFolderWithoutReadableTagsIsAnError()
    {
        _data.Songs = new List<SongArtistTags>();

        Analyze(true, "key");

        Assert.NotNull(_output.Error);
        Assert.Null(_output.Analysis);
        Assert.Equal(0, _data.AiCalls);
    }

    [Fact]
    public void WithoutAnyKeyTheAiOptionStopsWithAnExplanationWhenClaudeIsNeeded()
    {
        LibraryOf("aespa");
        _data.Failure = new ArtistAiException("No API key"); // what the data access says when something is not saved yet

        Analyze(true, "  ");

        Assert.True(_output.Error.Contains("ANTHROPIC_API_KEY", StringComparison.Ordinal), _output.Error);
        Assert.Equal(1, _data.AiCalls);
        Assert.Null(_data.KeyUsed);
    }

    [Fact]
    public void WithoutAnyKeyAnalysisStillWorksWhenEveryNameIsAlreadyKnown()
    {
        LibraryOf("aespa", "에스파 (aespa)");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));

        Analyze(true, "");

        Assert.Null(_output.Error);
        Assert.NotNull(_output.Analysis);
        Assert.Null(_data.KeyUsed);
    }

    [Fact]
    public void ATypedKeyWinsOverTheEnvironmentAndIsTrimmed()
    {
        LibraryOf("aespa");
        _data.EnvironmentKey = "from-env";
        Answer("aespa", Artist("aespa", "aespa"));

        Analyze(true, "  typed-key \n");

        Assert.Equal("typed-key", _data.KeyUsed);
        Assert.Equal("model-x", _data.ModelUsed);
    }

    [Fact]
    public void TheEnvironmentKeyIsUsedWhenNothingWasTyped()
    {
        LibraryOf("aespa");
        _data.EnvironmentKey = "from-env";
        Answer("aespa", Artist("aespa", "aespa"));

        Analyze(true, "");

        Assert.Equal("from-env", _data.KeyUsed);
        Assert.NotNull(_output.Analysis);
        Assert.True(_output.Analysis.Plan.IsUsedAi);
    }

    [Fact]
    public void WhenClaudeCannotBeUsedTheUserIsToldAndNothingIsPlanned()
    {
        LibraryOf("aespa");
        _data.Failure = new ArtistAiException("The Anthropic API rate limit was reached");

        Analyze(true, "key");

        Assert.True(_output.Error.Contains("rate limit", StringComparison.Ordinal), _output.Error);
        Assert.Null(_output.Analysis);
    }

    [Fact]
    public void WhatTheDataAccessWantsTheUserToKnowEndsUpInThePlansWarnings()
    {
        LibraryOf("张一铎");
        Answer("张一铎", new ParsedArtist("张一铎", "张一铎", false, true));
        _data.NotesToAdd.Add("The names could not be checked on the web (web search is not enabled)");

        Analyze(true, "key");

        Assert.Contains(_output.Analysis.Plan.Warnings, w => w.Contains("web search is not enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAiTheNamesAreGroupedLocallyAndClaudeIsNeverCalled()
    {
        LibraryOf("TWICE", "TWICE (트와이스)");

        Analyze(false, null);

        Assert.Equal(0, _data.AiCalls);
        Assert.False(_output.Analysis.Plan.IsUsedAi);
        Assert.Single(_output.Analysis.Plan.Groups);
        Assert.Equal("TWICE", _output.Analysis.Plan.Groups[0].CanonicalName);
    }

    [Fact]
    public void TheAnalysisListsTheSongsThatWouldChange()
    {
        LibraryOf("aespa", "에스파 (aespa)");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));

        Analyze(true, "key");

        Assert.Single(_output.Analysis.Updates);
        Assert.Equal("song1.flac", _output.Analysis.Updates[0].AudioPath);
        Assert.Equal("aespa", _output.Analysis.Updates[0].ArtistJoined);
    }

    // ---- review and apply ----

    [Fact]
    public void RenamingAnArtistAndReviewingRecomputesWhatWillChange()
    {
        LibraryOf("aespa", "aespa");
        Answer("aespa", Artist("aespa", "aespa"));
        Analyze(true, "key");
        ArtistPlan plan = _output.Analysis.Plan;
        Assert.True(_output.Analysis.Updates.Count == 0, "already standard");

        plan.Groups[0].CanonicalName = "Aespa";
        _interactor.Review(new StandardizeArtistsApplyInputData(plan));

        Assert.True(ReferenceEquals(plan, _output.Analysis.Plan), "the same plan, so the window keeps the edited names");
        Assert.Equal(2, _output.Analysis.Updates.Count);
        Assert.Equal("Aespa", _output.Analysis.Updates[0].ArtistJoined);
        Assert.True(_data.Written.Count == 0, "reviewing writes nothing");
    }

    [Fact]
    public void ApplyWritesEverySongThatChangesAndCountsTheRest()
    {
        LibraryOf("aespa", "에스파 (aespa)", "에스파 (aespa)");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));
        Analyze(true, "key");

        _interactor.Apply(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(2, _data.Written.Count);
        Assert.Equal(2, _output.Applied.UpdatedCount);
        Assert.Equal(1, _output.Applied.UnchangedCount);
        Assert.True(_output.Applied.FailedPaths.Count == 0);
    }

    [Fact]
    public void AFileThatCannotBeWrittenIsReportedAndTheOthersAreStillDone()
    {
        LibraryOf("에스파 (aespa)", "에스파 (aespa)");
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));
        Analyze(true, "key");
        _data.Unwritable.Add("song0.flac");

        _interactor.Apply(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(1, _output.Applied.UpdatedCount);
        Assert.Equal(new List<string> { "song0.flac" }, _output.Applied.FailedPaths);
    }

    [Fact]
    public void TheWindowAsksWhetherAKeyIsAvailableWithoutTyping()
    {
        Assert.False(_interactor.IsApiKeyAvailable);
        _data.EnvironmentKey = "key";
        Assert.True(_interactor.IsApiKeyAvailable);
    }

    // ---- scope: only some songs may change, the names come from the whole folder ----

    private void AnalyzeScoped(bool useAi, params string[] scopePaths)
    {
        _interactor.Analyze(new StandardizeArtistsAnalyzeInputData(Path, "; ", useAi, "key", "model-x",
                new HashSet<string>(scopePaths)));
    }

    [Fact]
    public void AScopeLimitsTheUpdatesToTheChosenSongs()
    {
        LibraryOf("aespa", "에스파 (aespa)", "에스파 (aespa)");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));

        AnalyzeScoped(true, "song1.flac");

        Assert.Equal(new HashSet<string> { "song1.flac" }, _output.Analysis.Plan.Scope);
        Assert.Single(_output.Analysis.Updates);
        Assert.Equal("song1.flac", _output.Analysis.Updates[0].AudioPath);
    }

    [Fact]
    public void TheNamesAreStillWorkedOutFromTheWholeFolder()
    {
        LibraryOf("aespa", "aespa", "aespa", "Aespa");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("Aespa", Artist("Aespa", "Aespa"));

        AnalyzeScoped(true, "song3.flac");

        Assert.True("aespa" == _output.Analysis.Plan.Groups[0].CanonicalName,
                "three songs outside the scope decide the spelling");
        Assert.Equal("aespa", _output.Analysis.Updates[0].ArtistJoined);
        Assert.Single(_output.Analysis.Updates);
    }

    [Fact]
    public void ClaudeIsStillAskedAboutEveryNameOfTheFolder()
    {
        LibraryOf("aespa", "에스파 (aespa)", "TWICE");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));
        Answer("TWICE", Artist("TWICE", "TWICE"));

        AnalyzeScoped(true, "song0.flac");

        Assert.Equal(new HashSet<string> { "aespa", "에스파 (aespa)", "TWICE" }, new HashSet<string>(_data.RawCountsAsked.Keys));
    }

    [Fact]
    public void WithoutAiAScopeWorksToo()
    {
        LibraryOf("TWICE", "TWICE (트와이스)", "TWICE (트와이스)");

        AnalyzeScoped(false, "song2.flac");

        Assert.Equal(0, _data.AiCalls);
        Assert.Single(_output.Analysis.Updates);
        Assert.Equal("song2.flac", _output.Analysis.Updates[0].AudioPath);
    }

    [Fact]
    public void ReviewingAfterARenameStaysWithinTheScope()
    {
        LibraryOf("aespa", "aespa", "aespa");
        Answer("aespa", Artist("aespa", "aespa"));
        AnalyzeScoped(true, "song0.flac", "song1.flac");
        Assert.Empty(_output.Analysis.Updates);

        _output.Analysis.Plan.Groups[0].CanonicalName = "Aespa";
        _interactor.Review(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.True(2 == _output.Analysis.Updates.Count, "song2 is not selected");
        Assert.True(_data.Written.Count == 0);
    }

    [Fact]
    public void ApplyWritesOnlyTheChosenSongsAndCountsOnlyThemAsUnchanged()
    {
        LibraryOf("aespa", "에스파 (aespa)", "에스파 (aespa)", "aespa", "aespa");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));
        AnalyzeScoped(true, "song0.flac", "song1.flac", "song3.flac");

        _interactor.Apply(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.True(1 == _data.Written.Count, "song2 needs the change too but was not chosen");
        Assert.Equal("song1.flac", _data.Written[0].AudioPath);
        Assert.Equal(1, _output.Applied.UpdatedCount);
        Assert.True(2 == _output.Applied.UnchangedCount, "of the 3 chosen songs, 2 were already right");
    }

    // ---- file names ----

    private void LibraryWithTitles(params (string Artist, string Title, string File)[] songs)
    {
        _data.Songs = songs.Select(song => new SongArtistTags(song.File, new List<string> { song.Artist }, new List<string>(),
                new List<string>(), song.Title)).ToList();
    }

    [Fact]
    public void TheFilesOfASongAreRenamedToTheStandardizedArtistDashTheTitle()
    {
        LibraryWithTitles(("에스파 (aespa)", "Next Level", "library/에스파 (aespa) - Next Level.flac"),
                ("aespa", "Savage", "library/aespa - Savage.flac"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));
        Answer("aespa", Artist("aespa", "aespa"));
        Analyze(true, "key");

        Assert.Single(_output.Analysis.Renames);
        Assert.Equal("aespa - Next Level", _output.Analysis.Renames[0].NewBaseName);

        _interactor.RenameFiles(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(new List<(string, string)> { ("library/에스파 (aespa) - Next Level.flac", "; ") }, _data.Renamed);
        Assert.Equal(1, _output.Renamed.RenamedCount);
        Assert.Empty(_output.Renamed.Skipped);
    }

    [Fact]
    public void FilesAreRenamedAlsoWhenNoTagChangesBecauseTheTagsAreAlreadyStandard()
    {
        LibraryWithTitles(("aespa", "Next Level", "library/old name.flac"));
        Answer("aespa", Artist("aespa", "aespa"));
        Analyze(true, "key");

        Assert.Empty(_output.Analysis.Updates);
        Assert.Single(_output.Analysis.Renames);

        _interactor.RenameFiles(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(1, _output.Renamed.RenamedCount);
    }

    [Fact]
    public void AFileThatIsNamedRightAlreadyIsNotAskedToBeRenamed()
    {
        LibraryWithTitles(("aespa", "Next Level", "library/aespa - Next Level.flac"));
        Answer("aespa", Artist("aespa", "aespa"));
        Analyze(true, "key");

        _interactor.RenameFiles(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.Empty(_output.Analysis.Renames);
        Assert.Empty(_data.Renamed);
        Assert.Equal(0, _output.Renamed.RenamedCount);
    }

    [Fact]
    public void ASongThatCannotBeRenamedIsReportedWithItsReasonAndTheOthersAreStillDone()
    {
        LibraryWithTitles(("aespa", "A", "library/a.flac"), ("aespa", "B", "library/b.flac"), ("aespa", "C", "library/c.flac"));
        Answer("aespa", Artist("aespa", "aespa"));
        Analyze(true, "key");
        _data.RenameOutcome["library/b.flac"] = SongRenameResult.Kind.Exists;
        _data.RenameOutcome["library/c.flac"] = SongRenameResult.Kind.AlreadyNamed;

        _interactor.RenameFiles(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(1, _output.Renamed.RenamedCount);
        Assert.Equal(1, _output.Renamed.AlreadyNamedCount);
        Assert.Single(_output.Renamed.Skipped);
        Assert.Equal("library/b.flac", _output.Renamed.Skipped[0].AudioPath);
        Assert.Equal(3, _data.Renamed.Count);
    }

    [Fact]
    public void OnlyTheChosenSongsGetTheirFilesRenamed()
    {
        LibraryWithTitles(("aespa", "A", "song0.flac"), ("aespa", "B", "song1.flac"), ("aespa", "C", "song2.flac"));
        Answer("aespa", Artist("aespa", "aespa"));
        AnalyzeScoped(true, "song1.flac");

        _interactor.RenameFiles(new StandardizeArtistsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(new List<string> { "song1.flac" }, _data.Renamed.Select(r => r.Path).ToList());
    }

    [Fact]
    public void ASongWithoutATitleHasNothingToBeNamedAfter()
    {
        LibraryOf("aespa"); // no title tag
        Answer("aespa", Artist("aespa", "aespa"));
        Analyze(true, "key");

        Assert.Empty(_output.Analysis.Renames);
    }

    [Fact]
    public void ChosenSongsWithoutReadableTagsLeaveNothingToDo()
    {
        LibraryOf("aespa", "에스파 (aespa)");
        Answer("aespa", Artist("aespa", "aespa"));
        Answer("에스파 (aespa)", Artist("에스파 (aespa)", "aespa"));

        AnalyzeScoped(true, "unreadable.flac");

        Assert.Null(_output.Error);
        Assert.True(_output.Analysis.Updates.Count == 0);
        Assert.Equal(0, _output.Analysis.Plan.ScopedSongCount);
    }
}
