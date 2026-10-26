using MusicManager.Entity;

namespace MusicManager.UseCase.OpenLibrary;

public interface IOpenLibraryDataAccess
{
    /// <summary>
    /// every audio file under path (recursively), in folder and file name order, with its title, artists and album
    /// from the tag and its cover art shrunk to a small thumbnail (a PNG of at most about 96 x 96 pixels, or null),
    /// so that thousands of songs fit in memory. A file whose tag cannot be read is still listed, with its file name
    /// as the title.
    /// </summary>
    /// <param name="onlyAudioPaths">read only these files (audio paths as returned by an earlier call); null = all of them</param>
    /// <param name="progress">told (done, total) as the files are read, possibly from several threads; may be null</param>
    List<Music> ListMusics(string path, HashSet<string> onlyAudioPaths, Action<int, int> progress);
}
