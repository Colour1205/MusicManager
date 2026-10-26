using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.View.Library;

/// <summary>
/// Works out, in the background, what Process would do to ONE song, for the song window: first the lyrics part (files
/// and lines, which may ask Claude for the romaji), then the tags part (artist names, which reads the whole folder and
/// may ask Claude too). Each part is handed to the listener as soon as it is ready, on the UI thread.
///
/// Every <see cref="Load"/> runs on a pipeline of its own (a new controller and view model), so the previews of different
/// windows, or of the same window after its Options changed, never see each other's events. The use cases cannot be
/// interrupted: <see cref="IHandle.Cancel"/> only drops what they find from then on. It has no UI component in it, so it
/// can be tested without a display.
/// </summary>
public sealed class SongPreviewLoader
{
    /// <summary>Told what the job finds, on the UI thread (or whatever the loader was given to deliver on).</summary>
    public interface IListener
    {
        /// <summary>The lyrics part is ready (also when it failed or is off: the preview then holds the sentence to show).</summary>
        void Lyrics(SongLyricsPreview preview);

        /// <summary>The tags part is ready.</summary>
        void Tags(SongTagsPreview preview);

        /// <summary>What the lyrics part is doing now, e.g. "Asking Claude for the romaji..."; only while it is not ready.</summary>
        void Progress(string text)
        {
        }

        /// <summary>What the tags part is doing now, e.g. "Asking Claude to group 12 artist names...".</summary>
        void TagsProgress(string text)
        {
        }
    }

    /// <summary>What a running job gives back.</summary>
    public interface IHandle
    {
        /// <summary>Nothing more is delivered after this; the job's own work is not interrupted, its result is dropped.</summary>
        void Cancel();
    }

    /// <summary>Runs a job on a thread of its own, like a cached thread pool of daemon threads.</summary>
    private static readonly Action<Action> Background = job =>
    {
        var thread = new Thread(() => job()) { Name = "song-preview", IsBackground = true };
        thread.Start();
    };

    private static readonly Regex LinesDone = new(@"\(([0-9]+) of ([0-9]+) lines?\)", RegexOptions.CultureInvariant);

    private readonly Func<StandardizePipeline> _pipelines;
    private readonly Action<Action> _deliverOn;
    private readonly Action<Action> _worker;

    /// <summary>
    /// The pipelines come from <see cref="AppContext.PreviewPipelines"/>; answers are delivered through the dispatcher,
    /// which must run an action on the UI thread.
    /// </summary>
    public SongPreviewLoader(Func<StandardizePipeline> pipelines, Action<Action> deliverOn)
        : this(pipelines, deliverOn, Background)
    {
    }

    /// <summary>The thread the job runs on and the thread the listener hears on are parameters so that a test can control both.</summary>
    public SongPreviewLoader(Func<StandardizePipeline> pipelines, Action<Action> deliverOn, Action<Action> worker)
    {
        _pipelines = pipelines;
        _deliverOn = deliverOn;
        _worker = worker;
    }

    /// <summary>
    /// Starts the job for one song of the folder with the options as they are now (they are copied: a later change does
    /// not reach the job). Returns at once.
    /// </summary>
    /// <param name="folder">The library folder the song is in; the names of the artists are decided from all of it.</param>
    public IHandle Load(string folder, Music song, StandardizeOptions options, IListener listener)
    {
        var job = new Job(this, folder, song, options.Copy(), listener);
        try
        {
            _worker(job.Run);
        }
        catch (Exception e) // no thread to run on: the window must not wait for ever
        {
            job.Fail(Describe(e));
        }
        return job;
    }

    /// <summary>True when the two Options give the same preview: a change of anything else does not need a new one.</summary>
    public static bool SameInputs(StandardizeOptions a, StandardizeOptions b)
    {
        return a.GetMode() == b.GetMode() && a.IsLyrics == b.IsLyrics && a.IsRomaji == b.IsRomaji
                && a.IsRecycleBin == b.IsRecycleBin && a.IsArtists == b.IsArtists
                && a.IsUseClaudeForArtists == b.IsUseClaudeForArtists && a.Separator == b.Separator
                && a.Model == b.Model && a.ApiKey == b.ApiKey;
    }

