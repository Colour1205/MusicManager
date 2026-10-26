namespace MusicManager.UseCase.OpenLibrary;

public interface IOpenLibraryOutputBoundary
{
    /// <summary>called once with every song of the folder</summary>
    void Present(OpenLibraryOutputData outputData);

    /// <summary>called while the folder is read, so a big library can show progress</summary>
    void PresentProgress(int done, int total);

    /// <summary>called instead of Present() when the folder cannot be opened</summary>
    void PresentError(string message);
}
