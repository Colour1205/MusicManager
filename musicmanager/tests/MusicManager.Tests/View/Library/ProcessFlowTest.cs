using System.Collections.Concurrent;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.AddSongs;
using MusicManager.InterfaceAdapter.ConvertLRC;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.AddSongs;
using MusicManager.UseCase.ConvertLRC;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using MusicManager.View.Library;
using Xunit;
using AppContext = MusicManager.View.Library.AppContext;

namespace MusicManager.Tests.View.Library;

/// <summary>
/// What Process does with the songs it is given, over fakes of the window, the dialogs, the review page and the use cases.
/// The Java ProcessFlow had no test of its own (it was only reachable through Swing); this one covers the C# port, whose
/// threading (tasks and a dispatcher instead of SwingWorker and invokeLater) is new.
/// </summary>
public class ProcessFlowTest
{
    private sealed class FakeHost : ProcessFlow.IHost
    {
        public readonly object FrameHandle = new();
        public readonly List<string> Calls = new();
        public object ShownPage;
        public HashSet<string> Processed;

        public object Frame()
        {
            return FrameHandle;
        }

        public void ShowReview(object reviewPage)
        {
            ShownPage = reviewPage;
            Add("showReview");
        }

        public void ShowLibrary()
        {
            Add("showLibrary");
        }

        public void OnSongsProcessed(ISet<string> audioPaths)
        {
            Processed = new HashSet<string>(audioPaths);
            Add("processed");
        }

        public void OnConvertProgress(string audioPath, bool success)
        {
            Add("convert:" + audioPath + ":" + success);
        }

        public void SetBusy(bool busy)
        {
            Add("busy:" + busy);
        }

        private void Add(string call)
        {
            lock (Calls)
            {
                Calls.Add(call);
            }
        }

        public List<string> Snapshot()
        {
            lock (Calls)
            {
                return new List<string>(Calls);
            }
        }
    }

    private sealed class FakePrompts : IProcessPrompts
    {
        public readonly List<string> Log = new();
        public readonly List<object> Parents = new();

        public bool Confirm(object parent, string title, string message)
        {
            Add("confirm", parent, title, message);
            return true;
        }

        public void Info(object parent, string title, string message)
        {
            Add("info", parent, title, message);
        }

        public void Warn(object parent, string title, string message)
        {
            Add("warn", parent, title, message);
        }

        public void Error(object parent, string title, string message)
        {
            Add("error", parent, title, message);
        }

        private void Add(string kind, object parent, string title, string message)
        {
            lock (Log)
            {
                Log.Add(kind + "|" + title + "|" + message);
                Parents.Add(parent);
            }
        }

        public List<string> Snapshot()
        {
            lock (Log)
            {
                return new List<string>(Log);
            }
        }
    }

    private sealed class FakePage : ProcessFlow.IReviewPage
    {
        public ProcessFlow.IHost Host;
        public StandardizeController Controller;
        public StandardizeViewModel ViewModel;
        public string Folder;
        public ISet<string> Scope;
        public StandardizeOptions Options;
        public Action OnClosed;
        public ProcessFlow.StagedSongs Staged;
        public bool Closed;
        public bool PreviewStarted;
        public readonly List<string> Events = new();

        public bool IsClosed => Closed;

        public void OnViewModelEvent(string property)
        {
            lock (Events)
            {
                Events.Add(property);
            }
        }

        public void StartPreview()
        {
            PreviewStarted = true;
        }
    }

    private sealed class FakeUseCases : IStandardizeLyricsInputBoundary, IStandardizeArtistsInputBoundary
    {
        public volatile bool ApiKeyAvailable;
        public volatile ManualResetEventSlim Gate;

        public void Execute(StandardizeLyricsInputData inputData)
        {
        }

        public void Analyze(StandardizeArtistsAnalyzeInputData inputData)
        {
        }

        public void Review(StandardizeArtistsApplyInputData inputData)
        {
        }

        public void Apply(StandardizeArtistsApplyInputData inputData)
        {
        }

        public void RenameFiles(StandardizeArtistsApplyInputData inputData)
        {
        }

