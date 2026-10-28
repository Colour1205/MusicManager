namespace MusicManager.UseCase.StandardizeAlbums;

public interface IStandardizeAlbumsInputBoundary
{
    /// <summary>reads the library's album tags and builds a plan (nothing is written)</summary>
    void Analyze(StandardizeAlbumsAnalyzeInputData inputData);

    /// <summary>recomputes (and presents) what would change after the user edited a name in the plan; writes nothing</summary>
    void Review(StandardizeAlbumsApplyInputData inputData);

    /// <summary>writes the (possibly user-edited) plan to the audio files' ALBUM tags</summary>
    void Apply(StandardizeAlbumsApplyInputData inputData);

    /// <summary>
    /// puts the plan's songs in folders named after their albums. Call it after Apply and after the lyrics (which find the
    /// files by their old paths); what it moved is presented, so that the steps after it can find the files.
    /// </summary>
    void MoveFolders(StandardizeAlbumsApplyInputData inputData);
}
