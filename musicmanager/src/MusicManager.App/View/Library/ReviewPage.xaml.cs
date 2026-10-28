using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// The review page (Java: view/Library/ReviewPage): what a preview of Process found, as cards the user can read (and, for
/// artist names, correct) before anything is written. Sections Lyrics / Artists / Tag changes (only those the options
/// turned on), a status bar with a progress bar, Back (nothing written) and Apply (artists first and lyrics second).
///
/// It is a page of the one window: ProcessFlow gives it to Host.ShowReview and feeds it the events of the process view
/// model; it returns to the library through Host.ShowLibrary on Back and after a successful run. The title bar's Back
/// button asks it through <see cref="IBackHandler"/>.
/// </summary>
public sealed partial class ReviewPage : Page, ProcessFlow.IReviewPage, IBackHandler
{
    private const int Lyrics = 0;
    private const int Artists = 1;
    private const int Albums = 2;
    private const int Folders = 3;
    private const int Changes = 4;
    private const string Arrow = "  →  ";
    private const int MaxErrorTextInBar = 500;
    private const int MaxRowsOpened = 400; // a folder tree with more rows than this starts with only the library folder open

    private readonly ProcessFlow.IHost _host;
    private readonly StandardizeController _controller;
    private readonly StandardizeViewModel _viewModel;
    private readonly IProcessPrompts _prompts;
    private readonly Action _onClosed;
    private readonly string _folder;
    private readonly ISet<string> _scope;
    private readonly ISet<string> _tagScope; // the songs whose tags, names and folders may change: the run's songs less those that only get lyrics
    private readonly ProcessFlow.StagedSongs _staged; // the songs of an Add that are not in the library yet, or null
    private readonly StandardizeOptions _options;
    private readonly bool _doLyrics;
    private readonly bool _doArtists;
    private readonly bool _doAlbums;
    private readonly DispatcherQueue _dispatcher; // the UI thread, which the view model events have to be moved to

    private readonly FrameworkElement[] _lists; // what each section shows (a list, or for the folders a panel with the tree)
    private readonly TextBlock[] _headlines;
    private readonly TextBlock[] _details;
    private readonly SelectorBarItem[] _sectionItems;
    private readonly SkeletonLines[] _skeletons; // the redacted blocks in the list header of each section
    private readonly int[] _rowCounts = new int[5]; // how many rows each section has been given so far
    private List<ArtistGroupItem> _artistItems = new List<ArtistGroupItem>();
    private List<AlbumGroupItem> _albumItems = new List<AlbumGroupItem>();

    // what the preview found (touched on the UI thread only)
    private ArtistPlan _currentPlan;
    private int _artistUpdateCount;
    private List<SongArtistUpdate> _artistUpdates = new List<SongArtistUpdate>();
    private AlbumPlan _currentAlbumPlan;
    private List<SongAlbumUpdate> _albumUpdates = new List<SongAlbumUpdate>();
    private List<AlbumFolderMove> _albumMoves = new List<AlbumFolderMove>();
    private List<SongRename> _artistRenames = new List<SongRename>(); // the files that will be named "Artist - Title": the folder tree shows the names
    private int _albumUpdateCount; // songs whose album tag will be written
    private int _albumMoveCount; // songs that will be put in another folder
    private int _renameCount; // songs whose files are not named "Artist - Title" after their tags: they are renamed with the rest
    private StandardizeLyricsOutputData _lyricsPreview;
    private bool _lyricsHasWork;
    private bool _busy;
    private bool _previewing; // the busy run is the preview (it shows the status text and, for a section without rows, redacted blocks)
    private bool _applied;
    private bool _previewFailed;
    private volatile bool _addCommitted; // the staged songs are in the library now: going back does not drop them any more
    private volatile string _addedSummary = "";
    private volatile bool _closed;
    private int _shownSection = -1;
    private bool _selecting; // the code is changing the selection of the section bar

    // written by the view model events, which arrive on the worker threads, and read when a run is over
    private readonly List<string> _runErrors = new List<string>();
    private volatile string _lyricsDoneSummary = "";
    private volatile string _artistsDoneSummary = "";
    private volatile string _renamedDoneSummary = "";
    private volatile string _albumsDoneSummary = "";
    private volatile string _foldersDoneSummary = "";
    private volatile IReadOnlyDictionary<string, string> _movedPaths = new Dictionary<string, string>(); // song moved to an album folder: path before -> after

