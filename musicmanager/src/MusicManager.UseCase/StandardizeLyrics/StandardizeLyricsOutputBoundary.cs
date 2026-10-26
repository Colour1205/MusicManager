namespace MusicManager.UseCase.StandardizeLyrics;

public interface IStandardizeLyricsOutputBoundary
{
    /// <summary>called once after every song has been handled</summary>
    void Present(StandardizeLyricsOutputData outputData);

    /// <summary>called as each step finishes, so a long run can show progress</summary>
    void PresentProgress(int done, int total, string label);

    /// <summary>called instead of Present() when the run cannot start at all (e.g. romaji was asked for but there is no API key)</summary>
    void PresentError(string message);
}
