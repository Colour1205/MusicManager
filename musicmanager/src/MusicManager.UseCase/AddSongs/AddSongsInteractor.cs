using MusicManager.Common;

namespace MusicManager.UseCase.AddSongs;

/// <summary>
/// Gets the songs the user picked ready for the library. They are only staged, not put in it: making their tags, names and
/// folders consistent with the rest of the library is what the Standardize run that follows is for, with a review before
/// anything is written, and the songs go into the library only when the user confirms that review (<see cref="Commit"/>);
/// if the user does not, they are thrown away (<see cref="Discard"/>) and the library has never changed.
/// </summary>
public class AddSongsInteractor : IAddSongsInputBoundary
{
    private readonly IAddSongsDataAccess _dataAccess;
    private readonly IAddSongsOutputBoundary _outputBoundary;

    public AddSongsInteractor(IAddSongsDataAccess dataAccess, IAddSongsOutputBoundary outputBoundary)
    {
        _dataAccess = dataAccess;
        _outputBoundary = outputBoundary;
    }

    public void Add(AddSongsInputData inputData)
    {
        if (inputData.LibraryPath == null || JavaString.IsBlank(inputData.LibraryPath) || !Directory.Exists(inputData.LibraryPath))
        {
            _outputBoundary.PresentError("The music folder is not there. Open a folder first.");
            return;
        }
        if (inputData.Sources.Count == 0)
        {
            _outputBoundary.PresentError("Nothing was picked.");
            return;
        }

        _outputBoundary.PresentProgress("Adding songs...", 0, 0);
        AddSongsOutputData result = _dataAccess.AddSongs(inputData.LibraryPath, inputData.Sources, inputData.FormatPreference,
                (done, total) => _outputBoundary.PresentProgress("Adding songs...", done, total));
        _outputBoundary.PresentAdded(result);
    }

    public void Commit()
    {
        _outputBoundary.PresentCommitted(_dataAccess.CommitAdded());
    }

    public void Discard()
    {
        _dataAccess.DiscardAdded();
    }
}
