using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using MusicManager.App;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.ConvertLRC;
using MusicManager.InterfaceAdapter.Standardize;

namespace MusicManager.View.Library;

/// <summary>
/// What happens when the user presses Process (for a selection, for all songs, or for one song from its window):
/// Standardize mode previews what would change, shows the review page, and writes only after the user confirms;
/// the deprecated Convert mode (AppFeatures.LegacyConvertLrc) converts straight away.
///
/// The Java flow built the Swing ReviewPage itself and used SwingUtilities / SwingWorker. Here the review page is made by
/// the app through a <see cref="ReviewPageFactory"/>, the UI thread is reached through a dispatcher (an action that runs
/// another action on the UI thread) and the background work runs on tasks.
/// </summary>
public sealed class ProcessFlow
{
    /// <summary>What the window offers the flow.</summary>
    public interface IHost
    {
        /// <summary>The frame, as the parent of dialogs (an opaque handle: this layer only passes it on to the prompts).</summary>
        object Frame();

        /// <summary>Replaces the library page with the review page (the page the flow made through the factory).</summary>
        void ShowReview(object reviewPage);

        /// <summary>Back to the song list.</summary>
        void ShowLibrary();

        /// <summary>These songs (audio paths) were changed: re-read their tags and re-check their lyrics.</summary>
        void OnSongsProcessed(ISet<string> audioPaths);

        /// <summary>Convert mode only: a song was converted, or failed.</summary>
        void OnConvertProgress(string audioPath, bool success);

        /// <summary>True while a run is going, so the window can disable its Process buttons.</summary>
        void SetBusy(bool busy);
    }

    /// <summary>
    /// What the flow needs from the review page (the WinUI ReviewPage implements it): the flow starts its preview, feeds it
    /// the events of the process view model and asks whether it is still open.
    /// </summary>
    public interface IReviewPage
    {
        /// <summary>True once the page has been left (Back, or after a run that wrote something).</summary>
        bool IsClosed { get; }

        /// <summary>An event of the process view model, on whatever thread the use case runs.</summary>
        void OnViewModelEvent(string property);

        /// <summary>Runs the preview on background threads; the page shows what it finds as it arrives.</summary>
        void StartPreview();
    }

    /// <summary>
    /// The songs of an Add that wait for the user's decision. They are not in the library: they are staged, and the review
    /// shows them as they will be once they are in it. The review that was given them puts them into the library when the
    /// user applies it (<see cref="Commit"/> first, before anything is written) and throws them away when the user goes back
    /// (<see cref="Discard"/>), so that the library never has songs that were not standardized.
    /// </summary>
    public sealed class StagedSongs
    {
        private readonly ISet<string> _newPaths;
        private readonly ISet<string> _lyricsOnlyPaths;
        private readonly Func<(string Problem, int Songs)> _commit;
        private readonly Action _discard;

        public StagedSongs(IEnumerable<string> newPaths, IEnumerable<string> lyricsOnlyPaths,
                Func<(string Problem, int Songs)> commit, Action discard)
        {
            _newPaths = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
            _lyricsOnlyPaths = new HashSet<string>(lyricsOnlyPaths ?? new string[0], StringComparer.OrdinalIgnoreCase);
            _commit = commit;
            _discard = discard;
        }

        /// <summary>the staged songs, by the path they will have in the library</summary>
        public ISet<string> NewPaths => _newPaths;

        /// <summary>
        /// songs of the library that are given the lyric files of a copy that was left out: only their lyrics are standardized,
        /// the tags and the folder of a song that was in the library before are left as they are
        /// </summary>
        public ISet<string> LyricsOnlyPaths => _lyricsOnlyPaths;

        /// <summary>how many songs the last <see cref="Commit"/> put into the library</summary>
        public int CommittedSongs { get; private set; }

        /// <summary>
        /// Puts the songs into the library, all or none; null when that worked, otherwise why it did not (the library is as
        /// it was and the songs are still staged). Blocks, so call it off the UI thread.
        /// </summary>
        public string Commit()
        {
            (string problem, int songs) = _commit();
            if (problem == null)
            {
                CommittedSongs = songs;
            }
            return problem;
        }

        /// <summary>throws the staged songs away: the library has not changed</summary>
        public void Discard()
        {
            _discard();
        }
    }

    /// <summary>
    /// Makes the review page for a run: the host, the process controller and view model, the folder, the audio paths of the
    /// run, the options as they were when Process was pressed, the prompts, what to call when the page is left, and, for
    /// the songs of an Add, the songs that are still to be put into the library (otherwise null).
    /// </summary>
    public delegate IReviewPage ReviewPageFactory(IHost host, StandardizeController controller,
            StandardizeViewModel viewModel, string folder, ISet<string> scope, StandardizeOptions options,
            IProcessPrompts prompts, Action onClosed, StagedSongs staged);

    private readonly AppContext _context;
    private readonly IHost _host;
    private readonly bool _legacyConvert;
    private readonly IProcessPrompts _prompts;
    private readonly ReviewPageFactory _reviewPages;
    private readonly Action<Action> _dispatcher;

