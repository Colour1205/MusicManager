using MusicManager.UseCase.AddSongs;

namespace MusicManager.InterfaceAdapter.AddSongs;

public class AddSongsController
{
    private readonly IAddSongsInputBoundary _interactor;

    public AddSongsController(IAddSongsInputBoundary interactor)
    {
        _interactor = interactor;
    }

    /// <summary>stages the picked files and folders for the library (the library is not changed until <see cref="Commit"/>); the result arrives as view model events (blocks until done, call it off the UI thread)</summary>
    public void Add(string libraryPath, IEnumerable<string> sources)
    {
        _interactor.Add(new AddSongsInputData(libraryPath, sources));
    }

    /// <summary>the same, adding only one version of a song that is there in several formats: the preferred one (an extension such as "flac"), or the largest file</summary>
    public void Add(string libraryPath, IEnumerable<string> sources, string preferredFormat)
    {
        _interactor.Add(new AddSongsInputData(libraryPath, sources, preferredFormat));
    }

    /// <summary>puts the staged songs into the library, all or none; the result arrives as a "committed" event, and is in the state when this returns (blocks until done, call it off the UI thread)</summary>
    public void Commit()
    {
        _interactor.Commit();
    }

    /// <summary>throws the staged songs away: the library has not changed</summary>
    public void Discard()
    {
        _interactor.Discard();
    }
}