        public bool IsApiKeyAvailable
        {
            get
            {
                Gate?.Wait();
                return ApiKeyAvailable;
            }
        }
    }

    private sealed class FakeConvert : IConvertLRCInputBoundary
    {
        public readonly List<ConvertLRCInputData> Calls = new();
        public Action<ConvertLRCInputData> Behavior = data => { };

        public void Execute(ConvertLRCInputData inputData)
        {
            lock (Calls)
            {
                Calls.Add(inputData);
            }
            Behavior(inputData);
        }
    }

    /// <summary>the add-songs use case, as far as the flow reaches it: confirming and dropping the staged songs</summary>
    private sealed class FakeAddSongs : IAddSongsInputBoundary
    {
        private readonly AddSongsViewModel _viewModel;
        public int Commits;
        public int Discards;
        public string Problem; // what a commit answers
        public int Songs = 3;

        public FakeAddSongs(AddSongsViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void Add(AddSongsInputData inputData)
        {
        }

        public void Commit()
        {
            Commits++;
            _viewModel.State.CommitProblem = Problem; // the presenter's job: the state is set when the call returns
            _viewModel.State.CommittedSongs = Problem == null ? Songs : 0;
        }

        public void Discard()
        {
            Discards++;
        }
    }

    private const string FOLDER = "C:\\music";

    private readonly FakeHost host = new();
    private readonly FakePrompts prompts = new();
    private readonly FakeUseCases useCases = new();
    private readonly FakeConvert convert = new();
    private readonly StandardizeViewModel processViewModel = new();
    private readonly ConvertLRCViewModal convertViewModal = new();
    private readonly AddSongsViewModel addSongsViewModel = new();
    private readonly FakeAddSongs addSongs;
    private readonly List<FakePage> pages = new();
    private readonly AppContext context;
    private readonly AppSettings settings = new AppSettings(FOLDER, null);

    public ProcessFlowTest()
    {
        addSongs = new FakeAddSongs(addSongsViewModel);
        context = new AppContext(null, null, null, null, null, new StandardizeController(useCases, useCases),
                processViewModel, new ConvertLRCController(convert), convertViewModal, null,
                new AddSongsController(addSongs), addSongsViewModel);
        settings.StandardizeOptions.ApiKey = "typed-key"; // so that no lookup in the environment is needed
    }

    private ProcessFlow Flow(bool legacyConvert)
    {
        return new ProcessFlow(context, host, legacyConvert, prompts, NewPage, action => action());
    }

    private ProcessFlow.IReviewPage NewPage(ProcessFlow.IHost h, StandardizeController controller,
            StandardizeViewModel viewModel, string folder, ISet<string> scope, StandardizeOptions options,
            IProcessPrompts p, Action onClosed, ProcessFlow.StagedSongs staged)
    {
        var page = new FakePage
        {
            Host = h, Controller = controller, ViewModel = viewModel, Folder = folder, Scope = scope, Options = options,
            OnClosed = onClosed, Staged = staged
        };
        lock (pages)
        {
            pages.Add(page);
        }
        return page;
    }

    private static List<Music> Songs(params string[] paths)
    {
        var songs = new List<Music>();
        foreach (string path in paths)
        {
            songs.Add(new Music("t", new List<string>(), "a", path, null));
        }
        return songs;
    }

