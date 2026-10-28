using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.AddSongs;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.StandardizeLyrics;
using MusicManager.View.Library;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using AppContext = MusicManager.View.Library.AppContext; // System.AppContext would otherwise be ambiguous
using Chip = MusicManager.View.Library.LibraryFilter.Chip;

namespace MusicManager.App.View.Library;

/// <summary>
/// The second page: the songs of the opened folder, to process all at once, a selection, or one by one.
///
/// It reads the folder (LibraryController), then checks the lyrics of every song once in the background (a dry run of
/// the lyrics use case, which only counts) to give each card its status line. The Options only change how those results
/// are shown, so changing them never rescans. Search and the filter chips narrow the grid; selecting songs and pressing
/// Process starts the ProcessFlow, which always shows a review before anything is written.
///
/// The window shows it in its Frame and keeps the instance (NavigationCacheMode.Required), so coming back from the review
/// shows the list as it was. e.Parameter of the navigation is the folder to read; Back from the review is not a new folder.
/// </summary>
public sealed partial class LibraryPage : Page
{
    /// <summary>the page that is in use; an older instance (should the Frame ever make a second one) ignores the view models</summary>
    private static LibraryPage s_current;

    private readonly AppContext _context;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui;

    // what the page shows
    private AppSettings _settings;
    private string _folder = "";
    private List<Music> _songs = new List<Music>();
    private Dictionary<string, SongLyricsResult> _lyricsResults = new Dictionary<string, SongLyricsResult>(); // the kept result of the scan, by audio path
    private Dictionary<string, SongStatus> _statuses = new Dictionary<string, SongStatus>(); // lyricsResults shown through the current Options
    private bool _statusesKnown; // true once a scan delivered results, so the chips can show counts
    private Chip _chip = Chip.All;

    // what is going on; Process waits for all of it. A new folder bumps the generation, so late answers are ignored
    private int _generation;
    private bool _reading;
    private int _refreshing;
    private int _scanning;
    private bool _hostBusy;
    private bool _adding; // songs are being staged for the library (copied to the folder they wait in)
    private int _addDone;
    private int _addTotal;
    private int _readDone;
    private int _readTotal;
    private int _scanDone;
    private int _scanTotal;
    private string _problemText = ""; // a failed check, shown in an error bar until the next job starts
    private SongDialog _openDialog;
    private StandardizeOptions _previewOptions; // the Options the preview of the open song window was made with
    private bool _optionsOpen; // the Options window of the page is up (or about to be)

    private volatile ScanJob _runningScan; // read by the view model listener, which runs on the scanning thread

    private readonly SongPreviewLoader _previewLoader; // makes the preview of the song window; null = there is none

    // one at a time: the view models hold one shared state each
    private readonly SerialExecutor _libraryJobs = new SerialExecutor("library-reader");
    private readonly SerialExecutor _scanJobs = new SerialExecutor("lyrics-scan");

    private readonly Dictionary<Chip, SelectorBarItem> _chipItems = new Dictionary<Chip, SelectorBarItem>();
    private bool _updatingChips; // the chip bar is being changed by the page itself, not by the user

    public LibraryPage()
    {
        _context = MusicManagerFrame.Current.Context;
        _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _settings = _context.Settings.Load();
        _previewLoader = _context.PreviewPipelines == null
                ? null
                : new SongPreviewLoader(_context.PreviewPipelines, Main.UiDispatcher);
        s_current = this;

        InitializeComponent();
        BuildChips();
        WireActions();
        WireViewModels();
        RefreshView();
    }

    // ------------------------------------------------------------------ layout

    private void BuildChips()
    {
        _updatingChips = true; // adding the chips is not the user choosing one
        try
        {
            foreach (Chip each in Enum.GetValues<Chip>())
            {
                var item = new SelectorBarItem { Text = LibraryFilter.ChipLabel(each, null), Tag = each };
                _chipItems[each] = item;
                ChipBar.Items.Add(item);
            }
        }
        finally
        {
            _updatingChips = false;
        }
        _chipItems[Chip.Problems].Visibility = Visibility.Collapsed; // only when there are problems
        SetChipChecked(Chip.All);
    }