    /// <summary>One load: what it reads from the pipeline's view model while a step runs, and what it has delivered.</summary>
    private sealed class Job : IHandle
    {
        private readonly SongPreviewLoader _loader;
        private readonly string _folder;
        private readonly Music _song;
        private readonly StandardizeOptions _options;
        private readonly IListener _listener;

        private volatile bool _cancelled;
        private bool _lyricsDelivered; // job thread only

        // what the events of the running step carried (the view model's listener runs on the job thread)
        private StandardizeLyricsOutputData _lyricsOutput;
        private List<SongArtistUpdate> _artistUpdates;
        private string _error;
        private Action<string> _progressTo = text => { };

        public Job(SongPreviewLoader loader, string folder, Music song, StandardizeOptions options, IListener listener)
        {
            _loader = loader;
            _folder = folder;
            _song = song;
            _options = options;
            _listener = listener;
        }

        public void Cancel()
        {
            _cancelled = true;
        }

        public void Run()
        {
            try
            {
                if (_cancelled)
                {
                    return;
                }
                StandardizePipeline pipeline = _loader._pipelines();
                Listen(pipeline.ViewModel);

                SongLyricsPreview lyrics = LyricsStep(pipeline);
                _lyricsDelivered = true;
                Deliver(() => _listener.Lyrics(lyrics));
                if (_cancelled)
                {
                    return; // the window is gone: do not ask Claude about the artists for nobody
                }

                SongTagsPreview tags = TagsStep(pipeline);
                Deliver(() => _listener.Tags(tags));
            }
            catch (Exception e) // whatever happened, the window must not stay on "working..."
            {
                Fail(Describe(e));
            }
        }

        /// <summary>The answer for a job that could not even start, or died: the part that is not delivered yet shows why.</summary>
        public void Fail(string reason)
        {
            if (!_lyricsDelivered)
            {
                _lyricsDelivered = true;
                Deliver(() => _listener.Lyrics(SongPreviewMapper.LyricsFailed(reason)));
            }
            Deliver(() => _listener.Tags(SongPreviewMapper.TagsFailed(reason)));
        }

        private void Deliver(Action action)
        {
            if (_cancelled)
            {
                return;
            }
            _loader._deliverOn(() =>
            {
                if (!_cancelled) // checked again: the user may have closed the window while this was waiting
                {
                    action();
                }
            });
        }

        private void Listen(StandardizeViewModel viewModel)
        {
            viewModel.AddPropertyChangeListener(evt =>
            {
                StandardizeState state = viewModel.State;
                string property = evt.PropertyName;
                if (property == StandardizeViewModel.Lyrics)
                {
                    _lyricsOutput = state.LyricsResult;
                }
                else if (property == StandardizeViewModel.ArtistsAnalysis)
                {
                    _artistUpdates = state.ArtistUpdates;
                }
                else if (property == StandardizeViewModel.Error)
                {
                    _error = state.ErrorMessage;
                }
                else if (property == StandardizeViewModel.Progress)
                {
                    _progressTo(state.ProgressMessage);
                }
            });
        }

        // -------------------------------------------------------------- the lyrics part

        private SongLyricsPreview LyricsStep(StandardizePipeline pipeline)
        {
            if (_options.GetMode() == StandardizeOptions.Mode.Convert)
            {
                return SongPreviewMapper.LyricsOff("There is no preview in Convert mode.");
            }
            if (!_options.IsLyrics)
            {
                return SongPreviewMapper.LyricsOff("The lyrics are off in Options.");
            }
            _error = null;
            _lyricsOutput = null;
            _progressTo = text =>
            {
                string shown = LyricsProgressText(text);
                if (shown != null)
                {
                    Deliver(() => _listener.Progress(shown));
                }
            };
            try
            {
                pipeline.Controller.PreviewSongLyrics(_folder, _options.IsRecycleBin, _options.IsRomaji,
                        _options.ApiKey, _options.Model, new HashSet<string> { _song.Path });
            }
            catch (Exception e)
            {
                return SongPreviewMapper.LyricsFailed(Describe(e));
            }
            finally
            {
                _progressTo = text => { };
            }
            if (_error != null)
            {
                return SongPreviewMapper.LyricsFailed(_error);
            }
            SongLyricsResult result = ResultOf(_lyricsOutput, _song.Path);
            if (result == null)
            {
                return SongPreviewMapper.LyricsFailed("This song was not found in the folder.");
            }
            try
            {
                return SongPreviewMapper.Lyrics(result, _options);
            }
            catch (Exception e)
            {
                return SongPreviewMapper.LyricsFailed(Describe(e));
            }
        }

