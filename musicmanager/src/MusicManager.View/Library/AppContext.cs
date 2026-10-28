using MusicManager.InterfaceAdapter.AddSongs;
using MusicManager.InterfaceAdapter.ConvertLRC;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.InterfaceAdapter.Standardize;
using MusicManager.UseCase.AppSettings;

namespace MusicManager.View.Library;

/// <summary>
/// Everything the pages of the window need from the rest of the app, built once in the composition root.
///
/// Two Standardize pipelines exist on purpose: the "scan" one only ever does dry runs of the whole folder to fill in the
/// status of every song card, and the "process" one runs the preview, review and apply of what the user asked for. They
/// have separate view models so that the events of one never reach the listeners of the other. The song window's preview
/// needs a third kind: a fresh pipeline for every preview job, so the previews of two windows (or of one window after its
/// Options changed) never share a state either.
/// </summary>
public sealed class AppContext
{
    public IAppSettingsDataAccess Settings { get; }

    public LibraryController LibraryController { get; }
    public LibraryViewModel LibraryViewModel { get; }

    public StandardizeController ScanController { get; }
    public StandardizeViewModel ScanViewModel { get; }

    public StandardizeController ProcessController { get; }
    public StandardizeViewModel ProcessViewModel { get; }

    /// <summary>The deprecated Convert LRC engine; only used when AppFeatures.LegacyConvertLrc is on.</summary>
    public ConvertLRCController ConvertController { get; }
    public ConvertLRCViewModal ConvertViewModal { get; }

    /// <summary>Makes a new, unshared pipeline for one preview job of the song window; null = there is no preview.</summary>
    public Func<StandardizePipeline> PreviewPipelines { get; }

    /// <summary>Stages picked songs for the library and puts them in or drops them (the Add button); null = the app cannot add songs.</summary>
    public AddSongsController AddSongsController { get; }
    public AddSongsViewModel AddSongsViewModel { get; }

    public AppContext(IAppSettingsDataAccess settings, LibraryController libraryController,
            LibraryViewModel libraryViewModel, StandardizeController scanController, StandardizeViewModel scanViewModel,
            StandardizeController processController, StandardizeViewModel processViewModel,
            ConvertLRCController convertController, ConvertLRCViewModal convertViewModal)
        : this(settings, libraryController, libraryViewModel, scanController, scanViewModel, processController,
                processViewModel, convertController, convertViewModal, null)
    {
    }

    public AppContext(IAppSettingsDataAccess settings, LibraryController libraryController,
            LibraryViewModel libraryViewModel, StandardizeController scanController, StandardizeViewModel scanViewModel,
            StandardizeController processController, StandardizeViewModel processViewModel,
            ConvertLRCController convertController, ConvertLRCViewModal convertViewModal,
            Func<StandardizePipeline> previewPipelines)
        : this(settings, libraryController, libraryViewModel, scanController, scanViewModel, processController,
                processViewModel, convertController, convertViewModal, previewPipelines, null, null)
    {
    }

    public AppContext(IAppSettingsDataAccess settings, LibraryController libraryController,
            LibraryViewModel libraryViewModel, StandardizeController scanController, StandardizeViewModel scanViewModel,
            StandardizeController processController, StandardizeViewModel processViewModel,
            ConvertLRCController convertController, ConvertLRCViewModal convertViewModal,
            Func<StandardizePipeline> previewPipelines, AddSongsController addSongsController,
            AddSongsViewModel addSongsViewModel)
    {
        AddSongsController = addSongsController;
        AddSongsViewModel = addSongsViewModel;
        Settings = settings;
        LibraryController = libraryController;
        LibraryViewModel = libraryViewModel;
        ScanController = scanController;
        ScanViewModel = scanViewModel;
        ProcessController = processController;
        ProcessViewModel = processViewModel;
        ConvertController = convertController;
        ConvertViewModal = convertViewModal;
        PreviewPipelines = previewPipelines;
    }
}
