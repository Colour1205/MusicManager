using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.ConvertLRC;
using MusicManager.UseCase.AppSettings;
using MusicManager.UseCase.ConvertLRC;
using Windows.Graphics;
using WinRT.Interop;

namespace MusicManager.App.View.ConvertLRC;

/// <summary>
/// The window of the deprecated stand-alone Convert LRC screens (Java: the two JFrames ConvertLRCView and SongListView):
/// the folder page, and, after Convert LRC, the list of songs with Back to the folder page. Mica backdrop, like the app.
/// </summary>
/// <remarks>
/// Deprecated: the old stand-alone Convert LRC feature, kept and unused. The app is one window now (View/Library); the
/// convert feature comes back in it by switching on AppFeatures.LegacyConvertLrc. <see cref="TryOpen"/> only opens the
/// window while that switch is on.
/// </remarks>
[Obsolete("the old stand-alone Convert LRC feature; the convert feature is behind AppFeatures.LegacyConvertLrc")]
public sealed class ConvertLRCWindow : Window
{
    private readonly IConvertLRCDataAccess _dataAccess;
    private readonly IConvertLRCInputBoundary _interactor;
    private readonly ConvertLRCViewModal _viewModal;
    private readonly IAppSettingsDataAccess _appSettingsDataAccess;
    private readonly IntPtr _handle;

    public ConvertLRCWindow(IConvertLRCDataAccess dataAccess, IConvertLRCInputBoundary interactor,
            ConvertLRCViewModal viewModal, IAppSettingsDataAccess appSettingsDataAccess)
    {
        _dataAccess = dataAccess;
        _interactor = interactor;
        _viewModal = viewModal;
        _appSettingsDataAccess = appSettingsDataAccess;
        _handle = WindowNative.GetWindowHandle(this);

        Title = "Music Manager";
        SystemBackdrop = new MicaBackdrop();
        ResizeTo(900, 680);
        ShowFolderPage();
    }

    /// <summary>
    /// Opens the window if the legacy Convert LRC feature is switched on (AppFeatures.LegacyConvertLrc); null otherwise.
    /// </summary>
    public static ConvertLRCWindow TryOpen(IConvertLRCDataAccess dataAccess, IConvertLRCInputBoundary interactor,
            ConvertLRCViewModal viewModal, IAppSettingsDataAccess appSettingsDataAccess)
    {
        bool enabled = AppFeatures.LegacyConvertLrc;
        if (!enabled)
        {
            return null;
        }
        var window = new ConvertLRCWindow(dataAccess, interactor, viewModal, appSettingsDataAccess);
        window.Activate();
        return window;
    }

    private void ShowFolderPage()
    {
        AppSettings settings = _appSettingsDataAccess.Load();
        Content = new ConvertLRCView(_dataAccess, _appSettingsDataAccess, settings, _handle, ShowSongPage);
    }

    private void ShowSongPage(string path, List<Music> musics, List<string> formats)
    {
        Content = new SongListView(path, musics, _interactor, _viewModal, _appSettingsDataAccess, formats, ShowFolderPage);
    }

    // ------------------------------------------------------------------ window size

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>sizes the window in logical pixels and puts it in the middle of the usable area of the screen</summary>
    private void ResizeTo(int width, int height)
    {
        uint dpi = GetDpiForWindow(_handle);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        RectInt32 work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = new SizeInt32(Math.Min((int)Math.Round(width * scale), work.Width),
                Math.Min((int)Math.Round(height * scale), work.Height));
        AppWindow.Resize(size);
        AppWindow.Move(new PointInt32(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2));
    }
}