    /// <summary>The review that is open, or null; the view model has no way to remove a listener, so one listener serves them all.</summary>
    private volatile IReviewPage _review;

    private bool _starting; // a start is waiting for the API key lookup (UI thread only)

    // Convert mode: the run that is going, and the songs it converted
    private volatile bool _convertRunning;
    private readonly ConcurrentDictionary<string, byte> _convertedPaths = new();

    /// <param name="dispatcher">Runs an action on the UI thread (the Swing event thread of the Java flow).</param>
    public ProcessFlow(AppContext context, IHost host, IProcessPrompts prompts, ReviewPageFactory reviewPages,
            Action<Action> dispatcher)
        : this(context, host, AppFeatures.LegacyConvertLrc, prompts, reviewPages, dispatcher)
    {
    }

    /// <summary>The switch and the dialogs are parameters so that a test can run either mode and answer the questions itself.</summary>
    public ProcessFlow(AppContext context, IHost host, bool legacyConvert, IProcessPrompts prompts,
            ReviewPageFactory reviewPages, Action<Action> dispatcher)
    {
        _context = context;
        _host = host;
        _legacyConvert = legacyConvert;
        _prompts = prompts;
        _reviewPages = reviewPages;
        _dispatcher = dispatcher;

        if (context.ProcessViewModel != null)
        {
            context.ProcessViewModel.AddPropertyChangeListener(evt =>
            {
                IReviewPage page = _review;
                if (page != null)
                {
                    page.OnViewModelEvent(evt.PropertyName);
                }
            });
        }
        if (legacyConvert && context.ConvertViewModal != null)
        {
            context.ConvertViewModal.AddPropertyChangeListener(evt => OnConvertEvent(evt.PropertyName));
        }
    }

    /// <summary>
    /// Starts the run for these songs of the folder, with the options in the settings (not blocking: the work runs on
    /// background threads and reports to the host).
    /// </summary>
    public void Start(string folder, IReadOnlyCollection<Music> songs, AppSettings settings)
    {
        _dispatcher(() => StartOnUiThread(folder, songs, null, null, settings));
    }

    /// <summary>
    /// Starts the run for the songs an Add has just staged (paths as they are or will be in the library; the new ones are the
    /// staged songs). The review previews what the library will look like with them, and the songs go into the library only
    /// when the user applies it; when no review can be opened (nothing to do, no key) or the user goes back, they are
    /// thrown away.
    /// </summary>
    public void StartAdded(string folder, IReadOnlyCollection<string> paths, IReadOnlyCollection<string> newPaths,
            AppSettings settings)
    {
        StartAdded(folder, paths, newPaths, null, settings);
    }

    /// <param name="lyricsOnlyPaths">
    /// songs of the library that only get lyric files staged for them: they are part of the run for their lyrics, not for
    /// their tags and folders (see <see cref="StagedSongs.LyricsOnlyPaths"/>)
    /// </param>
    public void StartAdded(string folder, IReadOnlyCollection<string> paths, IReadOnlyCollection<string> newPaths,
            IReadOnlyCollection<string> lyricsOnlyPaths, AppSettings settings)
    {
        bool anythingStaged = (newPaths != null && newPaths.Count != 0) || (lyricsOnlyPaths != null && lyricsOnlyPaths.Count != 0);
        StagedSongs staged = !anythingStaged ? null : new StagedSongs(newPaths ?? new string[0], lyricsOnlyPaths, () =>
        {
            _context.AddSongsController.Commit(); // (the presenter has stored the result when this returns)
            var state = _context.AddSongsViewModel.State;
            return (state.CommitProblem, state.CommittedSongs);
        }, () => _context.AddSongsController.Discard());
        _dispatcher(() => StartOnUiThread(folder, null, paths, staged, settings));
    }

    private void StartOnUiThread(string folder, IReadOnlyCollection<Music> songs, IReadOnlyCollection<string> paths,
            StagedSongs staged, AppSettings settings)
    {
        int count = songs != null ? songs.Count : paths == null ? 0 : paths.Count;
        if (count == 0)
        {
            staged?.Discard();
            _prompts.Info(_host.Frame(), "Process", "There are no songs to process.");
            return;
        }
        if ((_review != null && !_review.IsClosed) || _convertRunning || _starting)
        {
            staged?.Discard();
            return; // a run is already going, or its review is open
        }

        StandardizeOptions options = settings.StandardizeOptions.Copy(); // what is in the window now, whatever happens later
        if (staged == null && songs != null && _legacyConvert && options.GetMode() == StandardizeOptions.Mode.Convert)
        {
            StartConvert(new List<Music>(songs), settings); // (the caller's list may change while the run is starting)
        }
        else
        {
            List<string> scope = songs != null ? songs.Select(song => song.Path).ToList() : new List<string>(paths);
            StartStandardize(folder, scope, options, staged);
        }
    }

    // ------------------------------------------------------------------ Standardize