    public ReviewPage(ProcessFlow.IHost host, StandardizeController controller, StandardizeViewModel viewModel,
            string folder, ISet<string> scope, StandardizeOptions options, IProcessPrompts prompts, Action onClosed)
        : this(host, controller, viewModel, folder, scope, options, prompts, onClosed, null)
    {
    }

    /// <param name="staged">the songs of an Add that are to be put into the library when the user applies, or null</param>
    public ReviewPage(ProcessFlow.IHost host, StandardizeController controller, StandardizeViewModel viewModel,
            string folder, ISet<string> scope, StandardizeOptions options, IProcessPrompts prompts, Action onClosed,
            ProcessFlow.StagedSongs staged)
    {
        _host = host;
        _controller = controller;
        _viewModel = viewModel;
        _folder = folder;
        _scope = scope;
        _staged = staged;
        _tagScope = staged == null || staged.LyricsOnlyPaths.Count == 0
                ? scope : new HashSet<string>(scope.Where(path => !staged.LyricsOnlyPaths.Contains(path)));
        _options = options;
        _prompts = prompts;
        _onClosed = onClosed;
        _doLyrics = options.IsLyrics;
        _doArtists = options.IsArtists;
        _doAlbums = options.IsAlbums;

        InitializeComponent();
        _dispatcher = DispatcherQueue;
        _lists = new FrameworkElement[] { LyricsList, ArtistList, AlbumList, FolderPanel, ChangesList };
        _headlines = new[] { LyricsHeadline, ArtistsHeadline, AlbumsHeadline, FoldersHeadline, ChangesHeadline };
        _details = new[] { LyricsDetail, ArtistsDetail, AlbumsDetail, FoldersDetail, ChangesDetail };
        _sectionItems = new[] { LyricsItem, ArtistsItem, AlbumsItem, FoldersItem, ChangesItem };
        _skeletons = new[] { LyricsSkeleton, ArtistsSkeleton, AlbumsSkeleton, FoldersSkeleton, ChangesSkeleton };

        CaptionText.Text = staged == null
                ? ReviewText.SongCount(scope.Count) + ": continue by pressing Apply"
                : staged.NewPaths.Count == 0 ? ReviewText.LyricsOnlyCaption(staged.LyricsOnlyPaths.Count)
                : ReviewText.AddCaption(staged.NewPaths.Count);
        BuildTop();
        for (int section = 0; section < _lists.Length; section++)
        {
            SetSummary(section, "", "");
        }
        SelectSection(_doLyrics ? Lyrics : _doArtists ? Artists : Albums);
    }

    // ------------------------------------------------------------------ layout

