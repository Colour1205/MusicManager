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
    /// Makes the review page for a run: the host, the process controller and view model, the folder, the audio paths of the
    /// run, the options as they were when Process was pressed, the prompts, and what to call when the page is left.
    /// </summary>
    public delegate IReviewPage ReviewPageFactory(IHost host, StandardizeController controller,
            StandardizeViewModel viewModel, string folder, ISet<string> scope, StandardizeOptions options,
            IProcessPrompts prompts, Action onClosed);

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
        _dispatcher(() => StartOnUiThread(folder, songs, settings));
    }

    private void StartOnUiThread(string folder, IReadOnlyCollection<Music> songs, AppSettings settings)
    {
        if (songs == null || songs.Count == 0)
        {
            _prompts.Info(_host.Frame(), "Process", "There are no songs to process.");
            return;
        }
        if ((_review != null && !_review.IsClosed) || _convertRunning || _starting)
        {
            return; // a run is already going, or its review is open
        }

        var chosen = new List<Music>(songs); // the caller's list may change while the run is starting
        StandardizeOptions options = settings.StandardizeOptions.Copy(); // what is in the window now, whatever happens later
        if (_legacyConvert && options.GetMode() == StandardizeOptions.Mode.Convert)
        {
            StartConvert(chosen, settings);
        }
        else
        {
            StartStandardize(folder, chosen, options);
        }
    }

    // ------------------------------------------------------------------ Standardize

    private void StartStandardize(string folder, List<Music> songs, StandardizeOptions options)
    {
        if (!options.IsLyrics && !options.IsArtists)
        {
            _prompts.Info(_host.Frame(), "Nothing to do", "Nothing to do: turn on lyrics or artists in Options.");
            return;
        }
        if (!ReviewText.NeedsKeyLookup(options))
        {
            OpenReview(folder, songs, options, true);
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
                OpenReview(folder, songs, options, available);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>The review page for these songs, or, when Claude is needed and there is no key anywhere, the message saying so.</summary>
    private void OpenReview(string folder, List<Music> songs, StandardizeOptions options, bool keyInEnvironment)
    {
        // better to say so now than after the preview, or, worse, after the artist names were already rewritten
        string keyProblem = ReviewText.MissingKeyMessage(options, keyInEnvironment);
        if (keyProblem != null)
        {
            _prompts.Warn(_host.Frame(), "Anthropic API key", keyProblem);
            return;
        }

        var scope = new HashSet<string>();
        foreach (Music song in songs)
        {
            scope.Add(song.Path);
        }
        IReviewPage page = _reviewPages(_host, _context.ProcessController, _context.ProcessViewModel, folder,
                new ReadOnlySet<string>(scope), options, _prompts, () => _review = null);
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
