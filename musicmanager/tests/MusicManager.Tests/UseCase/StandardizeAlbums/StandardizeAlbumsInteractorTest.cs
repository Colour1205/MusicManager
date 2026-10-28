using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;

namespace MusicManager.Tests.UseCase.StandardizeAlbums;

/// <summary>Not a port: the album use case with a data access and an output that only record what they are asked.</summary>
public class StandardizeAlbumsInteractorTest
{
    private const string Root = @"C:\lib";

    private sealed class FakeData : IStandardizeAlbumsDataAccess
    {
        public List<SongAlbumTags> Songs = new List<SongAlbumTags>();
        public Func<IDictionary<string, AlbumRaw>, IDictionary<string, ParsedAlbum>> Brain;
        public string EnvironmentKey;
        public string KeyGiven = "(not asked)";
        public string ModelGiven;
        public int AiCalls;
        public HashSet<string> FailingWrites = new HashSet<string>();
        public List<SongAlbumUpdate> Written = new List<SongAlbumUpdate>();
        public List<AlbumFolderMove> Moved = new List<AlbumFolderMove>();
        public Func<AlbumFolderMove, AlbumFolderMoveResult> MoveBrain;

        public List<SongAlbumTags> ReadAlbumTags(string path) => Songs;

        public IDictionary<string, ParsedAlbum> ParseAlbumsWithAi(IDictionary<string, AlbumRaw> raws, string apiKey, string model,
                List<string> notes, Action<string> status)
        {
            AiCalls++;
            KeyGiven = apiKey;
            ModelGiven = model;
            status?.Invoke("asking");
            notes.Add("a note");
            return Brain(raws);
        }

        public string ReadApiKeyFromEnvironment() => EnvironmentKey;

        public bool WriteAlbumTag(SongAlbumUpdate update)
        {
            Written.Add(update);
            return !FailingWrites.Contains(update.AudioPath);
        }

        public AlbumFolderMoveResult MoveToAlbumFolder(AlbumFolderMove move)
        {
            Moved.Add(move);
            return MoveBrain(move);
        }
    }

    private sealed class FakeOutput : IStandardizeAlbumsOutputBoundary
    {
        public StandardizeAlbumsAnalysisOutputData Analysis;
        public StandardizeAlbumsApplyOutputData Applied;
        public StandardizeAlbumsMoveOutputData MovedOutput;
        public List<string> Errors = new List<string>();
        public List<string> Progress = new List<string>();

        public void PresentAlbumsAnalysis(StandardizeAlbumsAnalysisOutputData outputData) => Analysis = outputData;

        public void PresentAlbumsApplied(StandardizeAlbumsApplyOutputData outputData) => Applied = outputData;

        public void PresentAlbumsMoved(StandardizeAlbumsMoveOutputData outputData) => MovedOutput = outputData;

        public void PresentProgress(string message, int done, int total) => Progress.Add(message + "|" + done + "|" + total);

        public void PresentError(string message) => Errors.Add(message);
    }

    private readonly FakeData _data = new FakeData();
    private readonly FakeOutput _output = new FakeOutput();

    private StandardizeAlbumsInteractor Interactor() => new StandardizeAlbumsInteractor(_data, _output);

    private static SongAlbumTags Song(string folder, string file, string album)
    {
        return new SongAlbumTags(Path.Combine(Root, folder, file), album, new List<string> { "IU" }, new List<string>(), file);
    }

    private static StandardizeAlbumsAnalyzeInputData Input(bool useAi, string key = "", IEnumerable<string> scope = null)
    {
        return new StandardizeAlbumsAnalyzeInputData(Root, useAi, key, "the-model", scope);
    }

    // ---- analysing

    [Fact]
    public void WithoutTheAiTheValuesAreCleanedLocallyAndGrouped()
    {
        _data.Songs = new List<SongAlbumTags>
        {
            Song("Palette [Hi-Res 24bit-96khz]", "1.flac", "Palette [Hi-Res 24bit-96khz]"),
            Song("Palette", "2.flac", "Palette"),
        };

        Interactor().Analyze(Input(false));

        Assert.Empty(_output.Errors);
        AlbumPlan plan = _output.Analysis.Plan;
        Assert.False(plan.IsUsedAi);
        Assert.Equal(0, _data.AiCalls);
        AlbumGroup group = Assert.Single(plan.Groups);
        Assert.Equal("Palette", group.CanonicalName);
        Assert.Equal(2, group.SongCount);
        Assert.Single(_output.Analysis.Updates);
        AlbumFolderMove move = Assert.Single(_output.Analysis.Moves);
        Assert.Equal("Palette", move.TargetName);
    }

