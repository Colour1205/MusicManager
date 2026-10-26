using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicManager.App.View.Library;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.ConvertLRC;
using MusicManager.UseCase.AppSettings;
using MusicManager.UseCase.ConvertLRC;

namespace MusicManager.App.View.ConvertLRC;

/// <summary>
/// Lists the songs found in the chosen path as a grid of cards. Each song can be converted individually (press its card)
/// or all at once (the Start button); the Settings button picks the general target formats, which are persisted for next
/// launch alongside the current path.
/// </summary>
/// <remarks>
/// Deprecated: the old stand-alone Convert LRC feature, kept and unused. The app is one window now (View/Library); the
/// convert feature comes back in it by switching on AppFeatures.LegacyConvertLrc.
/// </remarks>
[Obsolete("the old stand-alone Convert LRC feature; the convert feature is behind AppFeatures.LegacyConvertLrc")]
public sealed partial class SongListView : Page
{
    private readonly string _path;
    private readonly List<Music> _musics;
    private readonly IConvertLRCInputBoundary _interactor;
    private readonly ConvertLRCViewModal _viewModal;
    private readonly IAppSettingsDataAccess _appSettingsDataAccess;
    private readonly Action _onBack;
    private readonly ContentDialogPrompts _prompts = new ContentDialogPrompts();
    private readonly DispatcherQueue _dispatcher;

    private readonly Dictionary<string, SongCard> _cardsByMusicPath = new Dictionary<string, SongCard>();

    private List<string> _selectedFormats;
    private bool _expectingBatchSummary;
    private bool _gone; // the page was left: events of the shared view model are of no interest any more

    // only one conversion (batch or single-song) may run at a time: overlapping conversions would race on the same
    // output files and on the shared ConvertLRCState/ViewModel
    private bool _busy;

    /// <param name="onBack">goes back to the folder page</param>
    public SongListView(string path, List<Music> musics, IConvertLRCInputBoundary interactor,
            ConvertLRCViewModal viewModal, IAppSettingsDataAccess appSettingsDataAccess, List<string> initialFormats,
            Action onBack)
    {
        _path = path;
        _musics = musics;
        _interactor = interactor;
        _viewModal = viewModal;
        _appSettingsDataAccess = appSettingsDataAccess;
        _onBack = onBack;
        _selectedFormats = new List<string>(ConvertLRCInputData.OrDefaultFormats(initialFormats));

        InitializeComponent();
        _dispatcher = DispatcherQueue;

        HeaderText.Text = musics.Count + " song(s) found in " + path;
        var cards = new List<SongCard>();
        foreach (Music music in musics)
        {
            var card = new SongCard(music, OnSongClicked);
            _cardsByMusicPath[music.Path] = card;
            cards.Add(card);
        }
        SongsRepeater.ItemsSource = cards; // UIElements as items are used as they are

        Unloaded += (sender, e) => _gone = true;

        // ConvertLRCState is a single long-lived mutable object shared by every conversion; the interactor can mutate
        // and re-fire it again (e.g. the next song in a batch) before the UI thread gets around to running a queued
        // action, so the fields this listener cares about must be captured into local variables synchronously, right
        // here, rather than read lazily inside the queued action
        viewModal.AddPropertyChangeListener(evt =>
        {
            if (_gone)
            {
                return;
            }
            if ("progress" == evt.PropertyName)
            {
                ConvertLRCState state = viewModal.State;
                string musicPath = state.LastProgressMusicPath;
                bool success = state.IsLastProgressSuccess;
                _dispatcher.TryEnqueue(() =>
                {
                    if (musicPath != null && _cardsByMusicPath.TryGetValue(musicPath, out SongCard card))
                    {
                        card.ShowResult(success);
                    }
                });
            }
            else if ("state" == evt.PropertyName)
            {
                ConvertLRCState state = viewModal.State;
                string message = state.Message;
                bool showSummary = _expectingBatchSummary;
                _dispatcher.TryEnqueue(() =>
                {
                    _busy = false;
                    StartButton.IsEnabled = true;
                    if (showSummary)
                    {
                        _prompts.Info(this, "Conversion result", message);
                    }
                });
            }
        });
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (!_busy)
        {
            _onBack();
        }
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(this, _selectedFormats, formats =>
        {
            _selectedFormats = formats;
            ConvertLRCView.SaveSettings(_appSettingsDataAccess, _path, formats);
        });
        await ContentDialogPrompts.ShowAsync(dialog);
    }

    /// <summary>pressing a song shows conversion options scoped to just that song</summary>
    private async void OnSongClicked(Music music)
    {
        if (_busy)
        {
            _prompts.Info(this, "Conversion in progress", "A conversion is already in progress. Please wait for it to finish.");
            return;
        }

        var dialog = new SongConvertDialog(this, music, _selectedFormats,
                formats => ConvertSongs(new List<Music> { music }, formats, false));
        await ContentDialogPrompts.ShowAsync(dialog);
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_selectedFormats.Count == 0)
        {
            _prompts.Warn(this, "No formats selected", "Select at least one target format in Settings.");
            return;
        }

        ConvertSongs(_musics, _selectedFormats, true);
    }

    private void ConvertSongs(List<Music> songsToConvert, List<string> formats, bool isBatch)
    {
        _busy = true;
        _expectingBatchSummary = isBatch;
        StartButton.IsEnabled = false;

        foreach (Music music in songsToConvert)
        {
            if (_cardsByMusicPath.TryGetValue(music.Path, out SongCard card))
            {
                card.ShowConverting();
            }
        }

        _ = Task.Run(() =>
        {
            var controller = new ConvertLRCController(_interactor);
            controller.Execute(songsToConvert, formats);
        });
    }
}