    private void BuildTop()
    {
        var sections = new List<int>();
        if (_doLyrics)
        {
            sections.Add(Lyrics);
        }
        if (_doArtists)
        {
            sections.Add(Artists);
        }
        if (_doAlbums)
        {
            sections.Add(Albums);
            sections.Add(Folders); // the album folders are what the albums decide
        }
        if (_doArtists || _doAlbums)
        {
            sections.Add(Changes);
        }
        for (int section = 0; section < _sectionItems.Length; section++)
        {
            _sectionItems[section].Visibility = sections.Contains(section) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (sections.Count > 1) // a single section needs no switch
        {
            SectionBar.Visibility = Visibility.Visible;
        }

        if (_doLyrics && _options.IsRomaji)
        {
            // what romaji does, in the words of the Options window; the sentence about the old Chinese-character romaji stands out
            RomajiNote.Message = ReviewText.RomajiAdds + " " + ReviewText.RomajiReplaces + " " + ReviewText.RomajiKeeps;
            RomajiNote.Visibility = Visibility.Visible;
        }
    }

    // ------------------------------------------------------------------ sections

    private void OnSectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_selecting)
        {
            return;
        }
        int section = Array.IndexOf(_sectionItems, sender.SelectedItem);
        if (section >= 0)
        {
            SelectSection(section);
        }
    }

    private void SelectSection(int section)
    {
        _shownSection = section;
        if (SectionBar.Visibility == Visibility.Visible && SectionBar.SelectedItem != _sectionItems[section])
        {
            _selecting = true;
            try
            {
                SectionBar.SelectedItem = _sectionItems[section];
            }
            finally
            {
                _selecting = false;
            }
        }
        for (int i = 0; i < _lists.Length; i++)
        {
            _lists[i].Visibility = i == section ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SetSummary(int section, string headline, string detail)
    {
        _headlines[section].Text = headline;
        _headlines[section].Visibility = headline.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _details[section].Text = detail; // the detail can run to several lines; the block wraps at \n
        _details[section].Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>a summary from the presenter is "headline\ndetail"</summary>
    private void SetSummary(int section, string summary)
    {
        string[] parts = ReviewText.SplitSummary(summary);
        SetSummary(section, parts[0], parts[1]);
    }

    // ------------------------------------------------------------------ runs

    /// <summary>runs the preview on background threads; the page shows what it finds as it arrives</summary>
    public void StartPreview()
    {
        _previewFailed = false;
        _applied = false;
        ClearErrors();
        _previewing = true;
        SetBusy(true);

        bool lyrics = _doLyrics;
        bool artists = _doArtists;
        bool albums = _doAlbums;
        bool trash = _options.IsRecycleBin;
        bool romanize = _doLyrics && _options.IsRomaji;
        string separator = _options.Separator;
        bool useAi = _options.IsUseClaudeForArtists;
        bool useAiForAlbums = _options.IsUseClaudeForAlbums;
        string apiKey = _options.ApiKey;
        string model = _options.Model;

        RunInBackground(() =>
        {
            if (lyrics)
            {
                _controller.StandardizeLyrics(_folder, true, trash, romanize, apiKey, model, _scope);
            }
            if (artists)
            {
                _controller.AnalyzeArtists(_folder, separator, useAi, apiKey, model, _tagScope);
            }
            if (albums)
            {
                _controller.AnalyzeAlbums(_folder, useAiForAlbums, apiKey, model, _tagScope);
            }
        }, FinishPreview);
    }

    private async void FinishPreview(Exception failure)
    {
        if (_closed)
        {
            return;
        }
        if (failure != null)
        {
            AddError("The preview stopped unexpectedly: " + Describe(failure));
        }
        bool failed = HasErrors();
        _previewFailed = failed;
        _previewing = false;
        bool work = HasWork();
        if (failed)
        {
            string errors = string.Join("\n\n", ErrorsNow());
            SetBusy(false, "The preview could not be completed. Nothing was changed.", Shorten(errors), InfoBarSeverity.Error);
            await ErrorAsync("Standardize", errors);
        }
        else
        {
            SetBusy(false, work ? "Preview ready. Nothing has been written yet." : "Nothing to change in these songs.");
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _applied || _previewFailed || _closed)
        {
            return;
        }
        bool applyLyrics = _doLyrics && _lyricsHasWork;
        bool applyArtists = _doArtists && _currentPlan != null && _artistUpdateCount > 0;
        bool applyAlbums = _doAlbums && _currentAlbumPlan != null && _albumUpdateCount > 0;
        bool applyFolders = _doAlbums && _currentAlbumPlan != null && _albumMoveCount > 0;
        bool applyRename = _doArtists && _currentPlan != null && _renameCount > 0;
        bool addSongs = _staged != null && !_addCommitted; // the songs of an Add go into the library, whatever else there is to do
        if (!applyLyrics && !applyArtists && !applyAlbums && !applyFolders && !applyRename && !addSongs)
        {
            return;
        }

        _lyricsDoneSummary = "";
        _artistsDoneSummary = "";
        _renamedDoneSummary = "";
        _albumsDoneSummary = "";
        _foldersDoneSummary = "";
        _movedPaths = new Dictionary<string, string>();
        ClearErrors();
        SetBusy(true);
        ArtistPlan plan = _currentPlan;
        AlbumPlan albumPlan = _currentAlbumPlan;
        bool trash = _options.IsRecycleBin;
        bool romanize = _options.IsRomaji;
        string apiKey = _options.ApiKey;
        string model = _options.Model;

        RunInBackground(() =>
        {
            // the songs of an Add go into the library first, all or none: everything below works on them where they will be
            // (the preview was made at those paths), and until now the library had not been touched
            if (addSongs)
            {
                string problem = _staged.Commit();
                if (problem != null)
                {
                    AddError(problem);
                    return;
                }
                _addCommitted = true;
                _addedSummary = ReviewText.AddedSummary(_staged.CommittedSongs);
            }
            // the tags first, so the .lrc header tags written next carry the standardized names
            if (applyArtists)
            {
                _controller.ApplyArtists(plan);
            }
            if (applyAlbums)
            {
                _controller.ApplyAlbums(albumPlan);
            }
            if (applyLyrics)
            {
                _controller.StandardizeLyrics(_folder, false, trash, romanize, apiKey, model, _scope);
            }
            // the songs go into the folders named after their albums: the lyrics above find them by their old paths
            if (applyFolders)
            {
                _controller.MoveAlbumFolders(albumPlan);
            }
            // the files are named after the tags that were just written, last: they are found where the folders put them
            if (applyRename)
            {
                _controller.RenameFiles(plan, _movedPaths);
            }
        }, FinishApply);
    }

    private async void FinishApply(Exception failure)
    {
        if (_closed)
        {
            return;
        }
        if (failure != null)
        {
            AddError("Standardizing stopped unexpectedly: " + Describe(failure));
        }
        string done = ReviewText.WithAdded(_addedSummary, ReviewText.ResultMessage(_artistsDoneSummary, _lyricsDoneSummary,
                _renamedDoneSummary, _albumsDoneSummary, _foldersDoneSummary));
        bool wroteSomething = done.Length != 0;

        if (!HasErrors())
        {
            _applied = true;
            SetBusy(false, "Done.");
            await InfoAsync("Standardize result", done.Length == 0 ? "Nothing needed to change." : done);
            Leave(true);
            return;
        }

        string error = string.Join("\n\n", ErrorsNow());
        if (wroteSomething)
        {
            // the artists step ran before the lyrics step failed: say what did change
            _applied = true;
            SetBusy(false, "Stopped early. Some changes were made.", Shorten(error), InfoBarSeverity.Error);
            await ErrorAsync("Standardize", error + "\n\nThis was already done before it stopped:\n\n" + done);
            Leave(true);
        }
        else
        {
            SetBusy(false, "Nothing was changed.", Shorten(error), InfoBarSeverity.Error);
            await ErrorAsync("Standardize", error);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }
        Leave(false);
    }

    /// <summary>The title bar's Back button: leave like the Back button of the page; not while a run is going.</summary>
    public bool OnBackRequested()
    {
        if (!_busy && !_closed)
        {
            Leave(false);
        }
        return true; // handled either way: a preview or a run that is going is not left half-way
    }

    /// <summary>back to the song list; after a run that wrote something, the library is told which songs to re-read first</summary>
    private void Leave(bool processed)
    {
        _closed = true;
        _onClosed();
        if (_staged != null && !_addCommitted)
        {
            _staged.Discard(); // the songs were never put into the library: there is nothing to undo
        }
        if (processed)
        {
            _host.OnSongsProcessed(_scope);
        }
        _host.ShowLibrary();
    }

    /// <summary>the user edited an artist name: have the preview of tag changes recomputed</summary>
    private void OnArtistEdited()
    {
        if (_currentPlan != null && !_busy && !_closed)
        {
            ArtistPlan plan = _currentPlan;
            RunInBackground(() => _controller.ReviewArtists(plan), async failure =>
            {
                if (failure != null && !_closed)
                {
                    await ErrorAsync("Standardize", "The tag changes could not be recomputed: " + Describe(failure));
                }
            });
        }
    }

    /// <summary>the user edited an album name: have the preview of tag and folder changes recomputed</summary>
    private void OnAlbumEdited()
    {
        if (_currentAlbumPlan != null && !_busy && !_closed)
        {
            AlbumPlan plan = _currentAlbumPlan;
            RunInBackground(() => _controller.ReviewAlbums(plan), async failure =>
            {
                if (failure != null && !_closed)
                {
                    await ErrorAsync("Standardize", "The album changes could not be recomputed: " + Describe(failure));
                }
            });
        }
    }

    /// <summary>the work on a worker thread (Java: a SwingWorker), then what to do next on the UI thread</summary>
    private async void RunInBackground(Action work, Action<Exception> afterwards)
    {
        Exception failure = null;
        try
        {
            await Task.Run(work);
        }
        catch (Exception e)
        {
            failure = e;
        }
        afterwards(failure);
    }

    private static string Describe(Exception failure)
    {
        return string.IsNullOrEmpty(failure.Message) || JavaString.IsBlank(failure.Message)
                ? failure.GetType().FullName : failure.Message;
    }

    /// <summary>a long error text would fill the status bar: the dialog has all of it, the bar the beginning</summary>
    private static string Shorten(string text)
    {
        return text.Length <= MaxErrorTextInBar ? text : text.Substring(0, MaxErrorTextInBar) + "…";
    }

    // ------------------------------------------------------------------ the dialogs

    // The real prompts are asynchronous, so the page waits for them without blocking the window; any other prompts
    // (a test's) are asked the way the interface says.

    private Task InfoAsync(string title, string message)
    {
        return TellAsync(true, title, message);
    }

    private Task ErrorAsync(string title, string message)
    {
        return TellAsync(false, title, message);
    }

    private async Task TellAsync(bool info, string title, string message)
    {
        try
        {
            if (_prompts is ContentDialogPrompts dialogs)
            {
                await (info ? dialogs.InfoAsync(_host.Frame(), title, message) : dialogs.ErrorAsync(_host.Frame(), title, message));
            }
            else if (info)
            {
                _prompts.Info(_host.Frame(), title, message);
            }
            else
            {
                _prompts.Error(_host.Frame(), title, message);
            }
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("A dialog could not be shown: " + e);
        }
    }

    // ------------------------------------------------------------------ view model events

    /// <summary>
    /// An event of the process view model, on whatever thread the use case runs. The state is one shared, mutable object,
    /// so everything the UI needs is read from it here and now; only then does the work move to the UI thread.
    /// </summary>
    public void OnViewModelEvent(string property)
    {
        if (_closed)
        {
            return;
        }
        StandardizeState state = _viewModel.State;
        switch (property)
        {
            case StandardizeViewModel.Lyrics:
                {
                    List<string[]> rows = state.LyricsRows;
                    string summary = state.LyricsSummary;
                    StandardizeLyricsOutputData result = state.LyricsResult;
                    if (result != null && !result.IsDryRun)
                    {
                        _lyricsDoneSummary = summary;
                    }
                    RunOnUiThread(() => ShowLyrics(rows, summary, result));
                    break;
                }
            case StandardizeViewModel.ArtistsAnalysis:
                {
                    ArtistPlan plan = state.ArtistPlan;
                    List<SongArtistUpdate> updates = state.ArtistUpdates;
                    string summary = state.ArtistsSummary;
                    int renames = state.ArtistRenameCount;
                    List<SongRename> renameList = state.ArtistRenames;
                    RunOnUiThread(() => ShowArtists(plan, updates, summary, renames, renameList));
                    break;
                }
            case StandardizeViewModel.ArtistsApplied:
                _artistsDoneSummary = state.ArtistsAppliedSummary;
                break;
            case StandardizeViewModel.ArtistsRenamed:
                _renamedDoneSummary = state.ArtistsRenamedSummary;
                break;
            case StandardizeViewModel.AlbumsAnalysis:
                {
                    AlbumPlan plan = state.AlbumPlan;
                    List<SongAlbumUpdate> updates = state.AlbumUpdates;
                    List<AlbumFolderMove> moves = state.AlbumFolderMoves;
                    string summary = state.AlbumsSummary;
                    RunOnUiThread(() => ShowAlbums(plan, updates, moves, summary));
                    break;
                }
            case StandardizeViewModel.AlbumsApplied:
                _albumsDoneSummary = state.AlbumsAppliedSummary;
                break;
            case StandardizeViewModel.AlbumsMoved:
                _movedPaths = state.AlbumMovedPaths;
                _foldersDoneSummary = state.AlbumsMovedSummary;
                break;
            case StandardizeViewModel.Progress:
                {
                    int done = state.ProgressDone;
                    int total = state.ProgressTotal;
                    RunOnUiThread(() => ShowProgress(done, total));
                    break;
                }
            case StandardizeViewModel.Error:
                AddError(state.ErrorMessage);
                break;
            default:
                break;
        }
    }

    private void RunOnUiThread(Action action)
    {
        _dispatcher.TryEnqueue(() => action());
    }

    private void ShowLyrics(List<string[]> rows, string summary, StandardizeLyricsOutputData result)
    {
        if (_closed)
        {
            return;
        }
        var cards = new List<ResultCardItem>();
        foreach (string[] row in ReviewText.OrderRows(rows, result)) // song | what happens | details | deprecated files removed | level
        {
            bool failed = "error" == row[4];
            string status = failed ? "Left untouched" : row[3];
            ResultTone tone = failed ? ResultTone.Critical : status.Length != 0 ? ResultTone.Accent : ResultTone.Neutral;
            cards.Add(new ResultCardItem(row[0], row[1], row[2], status, tone));
        }
        LyricsList.ItemsSource = cards;
        _rowCounts[Lyrics] = cards.Count;
        SetSummary(Lyrics, summary);
        UpdateSkeletons();

        if (result != null && result.IsDryRun)
        {
            _lyricsPreview = result;
            _lyricsHasWork = ReviewText.HasLyricsWork(result);
        }
        else
        {
            _lyricsHasWork = false; // what a real run did is not something to apply again
        }
        UpdateApplyEnabled();
    }

    private void ShowArtists(ArtistPlan plan, List<SongArtistUpdate> updates, string summary, int renames, List<SongRename> renameList)
    {
        if (_closed)
        {
            return;
        }
        if (!ReferenceEquals(plan, _currentPlan)) // a new analysis, not just a refresh after the user edited a name
        {
            _currentPlan = plan;
            var items = new List<ArtistGroupItem>();
            // the ones marked "please check" (red) first: they are the ones to look at
            foreach (ArtistGroup group in ReviewText.CheckFirst(ReviewText.GroupsInScope(plan)))
            {
                ArtistGroup shown = group;
                items.Add(new ArtistGroupItem(group, OnArtistEdited, () => ReviewText.SongsOf(plan, shown)) { CanEdit = !_busy });
            }
            _artistItems = items;
            _rowCounts[Artists] = items.Count;
            ArtistSearchBox.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ApplyArtistSearch();
        }
        _artistUpdateCount = updates.Count;
        _artistUpdates = updates;
        _renameCount = renames;
        _artistRenames = renameList ?? new List<SongRename>();
        SetSummary(Artists, summary);
        RebuildChanges();
    }

    private void ShowAlbums(AlbumPlan plan, List<SongAlbumUpdate> updates, List<AlbumFolderMove> moves, string summary)
    {
        if (_closed)
        {
            return;
        }
        if (!ReferenceEquals(plan, _currentAlbumPlan)) // a new analysis, not just a refresh after the user edited a name
        {
            _currentAlbumPlan = plan;
            var items = new List<AlbumGroupItem>();
            // the ones marked "please check" (red) first: they are the ones to look at
            foreach (AlbumGroup group in ReviewText.CheckFirst(ReviewText.AlbumGroupsInScope(plan)))
            {
                AlbumGroup shown = group;
                items.Add(new AlbumGroupItem(group, OnAlbumEdited, () => ReviewText.SongsOf(plan, shown),
                        typed => AlbumSuggestions.For(plan, shown, typed)) { CanEdit = !_busy });
            }
            _albumItems = items;
            _rowCounts[Albums] = items.Count;
            AlbumSearchBox.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ApplyAlbumSearch();
        }
        // the folder an album goes into follows its name: a name that is another album's puts the songs in that album's folder
        Dictionary<string, string> folderNotes = AlbumFolderNotes.For(plan, moves);
        foreach (AlbumGroupItem item in _albumItems)
        {
            item.FolderNote = folderNotes.TryGetValue(item.Group.Key, out string note) ? note : null;
        }
        _albumUpdates = updates;
        _albumMoves = moves;
        _albumUpdateCount = updates.Count;
        _albumMoveCount = moves.Sum(move => move.AudioPaths.Count);
        SetSummary(Albums, summary);
        RebuildChanges();
    }

    /// <summary>
    /// The list of tag changes: one card per song that changes, with a line for every tag value, from the artists and the
    /// albums of the preview; the headline counts the tag values. The folders are a tree of their own (ShowFolders), made
    /// from the same preview, so an edit of an artist or an album redoes both.
    /// </summary>
    private void RebuildChanges()
    {
        var order = new List<string>();
        var linesOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string> LinesFor(string path)
        {
            if (!linesOf.TryGetValue(path, out List<string> lines))
            {
                lines = new List<string>();
                linesOf[path] = lines;
                order.Add(path);
            }
            return lines;
        }

        int values = 0;
        var tagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool adding = _staged != null && !_addCommitted;
        if (adding)
        {
            foreach (string path in _staged.NewPaths)
            {
                // the songs are not in the library yet; where they will be is up to the album folders: the folder tree says
                LinesFor(path).Add(ReviewText.AddLine(path, !_doAlbums));
            }
        }
        foreach (SongArtistUpdate update in _artistUpdates)
        {
            List<string> lines = LinesFor(update.AudioPath);
            foreach (FieldChange change in update.Changes)
            {
                lines.Add(change.Field + ":  " + Shown(change.Before) + Arrow + Shown(change.After));
                values++;
            }
            tagged.Add(update.AudioPath);
        }
        foreach (SongAlbumUpdate update in _albumUpdates)
        {
            LinesFor(update.AudioPath).Add("ALBUM:  " + Shown(update.Before) + Arrow + Shown(update.Album));
            values++;
            tagged.Add(update.AudioPath);
        }
        var cards = new List<ResultCardItem>();
        foreach (string path in order)
        {
            cards.Add(new ResultCardItem(ReviewText.DisplayName(path), "", string.Join("\n", linesOf[path]), "", ResultTone.Neutral));
        }
        ChangesList.ItemsSource = cards;
        _rowCounts[Changes] = cards.Count;

        var notes = new List<string> { "Double check to make sure" };
        if (adding && _staged.NewPaths.Count != 0)
        {
            notes.Add(ReviewText.AddNote(_staged.NewPaths.Count));
        }
        if (_renameCount != 0)
        {
            notes.Add(ReviewText.RenameNote(_renameCount));
        }
        SetSummary(Changes,
                tagged.Count == 0 ? "No tags will change"
                        : values + " tag values in " + ReviewText.SongCount(tagged.Count) + " will be rewritten",
                string.Join("\n", notes));
        ShowFolders();
        UpdateSkeletons();
        UpdateApplyEnabled();
    }

    /// <summary>
    /// The tree of the folders as they will be, from the album plan with the names the user gave the albums, the moves that
    /// follow from them and the file names the artists give: only the folders that change, with the songs that arrive in them.
    /// </summary>
    private void ShowFolders()
    {
        if (!_doAlbums || _currentAlbumPlan == null)
        {
            return;
        }
        IEnumerable<string> added = _staged != null && !_addCommitted ? _staged.NewPaths : new List<string>();
        FolderPreview preview = FolderPreview.Build(_currentAlbumPlan, _albumMoves, added, _artistRenames, Directory.Exists);

        FolderTree.RootNodes.Clear();
        FolderCard.Visibility = preview.HasChanges ? Visibility.Visible : Visibility.Collapsed;
        if (preview.HasChanges)
        {
            FolderTree.RootNodes.Add(ToTreeNode(preview.Root, preview.Root.TotalCount <= MaxRowsOpened, true));
        }
        _rowCounts[Folders] = preview.Root.TotalCount;
        SetSummary(Folders, ReviewText.FoldersHeadline(preview), ReviewText.FoldersDetail(preview));
    }

    /// <summary>a node of the tree control for a row of the preview, with the rows under it (the library folder is always open)</summary>
    private static TreeViewNode ToTreeNode(FolderNode row, bool openAll, bool isLibrary)
    {
        var node = new TreeViewNode { Content = row };
        foreach (FolderNode child in row.Children)
        {
            node.Children.Add(ToTreeNode(child, openAll, false));
        }
        node.IsExpanded = openAll || isLibrary;
        return node;
    }

    // ------------------------------------------------------------------ the artist search

    private void OnArtistSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyArtistSearch();
    }

    private void OnArtistSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && ArtistSearchBox.Text.Length > 0)
        {
            ArtistSearchBox.Text = "";
            e.Handled = true;
        }
    }

    /// <summary>shows the artist cards that match the search box (all of them when it is empty)</summary>
    private void ApplyArtistSearch()
    {
        string query = ArtistSearchBox.Text;
        List<ArtistGroupItem> shown = JavaString.IsBlank(query)
                ? _artistItems
                : _artistItems.Where(item => ReviewText.MatchesSearch(item.Group, query)).ToList();
        ArtistList.ItemsSource = shown;
        bool nothing = shown.Count == 0 && _artistItems.Count != 0;
        ArtistSearchNote.Text = nothing ? "No artist matches \"" + JavaString.Trim(query) + "\"" : "";
        ArtistSearchNote.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ the album search

    private void OnAlbumSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyAlbumSearch();
    }

    private void OnAlbumSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && AlbumSearchBox.Text.Length > 0)
        {
            AlbumSearchBox.Text = "";
            e.Handled = true;
        }
    }

    /// <summary>shows the album cards that match the search box (all of them when it is empty)</summary>
    private void ApplyAlbumSearch()
    {
        string query = AlbumSearchBox.Text;
        List<AlbumGroupItem> shown = JavaString.IsBlank(query)
                ? _albumItems
                : _albumItems.Where(item => ReviewText.MatchesSearch(item.Group, query)).ToList();
        AlbumList.ItemsSource = shown;
        bool nothing = shown.Count == 0 && _albumItems.Count != 0;
        AlbumSearchNote.Text = nothing ? "No album matches \"" + JavaString.Trim(query) + "\"" : "";
        AlbumSearchNote.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Shown(string value)
    {
        return value == null || value.Length == 0 ? "(empty)" : value;
    }

    /// <summary>
    /// The bar keeps the known progress (the words about what is being asked are not shown: the status text only says that
    /// something is thinking).
    /// </summary>
    private void ShowProgress(int done, int total)
    {
        if (_closed || !_busy)
        {
            return;
        }
        if (total > 0)
        {
            RunProgress.IsIndeterminate = false;
            RunProgress.Maximum = total;
            RunProgress.Value = done;
        }
        else
        {
            RunProgress.IsIndeterminate = true;
        }
    }

    // ------------------------------------------------------------------ small helpers

    /// <summary>
    /// While the preview runs the busy panel shows the animated status text above the progress bar and nothing else; while
    /// the changes are written it shows only the progress bar. The InfoBar is shown only when a run is finished, with its
    /// title and message (the result or the error).
    /// </summary>
    private void SetBusy(bool isBusy, string title = "", string message = "", InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        _busy = isBusy;
        BackButton.IsEnabled = !isBusy;
        foreach (ArtistGroupItem item in _artistItems)
        {
            item.CanEdit = !isBusy; // names cannot be edited while a run is going: it works from the plan these cards change
        }
        foreach (AlbumGroupItem item in _albumItems)
        {
            item.CanEdit = !isBusy;
        }
        BusyPanel.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Visibility = isBusy && _previewing ? Visibility.Visible : Visibility.Collapsed;
        RunProgress.IsIndeterminate = true; // ShowProgress makes it determinate when the total is known
        RunProgress.Value = 0;
        StatusBar.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        StatusBar.Severity = severity;
        StatusBar.Title = isBusy ? "" : title;
        StatusBar.Message = isBusy ? "" : message;
        UpdateSkeletons();
        UpdateApplyEnabled();
        _host.SetBusy(isBusy);
        MusicManagerFrame.Current?.SetBackEnabled(!isBusy);
    }

    /// <summary>
    /// While the preview is still being worked out, a section that has no rows yet shows redacted blocks where they will be;
    /// they go as soon as rows arrive and when the run ends.
    /// </summary>
    private void UpdateSkeletons()
    {
        for (int section = 0; section < _skeletons.Length; section++)
        {
            bool used = section == Lyrics ? _doLyrics : section == Artists ? _doArtists : section == Albums || section == Folders ? _doAlbums
                    : _doArtists || _doAlbums;
            _skeletons[section].Visibility = _busy && _previewing && used && _rowCounts[section] == 0
                    ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateApplyEnabled()
    {
        ApplyButton.IsEnabled = !_busy && !_applied && !_previewFailed && HasWork();
    }

    /// <summary>something to write: lyrics, tags, or files named differently from their tags</summary>
    private bool HasWork()
    {
        return (_staged != null && !_addCommitted)
                || (_doLyrics && _lyricsHasWork)
                || (_doArtists && _currentPlan != null && (_artistUpdateCount > 0 || _renameCount > 0))
                || (_doAlbums && _currentAlbumPlan != null && (_albumUpdateCount > 0 || _albumMoveCount > 0));
    }

    // the error messages arrive on worker threads and are read on the UI thread when a run is over
    private void AddError(string message)
    {
        lock (_runErrors)
        {
            _runErrors.Add(message);
        }
    }

    private void ClearErrors()
    {
        lock (_runErrors)
        {
            _runErrors.Clear();
        }
    }

    private bool HasErrors()
    {
        lock (_runErrors)
        {
            return _runErrors.Count != 0;
        }
    }

    private List<string> ErrorsNow()
    {
        lock (_runErrors)
        {
            return new List<string>(_runErrors);
        }
    }

    // ------------------------------------------------------------------ for smoke tests

    /// <summary>true once the page has been left (Back, or after a run that wrote something)</summary>
    public bool IsClosed => _closed;

    public bool IsBusy => _busy;

    public bool IsApplyEnabled => ApplyButton.IsEnabled;

    /// <summary>the title of the status bar (what the page says it is doing)</summary>
    public string StatusMessage => StatusBar.Title;

    public bool LyricsPreviewHasWork => _lyricsHasWork;

    public int ArtistUpdateCount => _artistUpdateCount;

    /// <summary>how many songs have files that will be renamed to "Artist - Title"</summary>
    public int RenameCount => _renameCount;

    /// <summary>how many songs get another album tag</summary>
    public int AlbumUpdateCount => _albumUpdateCount;

    /// <summary>how many songs will be put in another folder</summary>
    public int AlbumMoveCount => _albumMoveCount;

    /// <summary>how often the lyrics action list of the last preview contains this action</summary>
    public int LyricsPreviewCount(LyricsAction action)
    {
        return _lyricsPreview == null ? 0 : _lyricsPreview.Count(action);
    }
}