        // -------------------------------------------------------------- the tags part

        private SongTagsPreview TagsStep(StandardizePipeline pipeline)
        {
            if (_options.GetMode() == StandardizeOptions.Mode.Convert)
            {
                return new SongTagsPreview(null, "");
            }
            if (!_options.IsArtists)
            {
                return SongPreviewMapper.TagsOff();
            }
            _error = null;
            _artistUpdates = null;
            _progressTo = text =>
            {
                if (text != null && !JavaString.IsBlank(text))
                {
                    Deliver(() => _listener.TagsProgress(text));
                }
            };
            try
            {
                pipeline.Controller.AnalyzeArtists(_folder, _options.Separator, _options.IsUseClaudeForArtists,
                        _options.ApiKey, _options.Model, new HashSet<string> { _song.Path });
            }
            catch (Exception e)
            {
                return SongPreviewMapper.TagsFailed(Describe(e));
            }
            finally
            {
                _progressTo = text => { };
            }
            if (_error != null)
            {
                return SongPreviewMapper.TagsFailed(_error);
            }
            if (_artistUpdates == null)
            {
                return SongPreviewMapper.TagsFailed("The artist names could not be checked.");
            }
            var mine = new List<SongArtistUpdate>();
            foreach (SongArtistUpdate update in _artistUpdates)
            {
                if (SamePath(update.AudioPath, _song.Path))
                {
                    mine.Add(update);
                }
            }
            try
            {
                return SongPreviewMapper.Tags(mine, _options);
            }
            catch (Exception e)
            {
                return SongPreviewMapper.TagsFailed(Describe(e));
            }
        }
    }

    // ------------------------------------------------------------------ small helpers

    /// <summary>The result of this song: the one with its path, or the only result there is (the run was limited to this song).</summary>
    private static SongLyricsResult ResultOf(StandardizeLyricsOutputData output, string audioPath)
    {
        if (output == null || output.Results == null)
        {
            return null;
        }
        var results = output.Results;
        foreach (SongLyricsResult result in results)
        {
            if (result != null && SamePath(result.AudioPath, audioPath))
            {
                return result;
            }
        }
        return results.Count == 1 ? results[0] : null;
    }

    private static bool SamePath(string a, string b)
    {
        if (a == null || b == null)
        {
            return false;
        }
        if (a == b)
        {
            return true;
        }
        try
        {
            // Java's Path.equals on Windows ignores the case
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// What the window shows while the lyrics part works: only the Claude requests are worth saying (the per-song
    /// messages of the use case are about a folder, and a single song is done in no time). null = nothing to show.
    /// </summary>
    public static string LyricsProgressText(string message)
    {
        if (message == null)
        {
            return null;
        }
        string lower = message.ToLowerInvariant();
        if (!lower.Contains("romaji", StringComparison.Ordinal) && !lower.Contains("claude", StringComparison.Ordinal))
        {
            return null;
        }
        Match lines = LinesDone.Match(message);
        if (lines.Success)
        {
            return "Asking Claude for the romaji... " + lines.Groups[1].Value + " of " + lines.Groups[2].Value + " lines";
        }
        return "Asking Claude for the romaji...";
    }

    /// <summary>The Java <c>failure.getMessage()</c>, or its <c>toString()</c> when there is none.</summary>
    private static string Describe(Exception failure)
    {
        return string.IsNullOrEmpty(failure.Message) || JavaString.IsBlank(failure.Message)
                ? failure.GetType().FullName : failure.Message;
    }
}
