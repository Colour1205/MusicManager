// =====================================================================================================================
// THE SHELL'S PUBLIC SURFACE (namespace MusicManager.App). Names follow the Java MusicManagerFrame; the pages live in
// one Frame (Home -> Library -> Review) and the title bar's back button goes back.
//
// Reach the shell:   MusicManagerFrame.Current (same as App.MusicManagerFrame)      .Context  the AppContext (settings, controllers, view models)
//                    .Flow  the ProcessFlow (library: Flow.Start(folder, songs, settings))      .Handle  the HWND (FolderPicker)
//                    .XamlRoot (for ContentDialog)      Main.UiDispatcher  an Action<Action> that runs on the UI thread,
//                    e.g. new SongPreviewLoader(context.PreviewPipelines, Main.UiDispatcher)
//
// Navigation:        OpenFolder(path)     Home -> Library: PageFrame.Navigate(typeof(LibraryPage), path)   (HomePage calls it)
//                    ShowHome()           back to the Home page
//                    ShowLibrary()        [ProcessFlow.IHost] Review -> Library (GoBack to the cached library page)
//                    ShowReview(page)     [ProcessFlow.IHost] shows the review page that ProcessFlow made (ReviewHostPage hosts it)
//                    OpenFailed(message)  the folder could not be read: back to Home, which shows the message in an InfoBar
//                    SetBackEnabled(bool) enables/disables the title-bar back button (it is enabled again on every navigation)
//                    OnSongsProcessed / OnConvertProgress / SetBusy  [ProcessFlow.IHost] forwarded to the LibraryPage (below)
//
// Dialogs and threads: ShowDialogAsync(ContentDialog)  sets XamlRoot, shows one dialog at a time (queued), UI thread safe
//                    ShowMessageAsync(title, message)  an OK dialog          RunOnUiThread(Action)
//
// WHAT THE PAGES MUST PROVIDE (the window calls these; the file names are the ones the project expects)
//   LibraryPage : Microsoft.UI.Xaml.Controls.Page, View\Library\LibraryPage.xaml(.cs), public parameterless constructor.
//        OnNavigatedTo: e.Parameter is the music folder (string); when e.NavigationMode == New start reading it, when it is
//        Back (coming back from the Review) keep everything as it is. The window sets NavigationCacheMode = Required, so Back
//        returns the SAME instance. Public methods the window forwards ProcessFlow.IHost to:
//        RefreshSongs(ISet<string> audioPaths), ShowConvertResult(string audioPath, bool success), SetHostBusy(bool busy).
//        If the folder cannot be read: MusicManagerFrame.Current.OpenFailed(message). Process: MusicManagerFrame.Current.Flow.Start(...).
//   ReviewPage : UserControl or Page implementing ProcessFlow.IReviewPage, View\Library\ReviewPage.xaml(.cs). The window makes it with
//        new ReviewPage(host, controller, viewModel, folder, scope, options, prompts, onClosed)   (see NewReviewPage below).
//        The title-bar back button asks the page through IBackHandler.OnBackRequested() when the page implements it (true =
//        handled; the page then calls onClosed() and host.ShowLibrary() itself, as the Java Back does); a page without it is
//        left by the window (onClosed() + ShowLibrary()). After a finished run the page calls onClosed() and host.ShowLibrary().
//   ContentDialogPrompts : ProcessFlow's IProcessPrompts, View\Library\ContentDialogPrompts.cs, public parameterless constructor.
//
// A Frame only navigates to a page that has a XAML file (x:Class): a code-only Page subclass crashes the process natively
// in Frame.Navigate. LibraryPage therefore needs its .xaml; ReviewPage does not (it is only hosted, never navigated to).
// Until those three files exist, _Stubs\ supplies placeholders (see MusicManager.Fluent.csproj); they drop out by themselves.
// =====================================================================================================================

using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using MusicManager.App.View;
using MusicManager.App.View.Library;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.View.Library;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using AppContext = MusicManager.View.Library.AppContext; // System.AppContext would otherwise be ambiguous

namespace MusicManager.App;

/// <summary>
/// The one window of the app: Home (pick a folder, Open) -> Library (the songs, to process all at once or one by one)
/// -> Review (what Process would change). It implements ProcessFlow.IHost, which is how a run talks back to the window.
/// </summary>
public sealed partial class MusicManagerFrame : Window, ProcessFlow.IHost
{
    private const string AppName = "Music Manager";

    // default and minimum size in logical pixels (the display's scale is applied when they are used)
    private const int DefaultWidth = 1100;
    private const int DefaultHeight = 760;
    private const int MinimumWidth = 640;
    private const int MinimumHeight = 480;

    private readonly IntPtr _hwnd;
    private LibraryPage _library;
    private Action _onReviewClosed;
    private Task _dialogChain = Task.CompletedTask; // UI thread only
    private string _folderName = "";

