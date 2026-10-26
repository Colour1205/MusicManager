using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Standardize;
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
    private const int Changes = 2;
    private const string Arrow = "  →  ";
    private const int MaxErrorTextInBar = 500;

    private readonly ProcessFlow.IHost _host;
    private readonly StandardizeController _controller;
    private readonly StandardizeViewModel _viewModel;
    private readonly IProcessPrompts _prompts;
    private readonly Action _onClosed;
    private readonly string _folder;
    private readonly ISet<string> _scope;
    private readonly StandardizeOptions _options;
    private readonly bool _doLyrics;
    private readonly bool _doArtists;
    private readonly DispatcherQueue _dispatcher; // the UI thread, which the view model events have to be moved to

    private readonly ListView[] _lists;
    private readonly TextBlock[] _headlines;
    private readonly TextBlock[] _details;
    private readonly SelectorBarItem[] _sectionItems;
    private readonly SkeletonLines[] _skeletons; // the redacted blocks in the list header of each section
    private readonly int[] _rowCounts = new int[3]; // how many rows each section has been given so far
    private List<ArtistGroupItem> _artistItems = new List<ArtistGroupItem>();

    // what the preview found (touched on the UI thread only)
    private ArtistPlan _currentPlan;
    private int _artistUpdateCount;
    private int _renameCount; // songs whose files are not named "Artist - Title" after their tags: they are renamed with the rest
    private StandardizeLyricsOutputData _lyricsPreview;
    private bool _lyricsHasWork;
    private bool _busy;
    private bool _previewing; // the busy run is the preview (it shows the status text and, for a section without rows, redacted blocks)
    private bool _applied;
    private bool _previewFailed;
    private volatile bool _closed;
    private int _shownSection = -1;
    private bool _selecting; // the code is changing the selection of the section bar

    // written by the view model events, which arrive on the worker threads, and read when a run is over
    private readonly List<string> _runErrors = new List<string>();
    private volatile string _lyricsDoneSummary = "";
    private volatile string _artistsDoneSummary = "";
    private volatile string _renamedDoneSummary = "";

    public ReviewPage(ProcessFlow.IHost host, StandardizeController controller, StandardizeViewModel viewModel,
            string folder, ISet<string> scope, StandardizeOptions options, IProcessPrompts prompts, Action onClosed)
    {
        _host = host;
        _controller = controller;
        _viewModel = viewModel;
        _folder = folder;
        _scope = scope;
        _options = options;
        _prompts = prompts;
        _onClosed = onClosed;
        _doLyrics = options.IsLyrics;
        _doArtists = options.IsArtists;

        InitializeComponent();
        _dispatcher = DispatcherQueue;
        _lists = new[] { LyricsList, ArtistList, ChangesList };
        _headlines = new[] { LyricsHeadline, ArtistsHeadline, ChangesHeadline };
        _details = new[] { LyricsDetail, ArtistsDetail, ChangesDetail };
        _sectionItems = new[] { LyricsItem, ArtistsItem, ChangesItem };
        _skeletons = new[] { LyricsSkeleton, ArtistsSkeleton, ChangesSkeleton };

        CaptionText.Text = ReviewText.SongCount(scope.Count) + ": continue by pressing Apply";
        BuildTop();
        for (int section = 0; section < _lists.Length; section++)
        {
            SetSummary(section, "", "");
        }
        SelectSection(_doLyrics ? Lyrics : Artists);
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
        bool trash = _options.IsRecycleBin;
        bool romanize = _doLyrics && _options.IsRomaji;
        string separator = _options.Separator;
        bool useAi = _options.IsUseClaudeForArtists;
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
                _controller.AnalyzeArtists(_folder, separator, useAi, apiKey, model, _scope);
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
        bool applyRename = _doArtists && _currentPlan != null && _renameCount > 0;
        if (!applyLyrics && !applyArtists && !applyRename)
        {
            return;
        }

        _lyricsDoneSummary = "";
        _artistsDoneSummary = "";
        _renamedDoneSummary = "";
        ClearErrors();
        SetBusy(true);
        ArtistPlan plan = _currentPlan;
        bool trash = _options.IsRecycleBin;
        bool romanize = _options.IsRomaji;
        string apiKey = _options.ApiKey;
        string model = _options.Model;

        RunInBackground(() =>
        {
            // artists first, so the .lrc header tags written next carry the standardized names
            if (applyArtists)
            {
                _controller.ApplyArtists(plan);
            }
            if (applyLyrics)
            {
                _controller.StandardizeLyrics(_folder, false, trash, romanize, apiKey, model, _scope);
            }
            // the files are named after the tags that were just written, last: the lyrics above find them by their old names
            if (applyRename)
            {
                _controller.RenameFiles(plan);
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
        string done = ReviewText.ResultMessage(_artistsDoneSummary, _lyricsDoneSummary, _renamedDoneSummary);
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
                    RunOnUiThread(() => ShowArtists(plan, updates, summary, renames));
                    break;
                }
            case StandardizeViewModel.ArtistsApplied:
                _artistsDoneSummary = state.ArtistsAppliedSummary;
                break;
            case StandardizeViewModel.ArtistsRenamed:
                _renamedDoneSummary = state.ArtistsRenamedSummary;
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

    private void ShowArtists(ArtistPlan plan, List<SongArtistUpdate> updates, string summary, int renames)
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
        _renameCount = renames;
        SetSummary(Artists, summary);

        var cards = new List<ResultCardItem>();
        int values = 0;
        foreach (SongArtistUpdate update in updates)
        {
            var lines = new List<string>();
            foreach (FieldChange change in update.Changes)
            {
                lines.Add(change.Field + ":  " + Shown(change.Before) + Arrow + Shown(change.After));
                values++;
            }
            cards.Add(new ResultCardItem(ReviewText.DisplayName(update.AudioPath), "", string.Join("\n", lines), "",
                    ResultTone.Neutral));
        }
        ChangesList.ItemsSource = cards;
        _rowCounts[Changes] = cards.Count;
        SetSummary(Changes,
                updates.Count == 0 ? "No tags will change"
                        : values + " tag values in " + ReviewText.SongCount(updates.Count) + " will be rewritten",
                renames == 0 ? "Double check to make sure" : "Double check to make sure\n" + ReviewText.RenameNote(renames));
        UpdateSkeletons();
        UpdateApplyEnabled();
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
            bool used = section == Lyrics ? _doLyrics : _doArtists;
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
        return (_doLyrics && _lyricsHasWork)
                || (_doArtists && _currentPlan != null && (_artistUpdateCount > 0 || _renameCount > 0));
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

    /// <summary>how often the lyrics action list of the last preview contains this action</summary>
    public int LyricsPreviewCount(LyricsAction action)
    {
        return _lyricsPreview == null ? 0 : _lyricsPreview.Count(action);
    }
}
