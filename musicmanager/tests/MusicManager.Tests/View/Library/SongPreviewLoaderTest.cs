using System.Collections.Concurrent;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using MusicManager.View.Library;
using Xunit;

namespace MusicManager.Tests.View.Library;

/// <summary>
/// The background job of the song window, over pipelines whose use cases are fakes that report through the real
/// presenter and view model (so the events are the ones the app produces).
/// </summary>
public class SongPreviewLoaderTest
{
    private const string FOLDER = "C:\\music";
    private const string SONG = "C:\\music\\Album\\Song.flac";
    private const string OTHER = "C:\\music\\Album\\Other.flac";

    /// <summary>The use cases of one pipeline: they report what the test says, and remember how they were asked.</summary>
    private sealed class Fake : IStandardizeLyricsInputBoundary, IStandardizeArtistsInputBoundary
    {
        public StandardizePresenter Presenter;
        public readonly List<string> Order = new();
        public readonly List<StandardizeLyricsInputData> LyricsCalls = new();
        public readonly List<StandardizeArtistsAnalyzeInputData> ArtistCalls = new();
        public string LyricsError;
        public string ArtistsError;
        public Exception LyricsThrows;
        public Exception ArtistsThrows;
        public List<SongLyricsResult> LyricsResults = new()
        {
            new SongLyricsResult(SONG, LyricsAction.AlreadyStandard, "", new List<string>())
        };
        public List<SongArtistUpdate> ArtistUpdates = new();
        public List<string> ProgressMessages = new();
        public Action DuringLyrics = () => { };

        public void Execute(StandardizeLyricsInputData inputData)
        {
            Order.Add("lyrics");
            LyricsCalls.Add(inputData);
            DuringLyrics();
            foreach (string message in ProgressMessages)
            {
                Presenter.PresentProgress(1, 3, message);
            }
            if (LyricsThrows != null)
            {
                throw LyricsThrows;
            }
            if (LyricsError != null)
            {
                Presenter.PresentError(LyricsError);
                return;
            }
            Presenter.Present(new StandardizeLyricsOutputData(LyricsResults, true));
        }

        public void Analyze(StandardizeArtistsAnalyzeInputData inputData)
        {
            Order.Add("artists");
            ArtistCalls.Add(inputData);
            Presenter.PresentProgress("Asking Claude to group 3 artist names...", 0, 0);
            if (ArtistsThrows != null)
            {
                throw ArtistsThrows;
            }
            if (ArtistsError != null)
            {
                Presenter.PresentError(ArtistsError);
                return;
            }
            ArtistPlan plan = new ArtistPlan("; ", false, new List<ArtistGroup>(),
                    new OrderedDictionary<string, List<string>>(), new List<SongArtistTags>(), new List<string>(),
                    new HashSet<string> { SONG });
            Presenter.PresentAnalysis(new StandardizeArtistsAnalysisOutputData(plan, ArtistUpdates));
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

        public bool IsApiKeyAvailable => true;
    }

    /// <summary>What the listener was told, in order: "lyrics", "tags", "progress: ...", "tagsProgress: ...".</summary>
    private class Heard : SongPreviewLoader.IListener
    {
        private readonly object _gate = new();
        public readonly List<string> Events = new();
        public volatile SongLyricsPreview LyricsPreview;
        public volatile SongTagsPreview TagsPreview;

        private void Add(string text)
        {
            lock (_gate)
            {
                Events.Add(text);
            }
        }

        public virtual void Lyrics(SongLyricsPreview preview)
        {
            LyricsPreview = preview;
            Add("lyrics");
        }

        public virtual void Tags(SongTagsPreview preview)
        {
            TagsPreview = preview;
            Add("tags");
        }

        public virtual void Progress(string text)
        {
            Add("progress: " + text);
        }

        public virtual void TagsProgress(string text)
        {
            Add("tagsProgress: " + text);
        }
    }

    private Fake fake;
    private readonly List<StandardizeViewModel> viewModels = new();
    private readonly List<Fake> fakesMade = new();
    private readonly Queue<Action> queued = new();
    private readonly List<Fake> nextFakes = new();
    private Music song;
    private StandardizeOptions options;
    private SongPreviewLoader loader;