    private void WireActions()
    {
        Songs.AddSelectionListener(RefreshSelection);
        Songs.AddOpenListener(OpenSong);
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        Songs.SelectAllVisible();
    }

    private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
    {
        Songs.ClearSelection();
    }

    private void OptionsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenOptions();
    }

    private void ProcessButton_Click(object sender, RoutedEventArgs e)
    {
        StartProcess(Targets());
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+F from anywhere on the page</summary>
    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void ProblemBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _problemText = ""; // the user has read it
    }

    // ------------------------------------------------------------------ navigation

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // a new folder starts reading; coming back from the review (Back) keeps everything as it is
        if (e.NavigationMode == NavigationMode.New && e.Parameter is string path)
        {
            Open(path);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Back to the start page: whatever is still running belongs to a folder nobody looks at (the review stacks on top, Back returns)
        if (e.NavigationMode == NavigationMode.Back)
        {
            Leave();
        }
    }

    // ------------------------------------------------------------------ the window talks to the page

    /// <summary>a folder was chosen: show the "reading songs" state and read it in the background</summary>
    public void Open(string path)
    {
        _generation++;
        _folder = path;
        _songs = new List<Music>();
        _lyricsResults = new Dictionary<string, SongLyricsResult>();
        _statuses = new Dictionary<string, SongStatus>();
        _statusesKnown = false;
        _reading = true;
        _refreshing = 0;
        _scanning = 0;
        _readDone = 0;
        _readTotal = 0;
        _problemText = "";
        _chip = Chip.All;
        SetChipChecked(Chip.All);
        SearchBox.Text = "";
        Songs.ClearSelection();
        Songs.SetSongs(new List<Music>());
        Songs.SetStatuses(_statuses);
        FolderLabel.Text = MusicManagerFrame.FolderName(path);
        ToolTipService.SetToolTip(FolderLabel, path);
        RefreshView();

        string readFolder = path;
        int forGeneration = _generation;
        RunJob(_libraryJobs, () => _context.LibraryController.Open(readFolder),
                failure => FailIfThrown(failure, forGeneration, "Failed to read the music folder"));
        // the lyrics are checked while the songs are read, not after: the two do not depend on each other (the check works
        // from the folder), and each uses several cores, so the page is ready when the slower of the two is
        StartScan(null);
    }

    /// <summary>the user left the page: whatever is still running belongs to a folder nobody looks at</summary>
    public void Leave()
    {
        _generation++;
        _reading = false;
        _refreshing = 0;
        _scanning = 0;
        _adding = false;
    }

    /// <summary>reads the folder again, as when it was opened, but keeps the filter and the search the user had</summary>
    private void Reload()
    {
        Chip chip = _chip;
        string search = SearchBox.Text;
        Open(_folder);
        _chip = chip;
        SetChipChecked(chip);
        SearchBox.Text = search;
        ApplyFilter();
    }

    /// <summary>these songs were changed by a run: read their tags again and check their lyrics again</summary>
    public void RefreshSongs(IEnumerable<string> audioPaths)
    {
        if (_folder.Length == 0 || audioPaths == null)
        {
            return;
        }
        var paths = new HashSet<string>(audioPaths);
        if (paths.Count == 0)
        {
            return;
        }
        var known = new HashSet<string>(_songs.Select(song => song.Path), StringComparer.OrdinalIgnoreCase);
        if (paths.Any(path => !File.Exists(path) || !known.Contains(path)))
        {
            // a song's files were renamed (to "Artist - Title" after its tags) or songs were added: the list holds paths that
            // are gone, or lacks the new ones, so the folder is read again
            Reload();
            return;
        }
        string refreshFolder = _folder;
        int forGeneration = _generation;
        _refreshing++;
        RefreshView();
        RunJob(_libraryJobs, () => _context.LibraryController.Refresh(refreshFolder, paths), failure =>
        {
            if (forGeneration == _generation)
            {
                _refreshing = Math.Max(0, _refreshing - 1);
                RefreshView();
            }
            FailIfThrown(failure, forGeneration, "Failed to read the songs again");
        });
        StartScan(paths);
    }

    /// <summary>Convert mode only: the result of one song</summary>
    public void ShowConvertResult(string audioPath, bool success)
    {
        Songs.SetStatus(audioPath, new SongStatus(SongStatus.Category.Unknown,
                success ? SongStatus.Tone.Good : SongStatus.Tone.Bad, success ? "Converted" : "Failed",
                new List<string>(), 0, 0, 0, success ? "Converted" : "Failed"));
    }

    /// <summary>a run (review, apply, convert) is going on elsewhere: no new one may start</summary>
    public void SetHostBusy(bool busy)
    {
        _hostBusy = busy;
        RefreshView();
    }

    /// <summary>the status of a song as the grid shows it (unknown until the check has seen it)</summary>
    public SongStatus StatusOf(Music song)
    {
        return _statuses.TryGetValue(song.Path, out SongStatus status) ? status : SongStatus.Unknown;
    }

    public AppSettings CurrentSettings()
    {
        return _settings;
    }

    // ------------------------------------------------------------------ reading songs

    private void WireViewModels()
    {
        _context.LibraryViewModel.AddPropertyChangeListener(evt =>
        {
            if (!ReferenceEquals(s_current, this))
            {
                return;
            }
            // the state is shared and the presenter runs on a worker thread: read what is needed here, right now
            LibraryState state = _context.LibraryViewModel.State;
            string name = evt.PropertyName;
            if (name == LibraryViewModel.Songs)
            {
                string path = state.Path;
                List<Music> list = state.Songs;
                HashSet<string> refreshed = state.RefreshedAudioPaths;
                Post(() => OnSongs(path, list, refreshed));
            }
            else if (name == LibraryViewModel.Progress)
            {
                int done = state.ProgressDone;
                int total = state.ProgressTotal;
                Post(() => OnReadProgress(done, total));
            }
            else if (name == LibraryViewModel.Error)
            {
                string message = state.ErrorMessage;
                Post(() => OnReadError(message));
            }
        });

        _context.AddSongsViewModel?.AddPropertyChangeListener(evt =>
        {
            if (!ReferenceEquals(s_current, this))
            {
                return;
            }
            AddSongsState state = _context.AddSongsViewModel.State;
            string name = evt.PropertyName;
            if (name == AddSongsViewModel.Added)
            {
                List<string> paths = state.LibraryPaths;
                List<string> newPaths = state.NewPaths;
                List<string> lyricsOnly = state.LyricsAddedTo;
                string summary = state.Summary;
                int problems = state.ProblemCount;
                Post(() => OnAdded(paths, newPaths, lyricsOnly, summary, problems));
            }
            else if (name == AddSongsViewModel.Progress)
            {
                int done = state.ProgressDone;
                int total = state.ProgressTotal;
                Post(() =>
                {
                    if (_adding)
                    {
                        _addDone = done;
                        _addTotal = total;
                        RefreshStatusBar();
                    }
                });
            }
            else if (name == AddSongsViewModel.Error)
            {
                string message = state.ErrorMessage;
                Post(() =>
                {
                    _adding = false;
                    RefreshView();
                    ShowError(message);
                });
            }
        });

        _context.ScanViewModel.AddPropertyChangeListener(evt =>
        {
            ScanJob job = _runningScan;
            if (job == null || !ReferenceEquals(s_current, this))
            {
                return;
            }
            StandardizeState state = _context.ScanViewModel.State;
            string name = evt.PropertyName;
            if (name == StandardizeViewModel.Lyrics)
            {
                StandardizeLyricsOutputData output = state.LyricsResult;
                List<SongLyricsResult> results = output == null
                        ? new List<SongLyricsResult>()
                        : new List<SongLyricsResult>(output.Results);
                Post(() => OnScanResults(job, results));
            }
            else if (name == StandardizeViewModel.Progress)
            {
                int done = state.ProgressDone;
                int total = state.ProgressTotal;
                Post(() => OnScanProgress(job, done, total));
            }
            else if (name == StandardizeViewModel.Error)
            {
                string message = state.ErrorMessage;
                Post(() => OnScanError(job, message));
            }
        });
    }

    private void OnSongs(string path, List<Music> list, IEnumerable<string> refreshedPaths)
    {
        if (path != _folder)
        {
            return; // the answer for a folder that was left
        }
        if (refreshedPaths == null)
        {
            if (!_reading)
            {
                return;
            }
            _reading = false;
            _songs = new List<Music>(list);
            Songs.SetSongs(new List<Music>(_songs)); // the grid keeps its own list: this one is changed by refreshes
            RefreshView();
            if (_statusesKnown)
            {
                // the check was started with the reading and finished first: its results go onto the songs now
                RemapStatuses();
            }
            else if (_scanning == 0 && _songs.Count > 0)
            {
                StartScan(null); // (the check is still running when the reading ends: its results are mapped when it is done)
            }
        }
        else
        {
            var fresh = new Dictionary<string, Music>();
            foreach (Music song in list)
            {
                fresh[song.Path] = song;
            }
            for (int i = 0; i < _songs.Count; i++)
            {
                if (fresh.TryGetValue(_songs[i].Path, out Music replacement))
                {
                    _songs[i] = replacement;
                }
            }
            Songs.MergeSongs(list);
            RefreshView();
        }
    }

    private void OnReadProgress(int done, int total)
    {
        if (_reading)
        {
            _readDone = done;
            _readTotal = total;
            RefreshStatusBar();
        }
    }

    private void OnReadError(string message)
    {
        if (_reading)
        {
            _reading = false;
            _generation++;
            MusicManagerFrame.Current.OpenFailed(message);
        }
        else if (_folder.Length > 0)
        {
            _problemText = message;
            RefreshStatusBar();
        }
    }

    // ------------------------------------------------------------------ checking lyrics

    /// <summary>what one background check is for: the whole folder (scope null) or only some songs, for the folder it started in</summary>
    private sealed class ScanJob
    {
        public readonly int Generation;
        public readonly HashSet<string> Scope;

        public ScanJob(int generation, HashSet<string> scope)
        {
            Generation = generation;
            Scope = scope;
        }
    }

    private void StartScan(HashSet<string> scope)
    {
        var job = new ScanJob(_generation, scope);
        string scanFolder = _folder;
        string model = _settings.StandardizeOptions.Model;
        _scanning++;
        _scanDone = 0;
        _scanTotal = 0;
        _problemText = "";
        RefreshView();
        RunJob(_scanJobs, () =>
        {
            _runningScan = job;
            // a dry run with romaji counted but not made: it never writes and never asks Claude anything
            _context.ScanController.StandardizeLyrics(scanFolder, true, false, true, "", model, job.Scope);
        }, failure =>
        {
            if (job.Generation == _generation)
            {
                _scanning = Math.Max(0, _scanning - 1);
                RefreshView();
            }
            FailIfThrown(failure, job.Generation, "Failed to check the lyrics");
        });
    }

    private void OnScanResults(ScanJob job, List<SongLyricsResult> results)
    {
        if (job.Generation != _generation)
        {
            return;
        }
        if (job.Scope == null)
        {
            _lyricsResults = new Dictionary<string, SongLyricsResult>();
        }
        foreach (SongLyricsResult result in results)
        {
            _lyricsResults[result.AudioPath] = result;
        }
        _statusesKnown = true;
        RemapStatuses();
    }

    private void OnScanProgress(ScanJob job, int done, int total)
    {
        if (job.Generation == _generation && _scanning > 0)
        {
            _scanDone = done;
            _scanTotal = total;
            RefreshStatusBar();
        }
    }

    private void OnScanError(ScanJob job, string message)
    {
        if (job.Generation == _generation)
        {
            _problemText = message;
            RefreshStatusBar();
        }
    }

    /// <summary>shows the kept results through the current Options; the Options window calls this, so nothing is rescanned</summary>
    private void RemapStatuses()
    {
        StandardizeOptions options = _settings.StandardizeOptions;
        var mapped = new Dictionary<string, SongStatus>();
        foreach (Music song in _songs)
        {
            if (_lyricsResults.TryGetValue(song.Path, out SongLyricsResult result))
            {
                mapped[song.Path] = LibraryStatusMapper.Map(result, options);
            }
        }
        _statuses = mapped;
        Songs.SetStatuses(_statuses);
        RefreshView();
        if (_openDialog != null)
        {
            _openDialog.SetStatus(_statuses.TryGetValue(_openDialog.Music.Path, out SongStatus status) ? status : null);
        }
    }

    // ------------------------------------------------------------------ search, chips, selection

    private void ChipBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_updatingChips && sender.SelectedItem?.Tag is Chip chip)
        {
            SelectChip(chip);
        }
    }

    private void SelectChip(Chip selected)
    {
        _chip = selected;
        ApplyFilter();
    }

    /// <summary>the chips are a group: exactly one of them is on</summary>
    private void SetChipChecked(Chip selected)
    {
        _updatingChips = true;
        try
        {
            ChipBar.SelectedItem = _chipItems[selected];
        }
        finally
        {
            _updatingChips = false;
        }
    }

    /// <summary>gives the grid the filter of the search text and chip, then updates everything that depends on what is shown</summary>
    private void ApplyFilter()
    {
        string search = SearchBox.Text;
        bool noFilter = _chip == Chip.All && JavaString.Trim(search).Length == 0;
        Func<Music, bool> filter = noFilter ? null : LibraryFilter.Predicate(search, _chip, _statuses);
        Songs.SetFilter(filter);
        RefreshCounts();
        RefreshProcess();
        RefreshCenter();
    }

    private void RefreshSelection()
    {
        RefreshCaption();
        RefreshProcess();
    }

    /// <summary>everything that follows from the songs, statuses, options or running jobs</summary>
    private void RefreshView()
    {
        OptionsCaption.Text = LibraryFilter.OptionsCaption(_settings.StandardizeOptions,
                _settings.DefaultTargetFormats, AppFeatures.LegacyConvertLrc);
        ApplyFilter();
        RefreshStatusBar();
        if (_openDialog != null)
        {
            _openDialog.SetProcessEnabled(CanProcessSong());
        }
    }

    private void RefreshCounts()
    {
        Dictionary<Chip, int> counts = LibraryFilter.Counts(_songs, _statuses, SearchBox.Text);
        bool problems = _statusesKnown && LibraryFilter.HasProblems(_songs, _statuses);
        if (!problems && _chip == Chip.Problems)
        {
            _chip = Chip.All;
            SetChipChecked(Chip.All);
        }
        foreach (Chip each in Enum.GetValues<Chip>())
        {
            SelectorBarItem item = _chipItems[each];
            bool known = each == Chip.All ? !_reading : _statusesKnown;
            item.Text = LibraryFilter.ChipLabel(each, known ? counts[each] : (int?)null);
            item.IsEnabled = !_reading;
        }
        _chipItems[Chip.Problems].Visibility = problems ? Visibility.Visible : Visibility.Collapsed;
        RefreshCaption();
    }

    private void RefreshCaption()
    {
        if (_reading)
        {
            CountLabel.Text = " "; // the middle of the page and the bar above already say it
        }
        else
        {
            CountLabel.Text = LibraryFilter.CountCaption(Songs.VisibleCount(), _songs.Count, Songs.SelectedCount());
        }
        SelectAllButton.IsEnabled = !_reading && Songs.VisibleCount() > 0;
        SelectNoneButton.IsEnabled = Songs.SelectedCount() > 0;
    }

    private void RefreshCenter()
    {
        string message = null;
        if (_reading)
        {
            // no cards yet: the thin bar above says it is working, the area stays empty
            Songs.Visibility = Visibility.Collapsed;
            MessagePanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (_songs.Count == 0)
        {
            message = "No songs found in this folder.";
        }
        else if (Songs.VisibleCount() == 0)
        {
            message = JavaString.Trim(SearchBox.Text).Length == 0 ? "No songs in this group." : "No songs match your search.";
        }
        if (message == null)
        {
            Songs.Visibility = Visibility.Visible;
            MessagePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            MessageLabel.Text = message;
            MessagePanel.Visibility = Visibility.Visible;
            Songs.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshProcess()
    {
        int selected = Songs.SelectedCount();
        ProcessLabel.Text = LibraryFilter.ProcessText(selected, Songs.VisibleCount(), _songs.Count);
        AutomationProperties.SetName(ProcessButton, ProcessLabel.Text); // the button holds an icon and a text block: name it for screen readers
        ProcessButton.IsEnabled = CanProcessSong() && (selected > 0 || Songs.VisibleCount() > 0);
        // a run elsewhere has a sentence of its own (the tool tip); the bar above the songs says that the page itself is busy
        ToolTipService.SetToolTip(ProcessButton, _hostBusy ? "A run is going" : null);
        AddButton.IsEnabled = CanAddSongs();
    }

    private bool CanProcessSong()
    {
        return !_reading && _refreshing == 0 && _scanning == 0 && !_hostBusy && !_adding && _songs.Count > 0;
    }

    /// <summary>songs can be added once the library has been read, and while nothing else changes it</summary>
    private bool CanAddSongs()
    {
        return _context.AddSongsController != null && _folder.Length > 0 && !_reading && !_adding && !_hostBusy
                && _refreshing == 0;
    }

    /// <summary>
    /// The bars above the songs: while the page is busy a thin progress bar (it fills when the count is known, and says no
    /// words about it), and a failed check in an error bar.
    /// </summary>
    private void RefreshStatusBar()
    {
        string name; // for screen readers only: nothing on screen says what the page is doing
        int done;
        int total;
        if (_reading)
        {
            name = "Reading songs";
            done = _readDone;
            total = _readTotal;
        }
        else if (_adding)
        {
            name = "Adding songs";
            done = _addDone;
            total = _addTotal;
        }
        else if (_scanning > 0)
        {
            name = "Checking lyrics";
            done = _scanDone;
            total = _scanTotal;
        }
        else if (_refreshing > 0)
        {
            name = "Updating songs";
            done = 0;
            total = 0;
        }
        else if (_hostBusy)
        {
            name = "Working";
            done = 0;
            total = 0;
        }
        else
        {
            name = null;
            done = 0;
            total = 0;
        }

        if (_problemText.Length == 0)
        {
            ProblemBar.IsOpen = false;
        }
        else
        {
            ProblemBar.Title = "Problem";
            ProblemBar.Message = _problemText;
            ProblemBar.IsOpen = true;
        }

        // the thin bar runs only while the page is busy; it says no words (the name is for screen readers only)
        bool busy = name != null;
        WorkProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            return;
        }
        AutomationProperties.SetName(WorkProgress, name);
        if (total > 0)
        {
            WorkProgress.IsIndeterminate = false;
            WorkProgress.Maximum = total;
            WorkProgress.Value = Math.Min(done, total);
        }
        else
        {
            WorkProgress.IsIndeterminate = true;
        }
    }

    // ------------------------------------------------------------------ adding songs

    private async void AddSongsItem_Click(object sender, RoutedEventArgs e)
    {
        await PickAndAdd(false);
    }

    private async void AddFolderItem_Click(object sender, RoutedEventArgs e)
    {
        await PickAndAdd(true);
    }

    /// <summary>asks for the songs or the folder to add, stages them for the library and starts the run that standardizes them</summary>
    private async Task PickAndAdd(bool folder)
    {
        if (!CanAddSongs())
        {
            return;
        }
        try
        {
            var sources = new List<string>();
            if (folder)
            {
                var picker = new FolderPicker();
                picker.SuggestedStartLocation = PickerLocationId.Downloads;
                picker.CommitButtonText = "Add this folder";
                picker.FileTypeFilter.Add("*");
                InitializeWithWindow.Initialize(picker, MusicManagerFrame.Current.Handle);
                Windows.Storage.StorageFolder chosen = await picker.PickSingleFolderAsync();
                if (chosen != null)
                {
                    sources.Add(chosen.Path);
                }
            }
            else
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.Downloads;
                picker.CommitButtonText = "Add songs";
                foreach (string extension in MusicManager.DataAccess.DataAccess.AudioExtensions)
                {
                    picker.FileTypeFilter.Add(extension);
                }
                InitializeWithWindow.Initialize(picker, MusicManagerFrame.Current.Handle);
                IReadOnlyList<Windows.Storage.StorageFile> chosen = await picker.PickMultipleFilesAsync();
                if (chosen != null)
                {
                    sources.AddRange(chosen.Select(file => file.Path));
                }
            }
            if (sources.Count != 0)
            {
                StartAdd(sources);
            }
        }
        catch (Exception e)
        {
            ShowError("Could not add the songs: " + e.Message);
        }
    }

    private void StartAdd(List<string> sources)
    {
        if (!CanAddSongs())
        {
            return;
        }
        _adding = true;
        _addDone = 0;
        _addTotal = 0;
        _problemText = "";
        RefreshView();
        string library = _folder;
        string preferred = _settings.StandardizeOptions.PreferredFormat;
        Task.Run(() => _context.AddSongsController.Add(library, sources, preferred)).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Exception failure = task.Exception.InnerException ?? task.Exception;
                Post(() =>
                {
                    _adding = false;
                    RefreshView();
                    ShowError("Could not add the songs: " + failure.Message);
                });
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// the songs are staged, not in the library: the run that standardizes them starts at once, with its review as for any
    /// Process. The review shows the library as it will be with them, and they are copied in only when the user applies it;
    /// when the user goes back they are dropped.
    /// </summary>
    private async void OnAdded(List<string> paths, List<string> newPaths, List<string> lyricsOnly, string summary, int problems)
    {
        _adding = false;
        if (paths.Count == 0 || problems != 0)
        {
            // nothing could be added, or some songs could not: say so now, the review does not
            await MusicManagerFrame.Current.ShowMessageAsync("Add songs", summary.Length == 0 ? "Nothing was added." : summary);
        }
        RefreshView();
        if (paths.Count == 0)
        {
            return;
        }
        try
        {
            // the review works on the library folder as one path: the same spelling the songs were staged for
            string library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_folder));
            MusicManagerFrame.Current.Flow.StartAdded(library, paths, newPaths, lyricsOnly, _settings.Copy());
        }
        catch (Exception e)
        {
            ShowError("Could not start: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ process, options, one song

    /// <summary>the selected songs; with nothing selected, the songs the search and chip show</summary>
    private List<Music> Targets()
    {
        HashSet<string> selected = Songs.SelectedPaths();
        if (selected.Count == 0)
        {
            return new List<Music>(Songs.VisibleSongs());
        }
        var chosen = new List<Music>();
        foreach (Music song in _songs)
        {
            if (selected.Contains(song.Path))
            {
                chosen.Add(song);
            }
        }
        return chosen;
    }

    private void StartProcess(List<Music> chosen)
    {
        if (chosen.Count == 0 || !CanProcessSong())
        {
            return;
        }
        try
        {
            MusicManagerFrame.Current.Flow.Start(_folder, chosen, _settings.Copy());
        }
        catch (Exception e)
        {
            ShowError("Could not start: " + e.Message);
        }
    }

    /// <summary>the Options button of the page</summary>
    private async void OpenOptions()
    {
        if (_optionsOpen)
        {
            return;
        }
        try
        {
            await ShowOptions();
        }
        catch (Exception e)
        {
            ShowError("Could not open the Options: " + e.Message);
        }
    }

    /// <summary>shows the Options window; the task ends when it is closed</summary>
    private async Task ShowOptions()
    {
        _optionsOpen = true;
        try
        {
            // looking for a key in the Windows environment can start a process or two: not on the UI thread
            bool apiKeyAvailable = await Task.Run(() =>
            {
                try
                {
                    return _context.ScanController.IsApiKeyAvailable;
                }
                catch (Exception)
                {
                    return false;
                }
            });
            await OptionsDialog.OpenAsync(MusicManagerFrame.Current.XamlRoot, _settings.Copy(), apiKeyAvailable, ApplyOptions);
        }
        finally
        {
            _optionsOpen = false;
        }
    }

    /// <summary>the user saved the Options: keep them (not the typed key, that only lives in memory) and show the songs again</summary>
    public void ApplyOptions(AppSettings saved)
    {
        AppSettings stored = _context.Settings.Load(); // fresh, so other settings (the last folder) are kept
        stored.StandardizeOptions = saved.StandardizeOptions;
        if (saved.DefaultTargetFormats != null)
        {
            stored.DefaultTargetFormats = saved.DefaultTargetFormats;
        }
        _context.Settings.Save(stored);
        _settings = stored; // still holds the typed key, which the file does not
        RemapStatuses();
        if (_openDialog != null && (_previewOptions == null
                || !SongPreviewLoader.SameInputs(_previewOptions, _settings.StandardizeOptions)))
        {
            _openDialog.SetPreviewSource(PreviewSourceFor(_openDialog.Music)); // the song window opened these Options
        }
    }

    /// <summary>
    /// The song window. It is a ContentDialog, and only one can be open at a time: its Options button closes it, the
    /// Options window is shown, and then this same dialog (with its preview still running) comes back.
    /// </summary>
    private async void OpenSong(Music song)
    {
        if (_openDialog != null)
        {
            return;
        }
        var dialog = new SongDialog(song, _statuses.TryGetValue(song.Path, out SongStatus status) ? status : null);
        dialog.SetProcessEnabled(CanProcessSong());
        dialog.SetPreviewSource(PreviewSourceFor(song));
        _openDialog = dialog;
        bool process = false;
        try
        {
            while (true)
            {
                ContentDialogResult result = await MusicManagerFrame.Current.ShowDialogAsync(dialog); // modal: ends when it is closed
                if (result == ContentDialogResult.Secondary && !_optionsOpen)
                {
                    await ShowOptions();
                    continue;
                }
                process = result == ContentDialogResult.Primary;
                break;
            }
        }
        catch (Exception e)
        {
            ShowError("Could not show the song: " + e.Message);
        }
        finally
        {
            _openDialog = null;
            _previewOptions = null;
            dialog.Dispose();
        }
        if (process)
        {
            StartProcess(new List<Music> { song }); // after the window has closed
        }
    }

    /// <summary>
    /// What the song window previews: this song with the Options as they are now (a copy, with the typed key as it is
    /// held in memory), in the folder that is open. Null when the app has no preview.
    /// </summary>
    private SongDialog.PreviewSource PreviewSourceFor(Music song)
    {
        if (_previewLoader == null)
        {
            _previewOptions = null;
            return null;
        }
        StandardizeOptions options = _settings.StandardizeOptions.Copy();
        string previewFolder = _folder;
        _previewOptions = options;
        return listener => _previewLoader.Load(previewFolder, song, options, listener);
    }

    // ------------------------------------------------------------------ small helpers

    /// <summary>a background job that threw (not the use cases' own error events): tell the user instead of failing silently</summary>
    private void FailIfThrown(Exception failure, int forGeneration, string what)
    {
        if (failure == null)
        {
            return;
        }
        if (forGeneration != _generation)
        {
            return; // it was for a folder that was left
        }
        _problemText = what + ": " + failure.Message;
        if (_reading)
        {
            _reading = false;
            _generation++;
            MusicManagerFrame.Current.OpenFailed(_problemText);
        }
        else
        {
            RefreshStatusBar();
        }
    }

    /// <summary>an error message with an OK button (Java: JOptionPane.showMessageDialog)</summary>
    private static void ShowError(string message)
    {
        _ = MusicManagerFrame.Current.ShowMessageAsync("Music Manager", message);
    }

    /// <summary>runs the action on the UI thread (the view models report from the thread of the use case)</summary>
    private void Post(Action action)
    {
        _ui.TryEnqueue(() => action());
    }

    /// <summary>
    /// Runs the work on the executor's thread and then tells done, on the UI thread, how it ended (null = it did not throw):
    /// the Java SwingWorker with its doInBackground and done.
    /// </summary>
    private void RunJob(SerialExecutor executor, Action work, Action<Exception> done)
    {
        executor.Execute(() =>
        {
            Exception failure = null;
            try
            {
                work();
            }
            catch (Exception e)
            {
                failure = e;
            }
            Post(() => done(failure));
        });
    }

    /// <summary>one thread that runs its jobs one after the other (a single-thread executor of daemon threads)</summary>
    private sealed class SerialExecutor
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();

        public SerialExecutor(string name)
        {
            var thread = new Thread(Run) { Name = name, IsBackground = true };
            thread.Start();
        }

        public void Execute(Action job)
        {
            _queue.Add(job);
        }

        private void Run()
        {
            foreach (Action job in _queue.GetConsumingEnumerable())
            {
                job();
            }
        }
    }
}
