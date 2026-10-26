using MusicManager.Entity;

namespace MusicManager.InterfaceAdapter.Library;

/// <summary>what the library page shows of an opened folder. Lists are replaced, never changed, so a listener can keep them.</summary>
public class LibraryState
{
    public string Path { get; set; } = "";

    public List<Music> Songs { get; set; } = new List<Music>();

    /// <summary>
    /// null when the last result is the whole folder; otherwise the result only re-reads these songs (merge them in)
    /// </summary>
    public HashSet<string> RefreshedAudioPaths { get; set; } // non-null when Songs holds only a refresh of these songs

    public int ProgressDone { get; set; }

    public int ProgressTotal { get; set; }

    public string ErrorMessage { get; set; } = "";
}
