using MusicManager.Entity;
using MusicManager.UseCase.OpenLibrary;
using Xunit;

namespace MusicManager.Tests.UseCase.OpenLibrary;

public class OpenLibraryInteractorTest : IDisposable
{
    private readonly string _folder;

    private FakeData _data;
    private Capture _output;
    private OpenLibraryInteractor _interactor;

    private sealed class FakeData : IOpenLibraryDataAccess
    {
        public List<Music> Songs = new List<Music>();
        public Exception Failure;
        public int Calls;
        public string PathAsked;
        public HashSet<string> OnlyAsked;
        public int[][] ProgressToReport = Array.Empty<int[]>();

        public List<Music> ListMusics(string path, HashSet<string> onlyAudioPaths, Action<int, int> progress)
        {
            Calls++;
            PathAsked = path;
            OnlyAsked = onlyAudioPaths;
            foreach (int[] step in ProgressToReport)
            {
                progress(step[0], step[1]);
            }
            if (Failure != null)
            {
                throw Failure;
            }
            return Songs;
        }
    }

    private sealed class Capture : IOpenLibraryOutputBoundary
    {
        public OpenLibraryOutputData Presented;
        public int PresentedCount;
        public string Error;
        public readonly List<string> Progress = new List<string>();

        public void Present(OpenLibraryOutputData outputData)
        {
            Presented = outputData;
            PresentedCount++;
        }

        public void PresentProgress(int done, int total)
        {
            Progress.Add(done + "/" + total);
        }

        public void PresentError(string message)
        {
            Error = message;
        }
    }

    private sealed class FailingPresenter : IOpenLibraryOutputBoundary
    {
        private readonly Capture _output;

        public FailingPresenter(Capture output)
        {
            _output = output;
        }

        public void Present(OpenLibraryOutputData outputData)
        {
            throw new InvalidOperationException("presenter bug");
        }

        public void PresentProgress(int done, int total)
        {
        }

        public void PresentError(string message)
        {
            _output.Error = message;
        }
    }

