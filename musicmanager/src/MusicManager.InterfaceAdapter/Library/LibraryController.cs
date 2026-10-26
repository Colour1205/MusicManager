using MusicManager.UseCase.OpenLibrary;

namespace MusicManager.InterfaceAdapter.Library;

public class LibraryController
{
    private readonly IOpenLibraryInputBoundary _interactor;

    public LibraryController(IOpenLibraryInputBoundary interactor)
    {
        _interactor = interactor;
    }

    /// <summary>reads the folder's songs; the result arrives as view model events (blocks until done, call it off the UI thread)</summary>
    public void Open(string path)
    {
        _interactor.Execute(new OpenLibraryInputData(path));
    }

    /// <summary>
    /// reads only these songs again (their tags changed); the result arrives as a SONGS event whose state has
    /// RefreshedAudioPaths set, and whose song list holds just those songs
    /// </summary>
    public void Refresh(string path, IEnumerable<string> audioPaths)
    {
        _interactor.Execute(new OpenLibraryInputData(path, audioPaths));
    }
}
