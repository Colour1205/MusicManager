using MusicManager.UseCase.AddSongs;
using Xunit;

namespace MusicManager.Tests.UseCase.AddSongs;

/// <summary>Not a port: the add-songs use case with a data access and an output that only record what they are asked.</summary>
public class AddSongsInteractorTest : IDisposable
{
    private readonly string _library = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public AddSongsInteractorTest()
    {
        Directory.CreateDirectory(_library);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_library, true);
        }
        catch (IOException)
        {
            // the temp folder is cleaned up by the OS sooner or later
        }
    }

    private sealed class FakeData : IAddSongsDataAccess
    {
        public string LibraryGiven;
        public IReadOnlyList<string> SourcesGiven;
        public string FormatGiven;
        public AddSongsOutputData Result = new AddSongsOutputData(new List<string>(), 0, 0, 0, new List<AddSongsOutputData.Problem>());
        public AddCommitOutputData CommitResult = new AddCommitOutputData(0, null);
        public int Commits;
        public int Discards;

        public AddSongsOutputData AddSongs(string libraryPath, IReadOnlyList<string> sources, string preferredFormat, Action<int, int> progress)
        {
            LibraryGiven = libraryPath;
            SourcesGiven = sources;
            FormatGiven = preferredFormat;
            progress(1, 2);
            return Result;
        }

        public AddCommitOutputData CommitAdded()
        {
            Commits++;
            return CommitResult;
        }

        public void DiscardAdded() => Discards++;
    }

    private sealed class FakeOutput : IAddSongsOutputBoundary
    {
        public AddSongsOutputData Added;
        public AddCommitOutputData Committed;
        public List<string> Errors = new List<string>();
        public List<string> Progress = new List<string>();

        public void PresentAdded(AddSongsOutputData outputData) => Added = outputData;

        public void PresentCommitted(AddCommitOutputData outputData) => Committed = outputData;

        public void PresentProgress(string message, int done, int total) => Progress.Add(message + "|" + done + "|" + total);

        public void PresentError(string message) => Errors.Add(message);
    }

    private readonly FakeData _data = new FakeData();
    private readonly FakeOutput _output = new FakeOutput();

    [Fact]
    public void ThePickedSourcesAreAddedAndTheResultPresented()
    {
        _data.Result = new AddSongsOutputData(new List<string> { "a" }, 1, 0, 1, new List<AddSongsOutputData.Problem>());

        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(_library, new[] { "x", "y" }));

        Assert.Equal(_library, _data.LibraryGiven);
        Assert.Equal(new[] { "x", "y" }, _data.SourcesGiven.ToArray());
        Assert.Same(_data.Result, _output.Added);
        Assert.Empty(_output.Errors);
        Assert.Equal(new[] { "Adding songs...|0|0", "Adding songs...|1|2" }, _output.Progress.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutALibraryFolderNothingIsDone(string library)
    {
        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(library, new[] { "x" }));

        Assert.Equal("The music folder is not there. Open a folder first.", Assert.Single(_output.Errors));
        Assert.Null(_data.SourcesGiven);
    }

    [Fact]
    public void AFolderThatIsGoneIsReported()
    {
        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(Path.Combine(_library, "gone"), new[] { "x" }));

        Assert.Single(_output.Errors);
        Assert.Null(_output.Added);
    }

    [Fact]
    public void NothingPickedIsReported()
    {
        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(_library, new string[0]));

        Assert.Equal("Nothing was picked.", Assert.Single(_output.Errors));
        Assert.Null(_data.SourcesGiven);
    }

    [Fact]
    public void TheFormatPreferenceGoesToTheDataAccessAndFlacIsTheDefault()
    {
        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(_library, new[] { "x" }, ".WAV"));
        Assert.Equal("wav", _data.FormatGiven);

        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(_library, new[] { "x" }));
        Assert.Equal("flac", _data.FormatGiven);

        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(_library, new[] { "x" }, "nonsense"));
        Assert.Equal("flac", _data.FormatGiven);
    }

    [Fact]
    public void ConfirmingPutsTheStagedSongsInTheLibraryAndPresentsWhatHappened()
    {
        _data.CommitResult = new AddCommitOutputData(4, null);

        new AddSongsInteractor(_data, _output).Commit();

        Assert.Equal(1, _data.Commits);
        Assert.Same(_data.CommitResult, _output.Committed);
        Assert.True(_output.Committed.IsDone);
        Assert.Equal(4, _output.Committed.Songs);
        Assert.Equal(0, _data.Discards);
    }

    [Fact]
    public void AConfirmationThatFailedSaysWhy()
    {
        _data.CommitResult = new AddCommitOutputData(0, "a file is in the way");

        new AddSongsInteractor(_data, _output).Commit();

        Assert.False(_output.Committed.IsDone);
        Assert.Equal("a file is in the way", _output.Committed.Problem);
    }

    [Fact]
    public void NotConfirmingThrowsTheStagedSongsAwayAndSaysNothing()
    {
        new AddSongsInteractor(_data, _output).Discard();

        Assert.Equal(1, _data.Discards);
        Assert.Equal(0, _data.Commits);
        Assert.Null(_output.Committed);
        Assert.Null(_output.Added);
        Assert.Empty(_output.Errors);
    }

    [Fact]
    public void AddingOnlyStagesAndNeverCommitsOrDiscards()
    {
        new AddSongsInteractor(_data, _output).Add(new AddSongsInputData(_library, new[] { "x" }));

        Assert.Equal(0, _data.Commits);
        Assert.Equal(0, _data.Discards);
    }

    [Fact]
    public void TheInputKeepsACopyOfTheSources()
    {
        var sources = new List<string> { "x" };
        var input = new AddSongsInputData(_library, sources);

        sources.Add("y");

        Assert.Equal(new[] { "x" }, input.Sources.ToArray());
        Assert.Empty(new AddSongsInputData(_library, null).Sources);
    }
}
