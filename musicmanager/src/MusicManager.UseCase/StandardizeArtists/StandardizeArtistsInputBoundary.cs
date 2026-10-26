namespace MusicManager.UseCase.StandardizeArtists;

public interface IStandardizeArtistsInputBoundary
{
    /// <summary>reads the library's artist tags and builds a plan (nothing is written)</summary>
    void Analyze(StandardizeArtistsAnalyzeInputData inputData);

    /// <summary>recomputes (and presents) what would change after the user edited a name in the plan; writes nothing</summary>
    void Review(StandardizeArtistsApplyInputData inputData);

    /// <summary>writes the (possibly user-edited) plan to the audio files' tags</summary>
    void Apply(StandardizeArtistsApplyInputData inputData);

    /// <summary>
    /// renames the files of the plan's songs to "Artist - Title" after the tags they have now (so after Apply, and after
    /// the lyrics, which find the files by their old names); a song whose files already have that name is left alone
    /// </summary>
    void RenameFiles(StandardizeArtistsApplyInputData inputData);

    /// <summary>whether an API key can be found without the user typing one</summary>
    bool IsApiKeyAvailable { get; }
}