    public MusicManagerFrame(AppContext context)
    {
        Current = this;
        Context = context;
        InitializeComponent();
        Title = AppName;
        _hwnd = WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SetIcon();
        SetSizes();

        // Java: setDefaultCloseOperation(EXIT_ON_CLOSE), which also ends the threads that are still working
        Closed += (_, _) => Environment.Exit(0);

        Flow = new ProcessFlow(context, this, new ContentDialogPrompts(), NewReviewPage, Main.UiDispatcher);

        // Alt+Left = the back button, as in every other Windows app with a Frame
        var back = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        back.Invoked += (_, args) =>
        {
            args.Handled = true;
            if (PageFrame.CanGoBack && AppTitleBar.IsBackButtonEnabled)
            {
                GoBackRequested();
            }
        };
        RootGrid.KeyboardAccelerators.Add(back);

        PageFrame.Navigated += PageFrame_Navigated;
        PageFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
    }

    /// <summary>the one window (also App.MusicManagerFrame)</summary>
    public new static MusicManagerFrame Current { get; private set; }

    /// <summary>settings, controllers and view models, built once by the composition root</summary>
    public AppContext Context { get; }

    /// <summary>what happens when Process is pressed (Java: the ProcessFlow of MusicManagerFrame)</summary>
    public ProcessFlow Flow { get; }

    /// <summary>the window handle, for the pickers that need one</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>the XamlRoot of the window's content, which every ContentDialog needs</summary>
    public XamlRoot XamlRoot => RootGrid.XamlRoot;

    /// <summary>the window as the parent of dialogs</summary>
    public object Frame() => this;

    // ------------------------------------------------------------------ ProcessFlow.IHost

    /// <summary>Shows the flow's review page in place of the library (a page of the Frame, with Back in the title bar).</summary>
    public void ShowReview(object reviewPage)
    {
        RunOnUiThread(() => PageFrame.Navigate(typeof(ReviewHostPage), reviewPage, new DrillInNavigationTransitionInfo()));
    }

    public void ShowLibrary()
    {
        RunOnUiThread(() =>
        {
            _onReviewClosed = null; // the review of a finished run is of no use any more
            GoBackTo(typeof(LibraryPage));
        });
    }

    public void OnSongsProcessed(ISet<string> audioPaths)
    {
        RunOnUiThread(() => _library?.RefreshSongs(audioPaths));
    }

    public void OnConvertProgress(string audioPath, bool success)
    {
        RunOnUiThread(() => _library?.ShowConvertResult(audioPath, success));
    }

    public void SetBusy(bool busy)
    {
        RunOnUiThread(() => _library?.SetHostBusy(busy));
    }

    // ------------------------------------------------------------------ navigation (used by the pages)

    /// <summary>the Home page chose a folder: show the library in its "reading songs" state and start reading</summary>
    public void OpenFolder(string path)
    {
        RunOnUiThread(() =>
        {
            _folderName = FolderName(path);
            // a library that is still on the stack (Open pressed again) is replaced, never stacked up
            GoBackTo(typeof(HomePage));
            PageFrame.Navigate(typeof(LibraryPage), path, new DrillInNavigationTransitionInfo());
        });
    }

    /// <summary>the library page's back button</summary>
    public void ShowHome()
    {
        RunOnUiThread(() =>
        {
            _onReviewClosed?.Invoke();
            _onReviewClosed = null;
            if (!GoBackTo(typeof(HomePage)))
            {
                PageFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
            }
        });
    }

    /// <summary>the folder could not be read: say so and go back to the start page</summary>
    public void OpenFailed(string message)
    {
        RunOnUiThread(() =>
        {
            ShowHome();
            if (PageFrame.Content is HomePage home)
            {
                home.ShowMessage("Could not open the folder", message, InfoBarSeverity.Error);
            }
            else
            {
                _ = ShowMessageAsync(AppName, message);
            }
        });
    }

    /// <summary>enables or disables the back button of the title bar (it is enabled again on every navigation)</summary>
    public void SetBackEnabled(bool enabled)
    {
        RunOnUiThread(() => AppTitleBar.IsBackButtonEnabled = enabled);
    }

    /// <summary>the last part of the path, or the whole path for a drive root such as C:\</summary>
    public static string FolderName(string path)
    {
        // java.io.File: trailing separators are dropped, both separators count, and a drive prefix such as "C:" is
        // not part of the name
        string trimmed = path.TrimEnd('\\', '/');
        int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        string name = cut < 0 ? trimmed : trimmed.Substring(cut + 1);
        if (cut < 0 && name.Length >= 2 && name[1] == ':' && char.IsLetter(name[0]))
        {
            name = name.Substring(2);
        }
        return name.Length == 0 ? path : name;
    }

    // ------------------------------------------------------------------ dialogs and threads