    public SongPreviewLoaderTest()
    {
        fake = new Fake();
        song = new Music("Song", new List<string> { "Artist" }, "Album", SONG, null);
        options = new StandardizeOptions();
        options.ApiKey = "typed-key";
        options.Model = "model-x";
        loader = new SongPreviewLoader(Pipeline, action => action(), action => action());
    }

    /// <summary>A new pipeline for each call, all over the one fake (or over the next fake the test lined up).</summary>
    private StandardizePipeline Pipeline()
    {
        Fake use = fakesMade.Count < nextFakes.Count ? nextFakes[fakesMade.Count] : fake;
        fakesMade.Add(use);
        StandardizeViewModel viewModel = new StandardizeViewModel();
        use.Presenter = new StandardizePresenter(viewModel);
        viewModels.Add(viewModel);
        return new StandardizePipeline(new StandardizeController(use, use), viewModel);
    }

    private SongPreviewLoader.IHandle Load(Heard heard)
    {
        return loader.Load(FOLDER, song, options, heard);
    }

    private static SongArtistUpdate Update(string path, string before, string after)
    {
        return new SongArtistUpdate(path, after, null, null, new List<FieldChange> { new FieldChange("ARTIST", before, after) });
    }

    private static List<string> WithoutProgress(Heard heard)
    {
        return heard.Events.Where(e => !e.Contains(':')).ToList();
    }

    // ---- the order and what is asked ----

    [Fact]
    public void TheLyricsPartComesFirstAndThenTheTags()
    {
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal(new List<string> { "lyrics", "artists" }, fake.Order);
        Assert.Equal(new List<string> { "lyrics", "tags" }, WithoutProgress(heard));
        Assert.NotNull(heard.LyricsPreview);
        Assert.NotNull(heard.TagsPreview);
        Assert.Equal(SongPreviewMapper.Tags(new List<SongArtistUpdate>(), options).Message, heard.TagsPreview.Message); // no failure
        Assert.Equal(SongStatus.Tone.Muted, heard.LyricsPreview.Tone);
    }

    [Fact]
    public void TheLyricsPartIsAskedAsADetailedDryRunOfThisSongOnly()
    {
        options.IsRecycleBin = true;

        Load(new Heard());

        StandardizeLyricsInputData asked = fake.LyricsCalls[0];
        Assert.Equal(FOLDER, asked.Path);
        Assert.True(asked.IsDryRun);
        Assert.True(asked.IsPreviewDetail);
        Assert.True(asked.IsMoveToTrash);
        Assert.True(asked.IsRomanize);
        Assert.Equal("typed-key", asked.ApiKey);
        Assert.Equal("model-x", asked.Model);
        Assert.Equal(new HashSet<string> { SONG }, asked.AudioPaths);
    }

    [Fact]
    public void RomajiOffIsPassedOn()
    {
        options.IsRomaji = false;

        Load(new Heard());

        Assert.False(fake.LyricsCalls[0].IsRomanize);
    }

    [Fact]
    public void TheTagsPartIsAskedForThisSongOnly()
    {
        options.Separator = " & ";
        options.IsUseClaudeForArtists = false;

        Load(new Heard());

        StandardizeArtistsAnalyzeInputData asked = fake.ArtistCalls[0];
        Assert.Equal(FOLDER, asked.Path);
        Assert.Equal(" & ", asked.Separator);
        Assert.False(asked.IsUseAi);
        Assert.Equal("typed-key", asked.ApiKey);
        Assert.Equal("model-x", asked.Model);
        Assert.Equal(new HashSet<string> { SONG }, asked.AudioPaths);
    }

    [Fact]
    public void OnlyTheUpdatesOfThisSongReachTheTagsPreview()
    {
        fake.ArtistUpdates = new List<SongArtistUpdate> { Update(OTHER, "A (a)", "A") };
        Heard heard = new Heard();

        Load(heard);

        Assert.Empty(heard.TagsPreview.Changes);
    }

    [Fact]
    public void TheUpdateOfThisSongReachesTheTagsPreview()
    {
        fake.ArtistUpdates = new List<SongArtistUpdate>
        {
            Update(OTHER, "A (a)", "A"),
            Update(SONG, "TWICE (\ud2b8\uc640\uc774\uc2a4)", "TWICE")
        };
        Heard heard = new Heard();

        Load(heard);

        Assert.NotEmpty(heard.TagsPreview.Changes);
        Assert.Equal("TWICE (\ud2b8\uc640\uc774\uc2a4)", heard.TagsPreview.Changes[0].Before);
        Assert.Equal("TWICE", heard.TagsPreview.Changes[0].After);
    }

