using MusicManager.Common;
using MusicManager.Entity;

namespace MusicManager.UseCase.OpenLibrary;

public class OpenLibraryInteractor : IOpenLibraryInputBoundary
{
    private readonly IOpenLibraryDataAccess _dataAccess;
    private readonly IOpenLibraryOutputBoundary _outputBoundary;

    public OpenLibraryInteractor(IOpenLibraryDataAccess dataAccess, IOpenLibraryOutputBoundary outputBoundary)
    {
        _dataAccess = dataAccess;
        _outputBoundary = outputBoundary;
    }

    public void Execute(OpenLibraryInputData inputData)
    {
        string path = inputData.Path;
        if (path == null || JavaString.IsBlank(path) || !Directory.Exists(path))
        {
            _outputBoundary.PresentError("Please choose a valid music folder.");
            return;
        }

        List<Music> songs;
        try
        {
            songs = _dataAccess.ListMusics(path, inputData.OnlyAudioPaths, _outputBoundary.PresentProgress);
        }
        catch (Exception e)
        {
            // .NET never leaves Message null; an empty one is the equivalent of Java's missing message
            string reason = string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
            _outputBoundary.PresentError("Failed to read the music folder: " + reason);
            return;
        }
        // outside the try: a presenter that fails is its own bug, not "the folder could not be read"
        _outputBoundary.Present(new OpenLibraryOutputData(path, songs, inputData.OnlyAudioPaths));
    }
}