    private void StartStandardize(string folder, List<string> songs, StandardizeOptions options, StagedSongs staged)
    {
        if (!options.IsLyrics && !options.IsArtists && !options.IsAlbums)
        {
            staged?.Discard();
            _prompts.Info(_host.Frame(), "Nothing to do", staged == null
                    ? "Nothing to do: turn on lyrics, artists or albums in Options."
                    : "Nothing was added to the library. Turn on lyrics, artists or albums in Options, then add the songs again.");
            return;
        }
        if (!ReviewText.NeedsKeyLookup(options))
        {
            OpenReview(folder, songs, options, true, staged);
            return;
        }
        // Claude is needed and no key was typed: looking for one in the Windows environment can start a process or two,
        // which does not belong on the UI thread
        _starting = true;
        Task.Run(() => _context.ProcessController.IsApiKeyAvailable).ContinueWith(task =>
        {
            _dispatcher(() =>
            {
                _starting = false;
                // a lookup that failed counts as "not found": the message of OpenReview says what to do
                bool available = task.Status == TaskStatus.RanToCompletion && task.Result;
                OpenReview(folder, songs, options, available, staged);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>The review page for these songs, or, when Claude is needed and there is no key anywhere, the message saying so.</summary>
    private void OpenReview(string folder, List<string> songs, StandardizeOptions options, bool keyInEnvironment,
            StagedSongs staged)
    {
        // better to say so now than after the preview, or, worse, after the artist names were already rewritten
        string keyProblem = ReviewText.MissingKeyMessage(options, keyInEnvironment);
        if (keyProblem != null)
        {
            staged?.Discard();
            _prompts.Warn(_host.Frame(), "Anthropic API key", staged == null
                    ? keyProblem : keyProblem + "\n\nNothing was added to the library; add the songs again after that.");
            return;
        }

        var scope = new HashSet<string>(songs);
        IReviewPage page;
        try
        {
            page = _reviewPages(_host, _context.ProcessController, _context.ProcessViewModel, folder,
                    new ReadOnlySet<string>(scope), options, _prompts, () => _review = null, staged);
        }
        catch (Exception)
        {
            staged?.Discard();
            throw;
        }
        _review = page;
        _host.ShowReview(page);
        page.StartPreview();
    }

    // ------------------------------------------------------------------ Convert (deprecated, behind the switch)

    private void StartConvert(List<Music> songs, AppSettings settings)
    {
        List<string> formats = settings.DefaultTargetFormats == null ? new List<string>()
                : new List<string>(settings.DefaultTargetFormats);
        if (formats.Count == 0)
        {
            _prompts.Warn(_host.Frame(), "No formats selected", "Select at least one format to convert to in Options.");
            return;
        }

        _convertedPaths.Clear();
        _convertRunning = true;
        _host.SetBusy(true);
        var toConvert = new List<Music>(songs);
        Task.Run(() => _context.ConvertController.Execute(toConvert, formats)).ContinueWith(task =>
        {
            if (!task.IsFaulted)
            {
                return;
            }
            Exception failure = task.Exception.InnerException ?? task.Exception;
            _dispatcher(() =>
            {
                // the use case reports its own failures through the view model; this is something it did not expect
                if (_convertRunning)
                {
                    _convertRunning = false;
                    _host.SetBusy(false);
                    _prompts.Error(_host.Frame(), "Conversion result", "Converting stopped unexpectedly: " + ThrowableText(failure));
                }
            });
        }, TaskScheduler.Default);
    }

    /// <summary>An event of the Convert view model, on whatever thread the use case runs: read the state here, then use the UI thread.</summary>
    private void OnConvertEvent(string property)
    {
        if (!_convertRunning)
        {
            return;
        }
        ConvertLRCState state = _context.ConvertViewModal.State;
        if (property == "progress")
        {
            string audioPath = state.LastProgressMusicPath;
            bool success = state.IsLastProgressSuccess;
            if (success && audioPath != null)
            {
                _convertedPaths.TryAdd(audioPath, 0);
            }
            _dispatcher(() => _host.OnConvertProgress(audioPath, success));
        }
        else if (property == "state")
        {
            string message = state.Message;
            bool failures = state.FailCount > 0;
            var converted = new HashSet<string>(_convertedPaths.Keys);
            _convertRunning = false;
            _dispatcher(() =>
            {
                _host.SetBusy(false);
                if (failures)
                {
                    _prompts.Warn(_host.Frame(), "Conversion result", message);
                }
                else
                {
                    _prompts.Info(_host.Frame(), "Conversion result", message);
                }
                if (converted.Count != 0)
                {
                    _host.OnSongsProcessed(converted); // new lyric files exist now: the songs' statuses may differ
                }
            });
        }
    }

    /// <summary>The Java <c>Throwable.toString()</c>: the type, and the message when there is one.</summary>
    private static string ThrowableText(Exception failure)
    {
        string type = failure.GetType().FullName;
        return string.IsNullOrEmpty(failure.Message) ? type : type + ": " + failure.Message;
    }
}