    // ---- off ----

    [Fact]
    public void LyricsOffMeansNoLyricsJobAndTheTagsStillRun()
    {
        options.IsLyrics = false;
        Heard heard = new Heard();

        Load(heard);

        Assert.Empty(fake.LyricsCalls);
        Assert.Equal("The lyrics are off in Options.", heard.LyricsPreview.Message);
        Assert.Equal(SongStatus.Tone.Muted, heard.LyricsPreview.Tone);
        Assert.Single(fake.ArtistCalls);
        Assert.Equal(new List<string> { "lyrics", "tags" }, WithoutProgress(heard));
    }

    [Fact]
    public void ArtistsOffMeansNoArtistJob()
    {
        options.IsArtists = false;
        Heard heard = new Heard();

        Load(heard);

        Assert.Empty(fake.ArtistCalls);
        Assert.Equal(SongPreviewMapper.TagsOff().Message, heard.TagsPreview.Message);
        Assert.Empty(heard.TagsPreview.Changes);
        Assert.Single(fake.LyricsCalls);
    }

    // ---- failures ----

    [Fact]
    public void AnErrorOfTheLyricsPartBecomesItsPreviewAndTheTagsStillRun()
    {
        fake.LyricsError = "Could not read the folder";
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal("Could not read the folder", heard.LyricsPreview.Message);
        Assert.Equal(SongStatus.Tone.Bad, heard.LyricsPreview.Tone);
        Assert.Equal(new List<string> { "lyrics", "artists" }, fake.Order);
        Assert.Equal(new List<string> { "lyrics", "tags" }, WithoutProgress(heard));
    }

    [Fact]
    public void AnErrorOfTheTagsPartIsNotMistakenForTheLyricsOne()
    {
        fake.ArtistsError = "Claude could not be used: no key";
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal(SongStatus.Tone.Muted, heard.LyricsPreview.Tone); // the lyrics part had no error to show
        Assert.Equal("Claude could not be used: no key", heard.TagsPreview.Message);
    }

    [Fact]
    public void AnExceptionOfTheLyricsPartBecomesItsPreview()
    {
        fake.LyricsThrows = new InvalidOperationException("disk gone");
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal("disk gone", heard.LyricsPreview.Message);
        Assert.Equal(SongStatus.Tone.Bad, heard.LyricsPreview.Tone);
        Assert.NotNull(heard.TagsPreview);
    }

    [Fact]
    public void AnExceptionOfTheTagsPartBecomesItsPreviewAndLeavesTheLyricsAlone()
    {
        fake.ArtistsThrows = new InvalidOperationException("no tags");
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal("no tags", heard.TagsPreview.Message);
        Assert.NotNull(heard.LyricsPreview);
        Assert.True(heard.LyricsPreview.Message.Length == 0 || heard.LyricsPreview.Tone != SongStatus.Tone.Bad);
    }

    [Fact]
    public void ASongThatIsNotInTheFolderIsSaidSo()
    {
        fake.LyricsResults = new List<SongLyricsResult>();
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal(SongStatus.Tone.Bad, heard.LyricsPreview.Tone);
        Assert.NotEqual("", heard.LyricsPreview.Message);
    }

    [Fact]
    public void APipelineThatCannotBeMadeFailsBothParts()
    {
        loader = new SongPreviewLoader(() => throw new InvalidOperationException("no pipeline"), action => action(),
                action => action());
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal("no pipeline", heard.LyricsPreview.Message);
        Assert.Equal("no pipeline", heard.TagsPreview.Message);
        Assert.Equal(new List<string> { "lyrics", "tags" }, heard.Events);
    }

    [Fact]
    public void AWorkerThatRefusesTheJobFailsBothParts()
    {
        // Java: RejectedExecutionException
        loader = new SongPreviewLoader(Pipeline, action => action(), job => throw new InvalidOperationException("no threads"));
        Heard heard = new Heard();

        Load(heard);

        Assert.Equal("no threads", heard.LyricsPreview.Message);
        Assert.Equal("no threads", heard.TagsPreview.Message);
    }