    /// <summary>runs an action on the UI thread: at once when already there, queued otherwise</summary>
    public void RunOnUiThread(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => action());
        }
    }

    /// <summary>
    /// Shows a ContentDialog in this window (the XamlRoot is set when the dialog has none). A ContentDialog can only be
    /// open one at a time: a dialog asked for while another one is open waits for it instead of failing.
    /// </summary>
    public Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        var done = new TaskCompletionSource<ContentDialogResult>();
        RunOnUiThread(() =>
        {
            dialog.XamlRoot ??= XamlRoot;
            Task<ContentDialogResult> shown = ShowAfter(_dialogChain, dialog);
            _dialogChain = shown;
            shown.ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    done.SetException(task.Exception.InnerExceptions);
                }
                else
                {
                    done.SetResult(task.Result);
                }
            }, TaskScheduler.Default);
        });
        return done.Task;
    }

    /// <summary>an OK dialog with a message</summary>
    public Task ShowMessageAsync(string title, string message)
    {
        return ShowDialogAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        });
    }

    private static async Task<ContentDialogResult> ShowAfter(Task before, ContentDialog dialog)
    {
        try
        {
            await before;
        }
        catch (Exception)
        {
            // the dialog before this one failed; that is its caller's business
        }
        // a dialog of someone else may still be open: wait a moment and ask again
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await dialog.ShowAsync();
            }
            catch (Exception e) when ((e is COMException || e is InvalidOperationException) && attempt < 100)
            {
                await Task.Delay(100);
            }
        }
    }

    // ------------------------------------------------------------------ the Frame

    private void PageFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Home and Library keep their state when a page is stacked on top and Back returns (the Java CardLayout kept them)
        if (e.Content is Page page && (e.SourcePageType == typeof(HomePage) || e.SourcePageType == typeof(LibraryPage)))
        {
            page.NavigationCacheMode = NavigationCacheMode.Required;
        }
        if (e.SourcePageType == typeof(LibraryPage))
        {
            _library = e.Content as LibraryPage;
        }
        if (e.NavigationMode == NavigationMode.Back)
        {
            PageFrame.ForwardStack.Clear(); // a review that was left is not coming back
        }

        AppTitleBar.IsBackButtonVisible = PageFrame.CanGoBack;
        AppTitleBar.IsBackButtonEnabled = true;
        AppTitleBar.Subtitle = e.SourcePageType == typeof(LibraryPage) ? _folderName
                : e.SourcePageType == typeof(ReviewHostPage) ? "Preview"
                : "";

        if (e.Content is HomePage home)
        {
            home.FocusPath();
        }
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        GoBackRequested();
    }

    /// <summary>Back: Review -> Library, Library -> Home; a page may answer the request itself</summary>
    private void GoBackRequested()
    {
        if (PageFrame.Content is IBackHandler handler && handler.OnBackRequested())
        {
            return;
        }
        if (PageFrame.Content is ReviewHostPage)
        {
            // the review page did not take over: leave it the way its own Back button does
            Action closed = _onReviewClosed;
            _onReviewClosed = null;
            closed?.Invoke();
            ShowLibrary();
            return;
        }
        if (PageFrame.Content is LibraryPage)
        {
            ShowHome();
            return;
        }
        if (PageFrame.CanGoBack)
        {
            PageFrame.GoBack();
        }
    }

    /// <summary>goes back until the page of this type is the current one; false if it is not on the back stack</summary>
    private bool GoBackTo(Type pageType)
    {
        if (PageFrame.CurrentSourcePageType == pageType)
        {
            return true;
        }
        if (!PageFrame.BackStack.Any(entry => entry.SourcePageType == pageType))
        {
            return false;
        }
        while (PageFrame.CanGoBack && PageFrame.CurrentSourcePageType != pageType)
        {
            PageFrame.GoBack();
        }
        return PageFrame.CurrentSourcePageType == pageType;
    }

    /// <summary>how ProcessFlow gets its review page: the WinUI one</summary>
    private ProcessFlow.IReviewPage NewReviewPage(ProcessFlow.IHost host, StandardizeController controller,
            StandardizeViewModel viewModel, string folder, ISet<string> scope, StandardizeOptions options,
            IProcessPrompts prompts, Action onClosed)
    {
        _onReviewClosed = onClosed;
        return new ReviewPage(host, controller, viewModel, folder, scope, options, prompts, onClosed);
    }

    // ------------------------------------------------------------------ window geometry

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>the display's scale (1.0 = 100%); window sizes are written in logical pixels</summary>
    private double Scale()
    {
        uint dpi = GetDpiForWindow(_hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private static int ToPixels(int logical, double scale) => (int)Math.Round(logical * scale);

    /// <summary>default size (never larger than the usable area of the screen), minimum size, centered</summary>
    private void SetSizes()
    {
        double scale = Scale();
        RectInt32 work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = ToPixels(MinimumWidth, scale);
            presenter.PreferredMinimumHeight = ToPixels(MinimumHeight, scale);
        }

        var size = new SizeInt32(Math.Min(ToPixels(DefaultWidth, scale), work.Width),
                Math.Min(ToPixels(DefaultHeight, scale), work.Height));
        AppWindow.Resize(size);
        // Java's setLocationRelativeTo(null): the middle of the usable area of the screen
        AppWindow.Move(new PointInt32(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2));
    }

    private void SetIcon()
    {
        string icon = Path.Combine(System.AppContext.BaseDirectory, "Assets", "musicmanager-icon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
            AppTitleBar.IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri(icon)) };
        }
    }
}