    public OpenLibraryInteractorTest()
    {
        _folder = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
        Directory.CreateDirectory(_folder);
        _data = new FakeData();
        _output = new Capture();
        _interactor = new OpenLibraryInteractor(_data, _output);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    private static Music Song(string title)
    {
        return new Music.Builder().SetTitle(title).SetPath(title + ".flac").Build();
    }

    // ---- the folder must be valid ----

    [Fact]
    public void AFolderThatDoesNotExistIsAnErrorAndNothingIsRead()
    {
        _interactor.Execute(new OpenLibraryInputData(Path.Combine(_folder, "missing")));

        Assert.Equal("Please choose a valid music folder.", _output.Error);
        Assert.Equal(0, _data.Calls);
        Assert.Null(_output.Presented);
    }

    [Fact]
    public void AFileInsteadOfAFolderIsAnError()
    {
        string file = Path.Combine(_folder, "song.flac");
        File.WriteAllBytes(file, Array.Empty<byte>());

        _interactor.Execute(new OpenLibraryInputData(file));

        Assert.Equal("Please choose a valid music folder.", _output.Error);
        Assert.Equal(0, _data.Calls);
    }

    [Fact]
    public void ABlankOrMissingPathIsAnError()
    {
        _interactor.Execute(new OpenLibraryInputData("   "));
        Assert.Equal("Please choose a valid music folder.", _output.Error);

        _output.Error = null;
        _interactor.Execute(new OpenLibraryInputData(""));
        Assert.Equal("Please choose a valid music folder.", _output.Error);

        _output.Error = null;
        _interactor.Execute(new OpenLibraryInputData((string)null));
        Assert.Equal("Please choose a valid music folder.", _output.Error);

        Assert.Equal(0, _data.Calls);
    }

    // ---- reading ----

    [Fact]
    public void TheSongsAreHandedOverWithTheFolder()
    {
        _data.Songs = new List<Music> { Song("a"), Song("b") };

        _interactor.Execute(new OpenLibraryInputData(_folder));

        Assert.Null(_output.Error);
        Assert.Equal(1, _output.PresentedCount);
        Assert.Equal(_folder, _output.Presented.Path);
        Assert.Same(_data.Songs, _output.Presented.Songs);
        Assert.Null(_output.Presented.OnlyAudioPaths);
        Assert.Equal(_folder, _data.PathAsked);
        Assert.Null(_data.OnlyAsked);
    }

    [Fact]
    public void ProgressFromTheDataAccessIsPassedOnAsItComes()
    {
        _data.ProgressToReport = new int[][] { new[] { 1, 3 }, new[] { 2, 3 }, new[] { 3, 3 } };

        _interactor.Execute(new OpenLibraryInputData(_folder));

        Assert.Equal(new List<string> { "1/3", "2/3", "3/3" }, _output.Progress);
        Assert.NotNull(_output.Presented);
    }

    [Fact]
    public void ARefreshAsksForJustThoseSongsAndSaysSo()
    {
        HashSet<string> only = new HashSet<string> { Path.Combine(_folder, "a.flac") };
        _data.Songs = new List<Music> { Song("a") };

        _interactor.Execute(new OpenLibraryInputData(_folder, only));

        Assert.Equal(only, _data.OnlyAsked);
        Assert.Equal(only, _output.Presented.OnlyAudioPaths);
        Assert.Single(_output.Presented.Songs);
    }

    [Fact]
    public void AnEmptyFolderIsStillAResult()
    {
        _interactor.Execute(new OpenLibraryInputData(_folder));

        Assert.Null(_output.Error);
        Assert.Empty(_output.Presented.Songs);
    }

    // ---- failures ----

    [Fact]
    public void AnExceptionWhileReadingBecomesAnErrorNotACrash()
    {
        _data.Failure = new InvalidOperationException("the disk went away");

        _interactor.Execute(new OpenLibraryInputData(_folder));

        Assert.Equal("Failed to read the music folder: the disk went away", _output.Error);
        Assert.Null(_output.Presented);
        Assert.Equal(0, _output.PresentedCount);
    }

    [Fact]
    public void AnExceptionWithoutAMessageNamesItsType()
    {
        // Java: new UnsupportedOperationException() has no message at all; .NET never leaves Message null (the default
        // is a generated sentence), so "no message" is an empty one and the type name is the .NET one
        _data.Failure = new NotSupportedException("");

        _interactor.Execute(new OpenLibraryInputData(_folder));

        Assert.Equal("Failed to read the music folder: NotSupportedException", _output.Error);
    }

    [Fact]
    public void ProgressBeforeAFailureIsStillPassedOn()
    {
        _data.ProgressToReport = new int[][] { new[] { 1, 2 } };
        _data.Failure = new InvalidOperationException("boom");

        _interactor.Execute(new OpenLibraryInputData(_folder));

        Assert.Equal(new List<string> { "1/2" }, _output.Progress);
        Assert.Contains("boom", _output.Error);
    }

    [Fact]
    public void APresenterThatFailsIsNotReportedAsAFolderThatCouldNotBeRead()
    {
        IOpenLibraryOutputBoundary failing = new FailingPresenter(_output);

        Assert.Throws<InvalidOperationException>(
                () => new OpenLibraryInteractor(_data, failing).Execute(new OpenLibraryInputData(_folder)));
        Assert.Null(_output.Error);
    }

    [Fact]
    public void TheInputDataKeepsACopyOfTheRequestedPaths()
    {
        HashSet<string> only = new HashSet<string> { "a" };
        OpenLibraryInputData input = new OpenLibraryInputData("x", only);

        only.Add("b");

        Assert.Equal(new HashSet<string> { "a" }, input.OnlyAudioPaths);
    }
}