    // ---- progress ----

    [Fact]
    public void TheRomajiRequestsAreReportedAsProgress()
    {
        fake.ProgressMessages = new List<string> { "Lyrics: Album / Song", "Lyrics: Romaji, request 1 of 3 (30 of 74 lines)" };
        Heard heard = new Heard();

        Load(heard);

        List<string> progress = heard.Events.Where(e => e.StartsWith("progress", StringComparison.Ordinal)).ToList();
        Assert.Equal(new List<string> { "progress: Asking Claude for the romaji... 30 of 74 lines" }, progress);
    }

    [Fact]
    public void TheArtistStepReportsWhatItDoes()
    {
        Heard heard = new Heard();

        Load(heard);

        Assert.Contains("tagsProgress: Asking Claude to group 3 artist names...", heard.Events);
        Assert.True(heard.Events.IndexOf("lyrics") < heard.Events.IndexOf("tagsProgress: Asking Claude to group 3 artist names..."));
    }

    [Fact]
    public void TheProgressTextOnlyMentionsClaudeRequests()
    {
        Assert.Null(SongPreviewLoader.LyricsProgressText("Lyrics: Album / Song"));
        Assert.Null(SongPreviewLoader.LyricsProgressText("Lyrics: Writing Song"));
        Assert.Null(SongPreviewLoader.LyricsProgressText(null));
        Assert.Equal("Asking Claude for the romaji...", SongPreviewLoader.LyricsProgressText("Lyrics: Romaji"));
        Assert.Equal("Asking Claude for the romaji... 1 of 2 lines",
                SongPreviewLoader.LyricsProgressText("Lyrics: Romaji, request 1 of 1 (1 of 2 lines)"));
    }

    // ---- cancel ----

    [Fact]
    public void ACancelledJobDeliversNothing()
    {
        loader = new SongPreviewLoader(Pipeline, action => action(), queued.Enqueue);
        Heard heard = new Heard();

        SongPreviewLoader.IHandle handle = Load(heard);
        handle.Cancel();
        queued.Dequeue()();

        Assert.Empty(heard.Events);
        Assert.Empty(fake.Order); // it did not even start
    }

    [Fact]
    public void CancellingWhileTheLyricsRunDropsTheirResultAndSkipsTheTags()
    {
        Heard heard = new Heard();
        var handle = new SongPreviewLoader.IHandle[1];
        fake.DuringLyrics = () => handle[0].Cancel();
        loader = new SongPreviewLoader(Pipeline, action => action(), queued.Enqueue);

        handle[0] = Load(heard);
        queued.Dequeue()();

        Assert.Equal(new List<string> { "lyrics" }, fake.Order);
        Assert.Empty(heard.Events);
    }

    /// <summary>A listener that cancels its job as soon as the lyrics part arrives.</summary>
    private sealed class Cancelling : Heard
    {
        public SongPreviewLoader.IHandle[] Handle;

        public override void Lyrics(SongLyricsPreview preview)
        {
            base.Lyrics(preview);
            Handle[0].Cancel();
        }
    }

    [Fact]
    public void CancellingAfterTheLyricsDropsTheTags()
    {
        var handle = new SongPreviewLoader.IHandle[1];
        Cancelling cancelling = new Cancelling { Handle = handle };

        loader = new SongPreviewLoader(Pipeline, action => action(), queued.Enqueue);
        handle[0] = Load(cancelling);
        queued.Dequeue()();

        Assert.Equal(new List<string> { "lyrics" }, cancelling.Events);
        Assert.Equal(new List<string> { "lyrics" }, fake.Order);
    }

    [Fact]
    public void WhatWaitsToBeDeliveredIsDroppedByACancel()
    {
        var delivery = new List<Action>();
        loader = new SongPreviewLoader(Pipeline, delivery.Add, action => action());
        Heard heard = new Heard();

        SongPreviewLoader.IHandle handle = Load(heard);
        Assert.NotEmpty(delivery);
        handle.Cancel();
        delivery.ForEach(action => action());

        Assert.Empty(heard.Events);
    }

    // ---- independence ----

