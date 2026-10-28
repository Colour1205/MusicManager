namespace MusicManager.UseCase.StandardizeAlbums;

public interface IStandardizeAlbumsOutputBoundary
{
    /// <summary>the analysis finished: show the plan for review</summary>
    void PresentAlbumsAnalysis(StandardizeAlbumsAnalysisOutputData outputData);

    /// <summary>the plan was written to the files' tags</summary>
    void PresentAlbumsApplied(StandardizeAlbumsApplyOutputData outputData);

    /// <summary>the songs were put in their album folders</summary>
    void PresentAlbumsMoved(StandardizeAlbumsMoveOutputData outputData);

    /// <summary>a long step made progress (done/total may be 0 when unknown)</summary>
    void PresentProgress(string message, int done, int total);

    /// <summary>something went wrong that the user must know about</summary>
    void PresentError(string message);
}
