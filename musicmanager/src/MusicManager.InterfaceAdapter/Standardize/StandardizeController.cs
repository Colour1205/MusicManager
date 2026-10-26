using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.InterfaceAdapter.Standardize;

public class StandardizeController
{
    private readonly IStandardizeLyricsInputBoundary _lyricsInteractor;
    private readonly IStandardizeArtistsInputBoundary _artistsInteractor;

    public StandardizeController(IStandardizeLyricsInputBoundary lyricsInteractor,
        IStandardizeArtistsInputBoundary artistsInteractor)
    {
        _lyricsInteractor = lyricsInteractor;
        _artistsInteractor = artistsInteractor;
    }

    /// <summary>
    /// dryRun = preview what would happen without writing, deleting or asking Claude anything
    /// </summary>
    /// <param name="romanize">also make a Latin-letter romaji line for every Korean/Japanese line, with Claude</param>
    /// <param name="apiKey">the key for romaji; blank means the ANTHROPIC_API_KEY environment variable</param>
    public void StandardizeLyrics(string path, bool dryRun, bool moveToTrash, bool romanize, string apiKey,
        string model)
    {
        StandardizeLyrics(path, dryRun, moveToTrash, romanize, apiKey, model, null);
    }

    /// <summary>the same for only the given songs (audio file paths); null = every song under the folder</summary>
    public void StandardizeLyrics(string path, bool dryRun, bool moveToTrash, bool romanize, string apiKey,
        string model, IEnumerable<string> audioPaths)
    {
        _lyricsInteractor.Execute(
            new StandardizeLyricsInputData(path, dryRun, moveToTrash, romanize, apiKey, model, audioPaths));
    }

    /// <summary>
    /// The preview of one song's window: a dry run (nothing is written or deleted) whose result lists the lines whose
    /// romaji changes, with the romaji made now (the saved answers, and Claude for the rest).
    /// </summary>
    public void PreviewSongLyrics(string path, bool moveToTrash, bool romanize, string apiKey, string model,
        IEnumerable<string> audioPaths)
    {
        _lyricsInteractor.Execute(
            new StandardizeLyricsInputData(path, true, moveToTrash, romanize, apiKey, model, audioPaths, true));
    }

    public void AnalyzeArtists(string path, string separator, bool useAi, string apiKey, string model)
    {
        AnalyzeArtists(path, separator, useAi, apiKey, model, null);
    }

    /// <summary>
    /// The names are worked out from the whole folder, but only the given songs (audio file paths) get their tags
    /// changed; null = every song.
    /// </summary>
    public void AnalyzeArtists(string path, string separator, bool useAi, string apiKey, string model,
        IEnumerable<string> audioPaths)
    {
        _artistsInteractor.Analyze(
            new StandardizeArtistsAnalyzeInputData(path, separator, useAi, apiKey, model, audioPaths));
    }

    /// <summary>call after editing a group's canonical name so the "what changes" preview is recomputed</summary>
    public void ReviewArtists(ArtistPlan plan)
    {
        _artistsInteractor.Review(new StandardizeArtistsApplyInputData(plan));
    }

    public void ApplyArtists(ArtistPlan plan)
    {
        _artistsInteractor.Apply(new StandardizeArtistsApplyInputData(plan));
    }

    public bool IsApiKeyAvailable => _artistsInteractor.IsApiKeyAvailable;
}