    [Fact]
    public void TheAiIsAskedForEveryRawValueWithTheTypedKeyAndTheModel()
    {
        _data.Songs = new List<SongAlbumTags> { Song("A", "1.flac", "Love poem"), Song("B", "2.flac", "LOVE POEM") };
        _data.Brain = raws => raws.Keys.ToDictionary(raw => raw, raw => new ParsedAlbum("Love poem", false));

        Interactor().Analyze(Input(true, "  sk-typed  "));

        Assert.Equal(1, _data.AiCalls);
        Assert.Equal("sk-typed", _data.KeyGiven);
        Assert.Equal("the-model", _data.ModelGiven);
        AlbumPlan plan = _output.Analysis.Plan;
        Assert.True(plan.IsUsedAi);
        Assert.Equal(2, Assert.Single(plan.Groups).SongCount);
        Assert.Contains("a note", plan.Warnings);
        Assert.Contains(_output.Progress, p => p.StartsWith("Asking Claude to group 2 album names", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutATypedKeyTheEnvironmentOnesIsUsed()
    {
        _data.Songs = new List<SongAlbumTags> { Song("A", "1.flac", "X") };
        _data.EnvironmentKey = "sk-env";
        _data.Brain = raws => raws.Keys.ToDictionary(raw => raw, raw => new ParsedAlbum(raw, false));

        Interactor().Analyze(Input(true, ""));

        Assert.Equal("sk-env", _data.KeyGiven);
    }

    [Fact]
    public void WithNoKeyAtAllTheDataAccessStillGetsToTryTheRememberedAnswers()
    {
        _data.Songs = new List<SongAlbumTags> { Song("A", "1.flac", "X") };
        _data.Brain = raws => raws.Keys.ToDictionary(raw => raw, raw => new ParsedAlbum(raw, false));

        Interactor().Analyze(Input(true, ""));

        Assert.Null(_data.KeyGiven);
        Assert.Empty(_output.Errors);
        Assert.NotNull(_output.Analysis);
    }

    [Fact]
    public void AnAiFailureWithAKeyIsReportedWithItsReason()
    {
        _data.Songs = new List<SongAlbumTags> { Song("A", "1.flac", "X") };
        _data.Brain = raws => throw new ArtistAiException("overloaded");

        Interactor().Analyze(Input(true, "sk-typed"));

        Assert.Equal("Claude could not be used: overloaded", Assert.Single(_output.Errors));
        Assert.Null(_output.Analysis);
    }

    [Fact]
    public void AnAiFailureWithoutAKeyTellsWhereToPutOne()
    {
        _data.Songs = new List<SongAlbumTags> { Song("A", "1.flac", "X") };
        _data.Brain = raws => throw new ArtistAiException("No API key");

        Interactor().Analyze(Input(true, ""));

        string error = Assert.Single(_output.Errors);
        Assert.Contains("ANTHROPIC_API_KEY", error);
        Assert.Contains("albums", error);
    }

    [Fact]
    public void NoSongsIsAnError()
    {
        Interactor().Analyze(Input(false));

        Assert.Equal("No audio files with readable tags were found in that folder.", Assert.Single(_output.Errors));
        Assert.Null(_output.Analysis);
    }

    [Fact]
    public void WhenNothingHasAnAlbumNothingIsAskedAndNothingChanges()
    {
        _data.Songs = new List<SongAlbumTags>
        {
            new SongAlbumTags(Path.Combine(Root, "1.flac"), null, new List<string>(), new List<string>(), "1"),
        };

        Interactor().Analyze(Input(true, "sk-typed"));

        Assert.Equal(0, _data.AiCalls);
        Assert.Empty(_output.Analysis.Plan.Groups);
        Assert.Empty(_output.Analysis.Updates);
        Assert.Empty(_output.Analysis.Moves);
    }

    [Fact]
    public void OnlyTheSongsInTheScopeChange()
    {
        SongAlbumTags a = Song("Palette [FLAC]", "1.flac", "Palette [FLAC]");
        SongAlbumTags b = Song("Palette [FLAC]", "2.flac", "Palette [FLAC]");
        _data.Songs = new List<SongAlbumTags> { a, b };

        Interactor().Analyze(Input(false, "", new[] { a.Path }));

        Assert.Equal(a.Path, Assert.Single(_output.Analysis.Updates).AudioPath);
        Assert.Equal(2, _output.Analysis.Plan.Groups[0].SongCount); // the names come from every song
    }

    // ---- reviewing, writing, moving

    [Fact]
    public void ReviewPresentsWhatTheEditedPlanWouldChange()
    {
        _data.Songs = new List<SongAlbumTags> { Song("Palette", "1.flac", "Palette") };
        Interactor().Analyze(Input(false));
        Assert.Empty(_output.Analysis.Updates);

        AlbumPlan plan = _output.Analysis.Plan;
        plan.Groups[0].CanonicalName = "PALETTE";
        Interactor().Review(new StandardizeAlbumsApplyInputData(plan));

        Assert.Equal("PALETTE", Assert.Single(_output.Analysis.Updates).Album);
        Assert.Single(_output.Analysis.Moves); // the folder is named by the new name as well
    }

    [Fact]
    public void ApplyWritesTheChangedTagsAndCountsWhatFailed()
    {
        SongAlbumTags ok = Song("A [FLAC]", "1.flac", "X [FLAC]");
        SongAlbumTags bad = Song("A [FLAC]", "2.flac", "X [FLAC]");
        SongAlbumTags fine = Song("B", "1.flac", "X");
        _data.Songs = new List<SongAlbumTags> { ok, bad, fine };
        _data.FailingWrites.Add(bad.Path);
        Interactor().Analyze(Input(false));

        Interactor().Apply(new StandardizeAlbumsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(new[] { ok.Path, bad.Path }, _data.Written.Select(u => u.AudioPath).ToArray());
        Assert.Equal(1, _output.Applied.UpdatedCount);
        Assert.Equal(1, _output.Applied.UnchangedCount);
        Assert.Equal(new[] { bad.Path }, _output.Applied.FailedPaths.ToArray());
        Assert.Contains("Writing album tags...|2|2", _output.Progress);
    }

    [Fact]
    public void MovingTheFoldersGathersWhatEveryMoveDid()
    {
        SongAlbumTags a = Song("Palette [FLAC]", "1.flac", "Palette");
        SongAlbumTags b = Song("Palette [FLAC]", "2.flac", "Palette");
        SongAlbumTags c = Song("Mixed", "3.flac", "Other");
        SongAlbumTags d = Song("Mixed", "4.flac", "Palette");
        _data.Songs = new List<SongAlbumTags> { a, b, c, d };
        Interactor().Analyze(Input(false));
        _data.MoveBrain = move =>
        {
            var moved = new Dictionary<string, string>();
            var skipped = new List<AlbumFolderMoveResult.Skipped>();
            foreach (string path in move.AudioPaths)
            {
                if (path == d.Path)
                {
                    skipped.Add(new AlbumFolderMoveResult.Skipped(path, "it is there already"));
                }
                else
                {
                    moved[path] = Path.Combine(Root, move.TargetName, Path.GetFileName(path));
                }
            }
            return new AlbumFolderMoveResult(moved, skipped, move.IsWholeFolder);
        };

        Interactor().MoveFolders(new StandardizeAlbumsApplyInputData(_output.Analysis.Plan));

        Assert.Equal(3, _data.Moved.Count); // the whole "Palette [FLAC]" folder, and two songs of "Mixed"
        StandardizeAlbumsMoveOutputData result = _output.MovedOutput;
        Assert.Equal(1, result.FoldersRenamed);
        Assert.Equal(3, result.SongsMoved);
        Assert.Equal(d.Path, Assert.Single(result.Skipped).AudioPath);
        Assert.Equal(Path.Combine(Root, "Palette", "1.flac"), result.MovedPaths[a.Path]);
        Assert.Equal(Path.Combine(Root, "Other", "3.flac"), result.MovedPaths[c.Path]);
        Assert.Contains("Moving songs into album folders...|3|3", _output.Progress);
    }

    [Fact]
    public void MovingNothingStillReports()
    {
        _data.Songs = new List<SongAlbumTags> { Song("Palette", "1.flac", "Palette") };
        Interactor().Analyze(Input(false));

        Interactor().MoveFolders(new StandardizeAlbumsApplyInputData(_output.Analysis.Plan));

        Assert.Empty(_data.Moved);
        Assert.Equal(0, _output.MovedOutput.SongsMoved);
        Assert.Empty(_output.MovedOutput.MovedPaths);
    }
}
