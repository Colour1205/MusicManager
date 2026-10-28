using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MusicManager.App;

/// <summary>
/// Starts the app (Java: app/Main.java main): builds the context once and shows the one window.
/// A first command-line argument that is an existing folder opens that folder in the library at once (smoke tests).
/// </summary>
public partial class App : Application
{
    private MusicManagerFrame _window;
    private bool _errorShown;

    public App()
    {
        InitializeComponent();
        UnhandledException += (sender, e) =>
        {
            // like the Swing event thread: an exception of one handler does not end the app, but it is never swallowed
            e.Handled = true;
            ReportUnhandled(e.Exception);
        };
    }

    /// <summary>the one window, for the dialogs and pickers of the pages that need its handle (same as MusicManagerFrame.Current)</summary>
    public static MusicManagerFrame MusicManagerFrame => MusicManagerFrame.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Main.InitializeDispatcher(DispatcherQueue.GetForCurrentThread());
        var dataAccess = new DataAccess.DataAccess();
        dataAccess.DiscardUndecidedAdds(); // songs added in an earlier run that were never confirmed: the library does not have them
        _window = new MusicManagerFrame(Main.BuildContext(dataAccess));
        _window.Activate();

        string[] commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length > 1 && Directory.Exists(commandLine[1]))
        {
            // after the first frame: a Frame cannot navigate while its window is still being set up
            string folder = Path.GetFullPath(commandLine[1]);
            _window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _window.OpenFolder(folder));
        }
    }

    private void ReportUnhandled(Exception exception)
    {
        System.Diagnostics.Debug.WriteLine("Unhandled exception: " + exception);
        // opt-in log for smoke tests: set MUSICMANAGER_ERROR_LOG to a file
        string log = Environment.GetEnvironmentVariable("MUSICMANAGER_ERROR_LOG");
        if (!string.IsNullOrEmpty(log))
        {
            try
            {
                File.AppendAllText(log, DateTime.Now.ToString("s") + " " + exception + Environment.NewLine);
            }
            catch (IOException)
            {
                // the log is a convenience
            }
        }
        if (_errorShown || _window == null)
        {
            return; // one message at a time: a failing handler can fire again and again
        }
        _errorShown = true;
        _ = _window.ShowMessageAsync("Music Manager", "Something went wrong: " + exception.Message)
                .ContinueWith(_ => _errorShown = false, TaskScheduler.Default);
    }
}
