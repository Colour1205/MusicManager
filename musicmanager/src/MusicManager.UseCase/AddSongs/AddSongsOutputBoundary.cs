namespace MusicManager.UseCase.AddSongs;

public interface IAddSongsOutputBoundary
{
    /// <summary>the songs are staged, ready to be added (or none could be added: see the output data)</summary>
    void PresentAdded(AddSongsOutputData outputData);

    /// <summary>the staged songs are in the library now, or could not be put there (the library is as it was)</summary>
    void PresentCommitted(AddCommitOutputData outputData);

    /// <summary>a long step made progress (done/total may be 0 when unknown)</summary>
    void PresentProgress(string message, int done, int total);

    /// <summary>nothing could be done at all</summary>
    void PresentError(string message);
}
