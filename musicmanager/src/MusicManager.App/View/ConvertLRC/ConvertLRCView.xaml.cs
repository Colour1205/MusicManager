using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.ConvertLRC;
using MusicManager.UseCase.AppSettings;
using MusicManager.UseCase.ConvertLRC;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MusicManager.App.View.ConvertLRC;

/// <summary>
/// Home screen: pick a music folder, then move on to SongListView. Prefills the path from persisted settings, and
/// remembers it (and the current default formats) whenever a valid folder is confirmed.
/// </summary>
/// <remarks>
/// Deprecated: the old stand-alone Convert LRC feature, kept and unused. The app is one window now (View/Library); the
/// convert feature comes back in it by switching on AppFeatures.LegacyConvertLrc.
/// </remarks>
[Obsolete("the old stand-alone Convert LRC feature; the convert feature is behind AppFeatures.LegacyConvertLrc")]
public sealed partial class ConvertLRCView : Page
{
    private readonly IConvertLRCDataAccess _dataAccess;
    private readonly IAppSettingsDataAccess _appSettingsDataAccess;
    private readonly IntPtr _windowHandle;
    private readonly Action<string, List<Music>, List<string>> _showSongs;

    /// <param name="windowHandle">the handle of the window that shows the page (the folder picker needs it)</param>
    /// <param name="showSongs">opens the song list for a path, its songs and the formats to start with</param>
    public ConvertLRCView(IConvertLRCDataAccess dataAccess, IAppSettingsDataAccess appSettingsDataAccess,
            AppSettings initialSettings, IntPtr windowHandle, Action<string, List<Music>, List<string>> showSongs)
    {
        _dataAccess = dataAccess;
        _appSettingsDataAccess = appSettingsDataAccess;
        _windowHandle = windowHandle;
        _showSongs = showSongs;

        InitializeComponent();
        if (initialSettings.LastPath != null)
        {
            PathBox.Text = initialSettings.LastPath;
        }
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        // Java started in the Downloads folder, or the home folder if there is none
        bool hasDownloads = Directory.Exists(Path.Combine(UserHome.Path, "Downloads"));
        var picker = new FolderPicker
        {
            SuggestedStartLocation = hasDownloads ? PickerLocationId.Downloads : PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _windowHandle);

        StorageFolder folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            PathBox.Text = folder.Path;
            MessageBar.IsOpen = false;
        }
    }

    private async void OnConvertClick(object sender, RoutedEventArgs e)
    {
        string path = JavaString.Trim(PathBox.Text ?? "");
        if (path.Length == 0 || !Directory.Exists(path))
        {
            Say(InfoBarSeverity.Warning, "Invalid path", "Please choose a valid music folder.");
            return;
        }
        MessageBar.IsOpen = false;

        // reload fresh rather than trusting a field cached at construction time: a stale in-memory copy would silently
        // revert formats changed in the song list's Settings dialog
        List<string> currentFormats = ConvertLRCInputData.OrDefaultFormats(_appSettingsDataAccess.Load().DefaultTargetFormats);
        SaveSettings(_appSettingsDataAccess, path, currentFormats);

        ConvertButton.IsEnabled = false;
        try
        {
            List<Music> musics = await Task.Run(() => new List<Music>(_dataAccess.GetMusics(path)));
            _showSongs(path, musics, currentFormats);
        }
        catch (Exception)
        {
            Say(InfoBarSeverity.Error, "Error", "Failed to scan the music folder.");
        }
        finally
        {
            ConvertButton.IsEnabled = true;
        }
    }

    private void Say(InfoBarSeverity severity, string title, string message)
    {
        MessageBar.Severity = severity;
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.IsOpen = true;
    }

    /// <summary>
    /// Remembers the folder and the formats. Java saved a fresh AppSettings(path, formats), which also reset the Options
    /// of the library window to their defaults; the options already saved are kept here.
    /// </summary>
    internal static void SaveSettings(IAppSettingsDataAccess access, string path, List<string> formats)
    {
        AppSettings settings = access.Load()?.Copy() ?? new AppSettings(path, formats);
        settings.LastPath = path;
        settings.DefaultTargetFormats = formats;
        access.Save(settings);
    }
}
