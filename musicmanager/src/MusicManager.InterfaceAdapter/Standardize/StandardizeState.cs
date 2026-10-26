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

    // progress / errors
    public string ProgressMessage { get; set; } = "";

    public int ProgressDone { get; set; }

    public int ProgressTotal { get; set; }

    public string ErrorMessage { get; set; } = "";
}