    private static void WaitFor(Func<bool> condition)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            Thread.Sleep(10);
        }
        Assert.True(condition(), "timed out");
    }

    // ---- Standardize ----

    [Fact]
    public void NoSongsSaysSo()
    {
        Flow(false).Start(FOLDER, new List<Music>(), settings);

        Assert.Equal(new List<string> { "info|Process|There are no songs to process." }, prompts.Snapshot());
        Assert.Same(host.FrameHandle, prompts.Parents[0]);
        Assert.Empty(pages);
        Assert.Empty(host.Snapshot());
    }

    [Fact]
    public void NothingToDoWhenLyricsArtistsAndAlbumsAreOff()
    {
        settings.StandardizeOptions.IsLyrics = false;
        settings.StandardizeOptions.IsArtists = false;
        settings.StandardizeOptions.IsAlbums = false;

        Flow(false).Start(FOLDER, Songs("C:\\music\\a.flac"), settings);

        Assert.Equal(new List<string> { "info|Nothing to do|Nothing to do: turn on lyrics, artists or albums in Options." },
                prompts.Snapshot());
        Assert.Empty(pages);
    }

    [Fact]
    public void AReviewIsOpenedForTheChosenSongsWithTheOptionsAsTheyWere()
    {
        var songs = Songs("C:\\music\\a.flac", "C:\\music\\b.flac", "C:\\music\\a.flac");

        Flow(false).Start(FOLDER, songs, settings);
        settings.StandardizeOptions.IsRomaji = false; // changed after the start: the review keeps what it was given
        songs.Clear();

        FakePage page = Assert.Single(pages);
        Assert.Same(page, host.ShownPage);
        Assert.True(page.PreviewStarted);
        Assert.Same(host, page.Host);
        Assert.Equal(FOLDER, page.Folder);
        Assert.Same(processViewModel, page.ViewModel);
        Assert.Equal(new[] { "C:\\music\\a.flac", "C:\\music\\b.flac" }, page.Scope.ToArray());
        Assert.True(page.Options.IsRomaji);
        Assert.Equal("typed-key", page.Options.ApiKey);
        Assert.Empty(prompts.Snapshot());
        Assert.Throws<NotSupportedException>(() => page.Scope.Add("C:\\music\\c.flac"));
    }

    [Fact]
    public void AnOpenReviewBlocksANewStartUntilItIsClosed()
    {
        ProcessFlow flow = Flow(false);
        var songs = Songs("C:\\music\\a.flac");

        flow.Start(FOLDER, songs, settings);
        flow.Start(FOLDER, songs, settings);
        Assert.Single(pages);

        pages[0].OnClosed(); // the review was left
        flow.Start(FOLDER, songs, settings);
        Assert.Equal(2, pages.Count);

        pages[1].Closed = true;
        flow.Start(FOLDER, songs, settings);
        Assert.Equal(3, pages.Count);
    }

    [Fact]
    public void TheEventsOfTheProcessViewModelReachTheOpenReview()
    {
        ProcessFlow flow = Flow(false);
        processViewModel.FirePropertyChange(StandardizeViewModel.Progress); // no review yet: nobody to tell

        flow.Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        processViewModel.FirePropertyChange(StandardizeViewModel.Lyrics);
        processViewModel.FirePropertyChange(StandardizeViewModel.Error);

        Assert.Equal(new List<string> { "lyrics", "error" }, pages[0].Events);

        pages[0].OnClosed();
        processViewModel.FirePropertyChange(StandardizeViewModel.Progress);
        Assert.Equal(2, pages[0].Events.Count);
    }

    [Fact]
    public void WithoutAnyKeyTheRunDoesNotStart()
    {
        settings.StandardizeOptions.ApiKey = ""; // Claude is needed and no key was typed
        settings.StandardizeOptions.IsAlbums = false;
        useCases.ApiKeyAvailable = false;
        ProcessFlow flow = Flow(false);

        flow.Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        WaitFor(() => prompts.Snapshot().Count == 1);

        string logged = prompts.Snapshot()[0];
        Assert.StartsWith("warn|Anthropic API key|Romaji and grouping the artist names both use Claude", logged, StringComparison.Ordinal);
        Assert.Empty(pages);

        // the lookup is over: another start is possible, and now finds the key
        useCases.ApiKeyAvailable = true;
        flow.Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        WaitFor(() => { lock (pages) { return pages.Count == 1; } });
        Assert.Single(prompts.Snapshot());
    }

    [Fact]
    public void AKeyInTheEnvironmentIsEnough()
    {
        settings.StandardizeOptions.ApiKey = "";
        useCases.ApiKeyAvailable = true;

        Flow(false).Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        WaitFor(() => { lock (pages) { return pages.Count == 1; } });

        Assert.True(pages[0].PreviewStarted);
        Assert.Empty(prompts.Snapshot());
    }

    [Fact]
    public void AStartWhileTheKeyLookupIsRunningIsIgnored()
    {
        settings.StandardizeOptions.ApiKey = "";
        useCases.ApiKeyAvailable = true;
        useCases.Gate = new ManualResetEventSlim(false); // the lookup waits until the test lets it answer
        var queued = new ConcurrentQueue<Action>();
        var flow = new ProcessFlow(context, host, false, prompts, NewPage, queued.Enqueue);

        flow.Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        Assert.True(queued.TryDequeue(out Action firstStart));
        firstStart(); // on the "UI thread": it begins the lookup
        flow.Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        Assert.True(queued.TryDequeue(out Action secondStart));
        secondStart(); // while the lookup is still waiting: ignored

        useCases.Gate.Set();
        WaitFor(() => queued.Count == 1); // the answer of the lookup, to be handled on the "UI thread"
        Assert.True(queued.TryDequeue(out Action answer));
        answer();
        Assert.Single(pages);
        Assert.Empty(prompts.Snapshot());
    }

    [Fact]
    public void ConvertModeIsIgnoredWhenTheFeatureIsOff()
    {
        settings.StandardizeOptions.SetMode(StandardizeOptions.Mode.Convert);

        Flow(false).Start(FOLDER, Songs("C:\\music\\a.flac"), settings);

        Assert.Single(pages);
        Assert.Empty(convert.Calls);
    }

    // ---- songs that were just added (staged: not in the library until the review is applied) ----

    private static readonly string[] AddedPaths = { "C:\\music\\New songs\\a.flac", "C:\\music\\Old\\b.flac" };
    private static readonly string[] NewPaths = { "C:\\music\\New songs\\a.flac" };

    [Fact]
    public void ARunForAddedSongsOpensAReviewThatKnowsWhichSongsAreNew()
    {
        Flow(false).StartAdded(FOLDER, AddedPaths, NewPaths, settings);

        FakePage page = Assert.Single(pages);
        Assert.True(page.PreviewStarted);
        Assert.Equal(AddedPaths, page.Scope.ToArray());
        Assert.NotNull(page.Staged);
        Assert.Equal(NewPaths, page.Staged.NewPaths.ToArray());
        Assert.Equal(0, addSongs.Discards); // the review decides: nothing is dropped, nothing is added yet
        Assert.Equal(0, addSongs.Commits);
    }

    [Fact]
    public void TheReviewConfirmsAnAddThroughTheUseCaseAndLearnsHowManySongsWentIn()
    {
        Flow(false).StartAdded(FOLDER, AddedPaths, NewPaths, settings);
        ProcessFlow.StagedSongs staged = pages[0].Staged;
        addSongs.Songs = 4;

        string problem = staged.Commit();

        Assert.Null(problem);
        Assert.Equal(1, addSongs.Commits);
        Assert.Equal(4, staged.CommittedSongs);
    }

    [Fact]
    public void AConfirmationThatFailedReturnsWhyAndCountsNoSongs()
    {
        Flow(false).StartAdded(FOLDER, AddedPaths, NewPaths, settings);
        addSongs.Problem = "a file is in the way";

        string problem = pages[0].Staged.Commit();

        Assert.Equal("a file is in the way", problem);
        Assert.Equal(0, pages[0].Staged.CommittedSongs);
    }

    [Fact]
    public void TheReviewDropsTheStagedSongsThroughTheUseCase()
    {
        Flow(false).StartAdded(FOLDER, AddedPaths, NewPaths, settings);

        pages[0].Staged.Discard();

        Assert.Equal(1, addSongs.Discards);
        Assert.Equal(0, addSongs.Commits);
    }

    [Fact]
    public void LibrarySongsThatOnlyGetLyricsAreToldApartFromTheNewSongs()
    {
        string[] lyricsOnly = { "C:\\music\\Old\\b.flac" };

        Flow(false).StartAdded(FOLDER, AddedPaths, NewPaths, lyricsOnly, settings);

        ProcessFlow.StagedSongs staged = Assert.Single(pages).Staged;
        Assert.Equal(NewPaths, staged.NewPaths.ToArray());
        Assert.Equal(lyricsOnly, staged.LyricsOnlyPaths.ToArray());
        Assert.Equal(AddedPaths, pages[0].Scope.ToArray()); // all of them are in the run: the lyrics of the second one are standardized too
    }

    [Fact]
    public void LyricsAloneAreEnoughToHaveSomethingToConfirm()
    {
        Flow(false).StartAdded(FOLDER, new[] { "C:\\music\\Old\\b.flac" }, new string[0], new[] { "C:\\music\\Old\\b.flac" }, settings);

        ProcessFlow.StagedSongs staged = Assert.Single(pages).Staged;
        Assert.NotNull(staged);
        Assert.Empty(staged.NewPaths);
        Assert.Single(staged.LyricsOnlyPaths);
    }

    [Fact]
    public void AddedSongsThatNeedNoStagingGetAnOrdinaryReview()
    {
        // songs picked from inside the library, or that it has already: nothing is staged, so there is nothing to confirm
        Flow(false).StartAdded(FOLDER, AddedPaths, new string[0], settings);

        Assert.Null(Assert.Single(pages).Staged);
    }

    [Fact]
    public void WhenNothingIsToBeDoneTheStagedSongsAreDroppedAndTheUserToldNothingWasAdded()
    {
        settings.StandardizeOptions.IsLyrics = false;
        settings.StandardizeOptions.IsArtists = false;
        settings.StandardizeOptions.IsAlbums = false;

        Flow(false).StartAdded(FOLDER, AddedPaths, NewPaths, settings);

        Assert.Empty(pages);
        Assert.Equal(1, addSongs.Discards);
        string logged = Assert.Single(prompts.Snapshot());
        Assert.StartsWith("info|Nothing to do|Nothing was added to the library.", logged, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnyKeyTheStagedSongsAreDroppedToo()
    {
        settings.StandardizeOptions.ApiKey = "";
        settings.StandardizeOptions.IsAlbums = false;
        useCases.ApiKeyAvailable = false;

        Flow(false).StartAdded(FOLDER, AddedPaths, NewPaths, settings);
        WaitFor(() => prompts.Snapshot().Count == 1);

        Assert.Empty(pages);
        Assert.Equal(1, addSongs.Discards);
        Assert.Contains("Nothing was added to the library", prompts.Snapshot()[0]);
    }

    [Fact]
    public void ARunThatCannotStartBecauseAReviewIsOpenDropsTheStagedSongs()
    {
        ProcessFlow flow = Flow(false);
        flow.Start(FOLDER, Songs("C:\\music\\a.flac"), settings);

        flow.StartAdded(FOLDER, AddedPaths, NewPaths, settings);

        Assert.Single(pages);
        Assert.Equal(1, addSongs.Discards);
    }

    [Fact]
    public void ARunWithNoSongsDropsTheStagedSongsToo()
    {
        Flow(false).StartAdded(FOLDER, new string[0], NewPaths, settings);

        Assert.Empty(pages);
        Assert.Equal(1, addSongs.Discards);
        Assert.Equal("info|Process|There are no songs to process.", Assert.Single(prompts.Snapshot()));
    }

    [Fact]
    public void AReviewThatCannotBeMadeDropsTheStagedSongs()
    {
        var flow = new ProcessFlow(context, host, false, prompts,
                (h, controller, viewModel, folder, scope, options, p, onClosed, staged) => throw new InvalidOperationException("no page"),
                action => action());

        Assert.Throws<InvalidOperationException>(() => flow.StartAdded(FOLDER, AddedPaths, NewPaths, settings));

        Assert.Equal(1, addSongs.Discards);
    }

    [Fact]
    public void AnOrdinaryProcessHasNothingStaged()
    {
        Flow(false).Start(FOLDER, Songs("C:\\music\\a.flac"), settings);

        Assert.Null(Assert.Single(pages).Staged);
        Assert.Equal(0, addSongs.Discards);
    }

    // ---- Convert (deprecated) ----

    [Fact]
    public void ConvertWithoutFormatsWarns()
    {
        settings.StandardizeOptions.SetMode(StandardizeOptions.Mode.Convert);
        settings.DefaultTargetFormats = new List<string>();

        Flow(true).Start(FOLDER, Songs("C:\\music\\a.flac"), settings);

        Assert.Equal(new List<string> { "warn|No formats selected|Select at least one format to convert to in Options." },
                prompts.Snapshot());
        Assert.Empty(convert.Calls);
        Assert.Empty(host.Snapshot());
    }

    [Fact]
    public void ConvertModeConvertsStraightAwayAndTellsTheWindowWhatHappened()
    {
        settings.StandardizeOptions.SetMode(StandardizeOptions.Mode.Convert);
        settings.DefaultTargetFormats = new List<string> { "LRC", "TTML" };
        convert.Behavior = data =>
        {
            ConvertLRCState state = convertViewModal.State;
            state.LastProgressMusicPath = "C:\\music\\a.flac";
            state.IsLastProgressSuccess = true;
            convertViewModal.FirePropertyChange("progress");
            state.LastProgressMusicPath = "C:\\music\\b.flac";
            state.IsLastProgressSuccess = false;
            convertViewModal.FirePropertyChange("progress");
            state.Message = "Converted 1/2";
            state.FailCount = 1;
            convertViewModal.FirePropertyChange();
        };

        Flow(true).Start(FOLDER, Songs("C:\\music\\a.flac", "C:\\music\\b.flac"), settings);
        WaitFor(() => host.Snapshot().Contains("processed"));

        ConvertLRCInputData asked = Assert.Single(convert.Calls);
        Assert.Equal(new List<string> { "C:\\music\\a.flac", "C:\\music\\b.flac" }, asked.Musics.Select(m => m.Path).ToList());
        Assert.Equal(new List<string> { "LRC", "TTML" }, asked.TargetFormats);
        Assert.Equal(new List<string>
        {
            "busy:True", "convert:C:\\music\\a.flac:True", "convert:C:\\music\\b.flac:False", "busy:False", "processed"
        }, host.Snapshot());
        Assert.Equal(new HashSet<string> { "C:\\music\\a.flac" }, host.Processed);
        Assert.Equal(new List<string> { "warn|Conversion result|Converted 1/2" }, prompts.Snapshot()); // there was a failure
        Assert.Empty(pages);
    }

    [Fact]
    public void ConvertWithoutFailuresIsReportedAsInformation()
    {
        settings.StandardizeOptions.SetMode(StandardizeOptions.Mode.Convert);
        settings.DefaultTargetFormats = new List<string> { "LRC" };
        convert.Behavior = data =>
        {
            ConvertLRCState state = convertViewModal.State;
            state.Message = "Converted 1/1";
            state.FailCount = 0;
            convertViewModal.FirePropertyChange();
        };

        Flow(true).Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        WaitFor(() => host.Snapshot().Contains("busy:False"));

        Assert.Equal(new List<string> { "info|Conversion result|Converted 1/1" }, prompts.Snapshot());
        Assert.DoesNotContain("processed", host.Snapshot()); // nothing was converted
    }

    [Fact]
    public void ConvertThatStopsUnexpectedlyIsReported()
    {
        settings.StandardizeOptions.SetMode(StandardizeOptions.Mode.Convert);
        settings.DefaultTargetFormats = new List<string> { "LRC" };
        convert.Behavior = data => throw new InvalidOperationException("boom");

        Flow(true).Start(FOLDER, Songs("C:\\music\\a.flac"), settings);
        WaitFor(() => prompts.Snapshot().Count == 1);

        Assert.Equal(new List<string>
        {
            "error|Conversion result|Converting stopped unexpectedly: System.InvalidOperationException: boom"
        }, prompts.Snapshot());
        WaitFor(() => host.Snapshot().Contains("busy:False"));
        Assert.Equal(new List<string> { "busy:True", "busy:False" }, host.Snapshot());
    }
}
