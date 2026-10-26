namespace MusicManager.UseCase.StandardizeArtists;

public interface IStandardizeArtistsOutputBoundary
{
    /// <summary>the analysis finished: show the plan for review</summary>
    void PresentAnalysis(StandardizeArtistsAnalysisOutputData outputData);

    /// <summary>the plan was written to the files</summary>
    void PresentApplied(StandardizeArtistsApplyOutputData outputData);

    /// <summary>the files of the songs were renamed after their tags</summary>
    void PresentRenamed(StandardizeArtistsRenameOutputData outputData);

    /// <summary>a long step made progress (done/total may be 0 when unknown)</summary>
    void PresentProgress(string message, int done, int total);

    /// <summary>something went wrong that the user must know about</summary>
    void PresentError(string message);
}