    [Fact]
    public void EveryLoadGetsAPipelineOfItsOwn()
    {
        Fake first = new Fake();
        first.LyricsResults = new List<SongLyricsResult>
        {
            new SongLyricsResult(SONG, LyricsAction.NoLyrics, "first", new List<string>())
        };
        Fake second = new Fake();
        second.LyricsError = "second failed";
        nextFakes.Add(first);
        nextFakes.Add(second);
        loader = new SongPreviewLoader(Pipeline, action => action(), queued.Enqueue);
        Heard heardFirst = new Heard();
        Heard heardSecond = new Heard();

        Load(heardFirst);
        Load(heardSecond);
        queued.Dequeue()();
        queued.Dequeue()();

        Assert.Equal(2, fakesMade.Count);
        Assert.NotSame(viewModels[0], viewModels[1]);
        Assert.Single(first.LyricsCalls);
        Assert.Single(second.LyricsCalls);
        Assert.Equal(SongStatus.Tone.Bad, heardSecond.LyricsPreview.Tone);
        Assert.Equal("second failed", heardSecond.LyricsPreview.Message);
        Assert.True(heardFirst.LyricsPreview.Tone != SongStatus.Tone.Bad);
        Assert.DoesNotContain("second failed", heardFirst.LyricsPreview.Message);
    }

    [Fact]
    public void TheOptionsAreCopiedWhenTheJobIsStarted()
    {
        loader = new SongPreviewLoader(Pipeline, action => action(), queued.Enqueue);
        Heard heard = new Heard();

        Load(heard);
        options.IsLyrics = false; // changed after the start: the job keeps what it was given
        queued.Dequeue()();

        Assert.Single(fake.LyricsCalls);
    }

    // ---- options that change the preview ----

    [Fact]
    public void OnlyTheOptionsThatChangeThePreviewAreCompared()
    {
        StandardizeOptions a = new StandardizeOptions();
        StandardizeOptions b = a.Copy();
        Assert.True(SongPreviewLoader.SameInputs(a, b));

        b.IsRomaji = !a.IsRomaji;
        Assert.False(SongPreviewLoader.SameInputs(a, b));

        b = a.Copy();
        b.ApiKey = "another key";
        Assert.False(SongPreviewLoader.SameInputs(a, b));

        b = a.Copy();
        b.Separator = " / ";
        Assert.False(SongPreviewLoader.SameInputs(a, b));
    }

    // ---- threads ----

    /// <summary>A thread that runs the actions it is given one after the other: the stand-in for the UI thread.</summary>
    private sealed class EventThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;

        public EventThread()
        {
            _thread = new Thread(() =>
            {
                foreach (Action action in _queue.GetConsumingEnumerable())
                {
                    action();
                }
            }) { Name = "test-event-thread", IsBackground = true };
            _thread.Start();
        }

        public void Post(Action action)
        {
            _queue.Add(action);
        }

        public bool IsEventThread()
        {
            return Thread.CurrentThread == _thread;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }

    private sealed class ThreadNoting : SongPreviewLoader.IListener
    {
        private readonly Action<bool> _note;
        private readonly CountdownEvent _done;
        private readonly EventThread _events;

        public ThreadNoting(EventThread events, Action<bool> note, CountdownEvent done)
        {
            _events = events;
            _note = note;
            _done = done;
        }

        public void Lyrics(SongLyricsPreview preview)
        {
            _note(_events.IsEventThread());
            _done.Signal();
        }

        public void Tags(SongTagsPreview preview)
        {
            _note(_events.IsEventThread());
            _done.Signal();
        }
    }

    [Fact]
    public void TheRealLoaderRunsOffTheEventThreadAndAnswersOnIt()
    {
        using var events = new EventThread();
        loader = new SongPreviewLoader(Pipeline, events.Post);
        var done = new CountdownEvent(2);
        var onEventThread = new ConcurrentQueue<bool>();
        var jobThread = new ConcurrentQueue<string>();
        fake.DuringLyrics = () => jobThread.Enqueue(events.IsEventThread() ? "edt" : "background");

        loader.Load(FOLDER, song, options, new ThreadNoting(events, onEventThread.Enqueue, done));

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(new List<bool> { true, true }, onEventThread.ToList());
        Assert.Equal(new List<string> { "background" }, jobThread.ToList());
    }
}
