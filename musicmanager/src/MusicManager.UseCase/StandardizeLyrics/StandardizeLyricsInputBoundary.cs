namespace MusicManager.UseCase.StandardizeLyrics;

public interface IStandardizeLyricsInputBoundary
{
    /// <summary>runs (or, with dryRun, only plans) the lyrics standardization for a folder</summary>
    void Execute(StandardizeLyricsInputData inputData);
}
