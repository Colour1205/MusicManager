namespace MusicManager.UseCase.AddSongs;

/// <summary>
/// Names of library folders that say nothing about an album: the folder Add puts loose songs in, and the folders people
/// keep downloads in. A song whose folder is one of these has no album to take from its folder.
/// </summary>
public static class LibraryFolderNames
{
    /// <summary>the folder of songs that were picked from a folder with no name worth keeping (the desktop, the downloads)</summary>
    public const string LooseSongs = "New songs";

    private static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        LooseSongs, "Downloads", "Desktop", "Music", "Documents", "Temp", "tmp"
    };

    /// <summary>true if this folder name tells nothing about an album</summary>
    public static bool IsGeneric(string folderName)
    {
        return folderName != null && Generic.Contains(folderName.Trim());
    }
}
