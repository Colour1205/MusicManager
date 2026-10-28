using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.InterfaceAdapter.Standardize;

/// <summary>
/// The state held by StandardizeViewModel. Every list is replaced (never mutated) when it
/// changes, so a listener may safely keep the reference it read when the event fired.
/// </summary>
public class StandardizeState
{
    // lyrics
    public StandardizeLyricsOutputData LyricsResult { get; set; }

    /// <summary>song | what happens | details | deprecated files removed | level ("", "note" or "error")</summary>
    public List<string[]> LyricsRows { get; set; } = new List<string[]>();

    public string LyricsSummary { get; set; } = "";

    // artists
    public ArtistPlan ArtistPlan { get; set; }

    public List<SongArtistUpdate> ArtistUpdates { get; set; } = new List<SongArtistUpdate>();

    public string ArtistsSummary { get; set; } = "";

    public string ArtistsAppliedSummary { get; set; } = "";
    /// <summary>how many songs have files that are not named "Artist - Title" after their tags (they are renamed when the changes are applied)</summary>
    public int ArtistRenameCount { get; set; }

    /// <summary>the files that will be renamed to "Artist - Title": the song and the name (without the extension) it gets</summary>
    public List<SongRename> ArtistRenames { get; set; } = new List<SongRename>();

    /// <summary>what renaming the files to "Artist - Title" did, in a sentence ("" when it did nothing)</summary>
    public string ArtistsRenamedSummary { get; set; } = "";

    // albums
    public AlbumPlan AlbumPlan { get; set; }

    public List<SongAlbumUpdate> AlbumUpdates { get; set; } = new List<SongAlbumUpdate>();

    /// <summary>the songs that would be put in another folder (they are in a folder with another name than their album's)</summary>
    public List<AlbumFolderMove> AlbumFolderMoves { get; set; } = new List<AlbumFolderMove>();

    public string AlbumsSummary { get; set; } = "";

    /// <summary>what writing the ALBUM tags did, in a sentence</summary>
    public string AlbumsAppliedSummary { get; set; } = "";

    /// <summary>what putting the songs in their album folders did, in a sentence ("" when it did nothing)</summary>
    public string AlbumsMovedSummary { get; set; } = "";

    /// <summary>every song that was moved: its audio path before -> after</summary>
    public IReadOnlyDictionary<string, string> AlbumMovedPaths { get; set; } = new Dictionary<string, string>();

    // progress / errors
    public string ProgressMessage { get; set; } = "";

    public int ProgressDone { get; set; }

    public int ProgressTotal { get; set; }

    public string ErrorMessage { get; set; } = "";
}
