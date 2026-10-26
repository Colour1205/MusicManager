using Microsoft.UI.Dispatching;
using MusicManager.InterfaceAdapter.ConvertLRC;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.ConvertLRC;
using MusicManager.UseCase.OpenLibrary;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using AppContext = MusicManager.View.Library.AppContext; // System.AppContext would otherwise be ambiguous

namespace MusicManager.App;

/// <summary>
/// The only place that knows every layer: wires the use cases to the one DataAccess (Java: app/Main.java buildContext)
/// and hands out the UI-thread dispatcher that the View project's classes (ProcessFlow, SongPreviewLoader, ...) take as
/// an <c>Action&lt;Action&gt;</c> because they never see WinUI types.
/// </summary>
public static class Main
{
    /// <summary>
    /// Runs an action on the UI thread: at once when the caller already is on it, otherwise it is queued on the
    /// window's <see cref="DispatcherQueue"/>. Set by App on launch (before the window is made); safe to call from any
    /// thread. Pass it to <c>new SongPreviewLoader(context.PreviewPipelines, Main.UiDispatcher)</c>.
    /// </summary>
    public static Action<Action> UiDispatcher { get; private set; } = action => action();

    /// <summary>makes the dispatcher for the UI thread's queue (call once, on the UI thread, before BuildContext)</summary>
    public static void InitializeDispatcher(DispatcherQueue queue)
    {
        UiDispatcher = action =>
        {
            if (queue.HasThreadAccess)
            {
                action();
            }
            else
            {
                queue.TryEnqueue(() => action());
            }
        };
    }

    /// <summary>wires the use cases to the one DataAccess, the way every screen of the window uses them</summary>
    public static AppContext BuildContext(MusicManager.DataAccess.DataAccess dataAccess)
    {
        LibraryViewModel libraryViewModel = new LibraryViewModel();
        LibraryController libraryController = new LibraryController(
                new OpenLibraryInteractor(dataAccess, new LibraryPresenter(libraryViewModel)));

        // separate Standardize pipelines with their own view models: one only scans the folder for the song cards'
        // status, the other runs the preview, review and apply of what the user asked for, and the song window gets a
        // new one for every preview it makes
        StandardizePipeline scan = NewStandardizePipeline(dataAccess);
        StandardizePipeline process = NewStandardizePipeline(dataAccess);

        // the deprecated Convert LRC engine, only reachable when AppFeatures.LegacyConvertLrc is on
        ConvertLRCViewModal convertViewModal = new ConvertLRCViewModal();
        ConvertLRCInteractor convertInteractor = new ConvertLRCInteractor();
        convertInteractor.ConvertLRC(dataAccess, new ConvertLRCPresenter(convertViewModal));
        ConvertLRCController convertController = new ConvertLRCController(convertInteractor);

        return new AppContext(dataAccess, libraryController, libraryViewModel, scan.Controller, scan.ViewModel,
                process.Controller, process.ViewModel, convertController, convertViewModal,
                () => NewStandardizePipeline(dataAccess));
    }

    /// <summary>a Standardize controller over the one DataAccess, with a view model of its own for its presenter</summary>
    private static StandardizePipeline NewStandardizePipeline(MusicManager.DataAccess.DataAccess dataAccess)
    {
        StandardizeViewModel viewModel = new StandardizeViewModel();
        StandardizePresenter presenter = new StandardizePresenter(viewModel);
        StandardizeController controller = new StandardizeController(
                // the songs of a library are checked (and written) several at once, one worker for each core up to 16
                new StandardizeLyricsInteractor(dataAccess, presenter, Math.Clamp(Environment.ProcessorCount, 2, 16)),
                new StandardizeArtistsInteractor(dataAccess, presenter));
        return new StandardizePipeline(controller, viewModel);
    }
}
