namespace MusicManager.UseCase.OpenLibrary;

public class OpenLibraryInputData
{
    /// <summary>every song under the folder</summary>
    public OpenLibraryInputData(string path) : this(path, null)
    {
    }

    /// <param name="path">the music folder to open</param>
    /// <param name="onlyAudioPaths">
    /// read only these songs (audio file paths as an earlier listing returned them), to refresh a few songs after they
    /// were changed; null = every song under the folder. The set is copied.
    /// </param>
    public OpenLibraryInputData(string path, IEnumerable<string> onlyAudioPaths)
    {
        Path = path;
        OnlyAudioPaths = onlyAudioPaths == null ? null : new HashSet<string>(onlyAudioPaths);
    }

    /// <summary>the music folder to open</summary>
    public string Path { get; }

    /// <summary>the songs to read, or null for every song under the folder</summary>
    public HashSet<string> OnlyAudioPaths { get; }
}
