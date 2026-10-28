using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.InterfaceAdapter.Standardize;

public class StandardizeController
{
    private readonly IStandardizeLyricsInputBoundary _lyricsInteractor;
    private readonly IStandardizeArtistsInputBoundary _artistsInteractor;
    private readonly IStandardizeAlbumsInputBoundary _albumsInteractor;

    public StandardizeController(IStandardizeLyricsInputBoundary lyricsInteractor,
        IStandardizeArtistsInputBoundary artistsInteractor)
        : this(lyricsInteractor, artistsInteractor, null)
    {
    }

    public StandardizeController(IStandardizeLyricsInputBoundary lyricsInteractor,
        IStandardizeArtistsInputBoundary artistsInteractor, IStandardizeAlbumsInputBoundary albumsInteractor)
    {
        _lyricsInteractor = lyricsInteractor;
        _artistsInteractor = artistsInteractor;
        _albumsInteractor = albumsInteractor;
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

    /// <summary>
    /// renames the plan's songs' files to "Artist - Title" after their tags; call it last (after ApplyArtists and the
    /// lyrics), because the lyrics are found by the old file names
    /// </summary>
    public void RenameFiles(ArtistPlan plan)
    {
        RenameFiles(plan, null);
    }

    /// <summary>
    /// the same for songs that were moved to album folders since the plan was made: movedPaths (audio path before ->
    /// after, from the albums' MovedPaths) says where their files are now
    /// </summary>
    public void RenameFiles(ArtistPlan plan, IReadOnlyDictionary<string, string> movedPaths)
    {
        _artistsInteractor.RenameFiles(new StandardizeArtistsApplyInputData(plan, movedPaths));
    }

    // ---- albums ----

    /// <summary>
    /// Works out which album each song belongs to, from the whole folder; only the given songs (audio file paths) get their
    /// tags and folders changed, null = every song.
    /// </summary>
    public void AnalyzeAlbums(string path, bool useAi, string apiKey, string model, IEnumerable<string> audioPaths)
    {
        _albumsInteractor.Analyze(new StandardizeAlbumsAnalyzeInputData(path, useAi, apiKey, model, audioPaths));
    }

    /// <summary>call after editing an album's name so the "what changes" preview is recomputed</summary>
    public void ReviewAlbums(AlbumPlan plan)
    {
        _albumsInteractor.Review(new StandardizeAlbumsApplyInputData(plan));
    }

    public void ApplyAlbums(AlbumPlan plan)
    {
        _albumsInteractor.Apply(new StandardizeAlbumsApplyInputData(plan));
    }

    /// <summary>
    /// puts the plan's songs in folders named after their albums; call it after ApplyAlbums and the lyrics (the lyrics are
    /// found by the old paths) and before RenameFiles, which needs to be told where the files went
    /// </summary>
    public void MoveAlbumFolders(AlbumPlan plan)
    {
        _albumsInteractor.MoveFolders(new StandardizeAlbumsApplyInputData(plan));
    }

    public bool IsApiKeyAvailable => _artistsInteractor.IsApiKeyAvailable;
}
