using MusicManager.Common;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>
/// The name of the folder an album's songs are kept in: the album's name, fit to be a folder name. Windows does not allow
/// what a file name does not (see <see cref="StandardFileName"/>), a folder name must not end in a dot or a space, and a
/// few words are reserved for devices. Case is kept: "Love poem" stays "Love poem".
/// </summary>
public static class AlbumFolderName
{
    private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>the folder name for this album name; empty when nothing is left of it</summary>
    public static string Clean(string album)
    {
        string name = StandardFileName.Clean(album);
        // a dot or space at the end is dropped by Windows, so two names that differ only by it would be one folder
        name = JavaString.Trim(name.TrimEnd('.', ' '));
        if (name.Length == 0)
        {
            return "";
        }
        return Reserved.Contains(name) ? name + "_" : name;
    }
}
