namespace MusicManager.UseCase.OpenLibrary;

public interface IOpenLibraryInputBoundary
{
    /// <summary>reads every song of the folder (tags and a small cover thumbnail) and presents the list</summary>
    void Execute(OpenLibraryInputData inputData);
}
