namespace MusicManager.InterfaceAdapter.AddSongs;

/// <summary>
/// The state held by AddSongsViewModel. Every list is replaced (never mutated) when it changes, so a listener may safely
/// keep the reference it read when the event fired.
/// </summary>
public class AddSongsState
{
    /// <summary>the audio files to be standardized, by the path they have in the library or will have in it: the staged copies, and the picked songs that were there already</summary>
    public List<string> LibraryPaths { get; set; } = new List<string>();

    /// <summary>the staged songs (a part of <see cref="LibraryPaths"/>): not in the library until the add is confirmed</summary>
    public List<string> NewPaths { get; set; } = new List<string>();

    /// <summary>songs of the library that are given the lyric files of a copy that was left out (a part of <see cref="LibraryPaths"/>): only their lyrics are standardized</summary>
    public List<string> LyricsAddedTo { get; set; } = new List<string>();

    /// <summary>how many songs were staged to be copied into the library</summary>
    public int CopiedCount { get; set; }

    /// <summary>how many songs the confirmed add put into the library</summary>
    public int CommittedSongs { get; set; }

    /// <summary>null when the confirmed add went through; otherwise why the songs could not be put into the library (it is as it was)</summary>
    public string CommitProblem { get; set; }

    /// <summary>how many songs there were in what the user picked</summary>
    public int FoundCount { get; set; }

    /// <summary>what was done, in a sentence, and what could not be added and why</summary>
    public string Summary { get; set; } = "";

    /// <summary>how many songs could not be added</summary>
    public int ProblemCount { get; set; }

    public int ProgressDone { get; set; }

    public int ProgressTotal { get; set; }

    public string ErrorMessage { get; set; } = "";
}
