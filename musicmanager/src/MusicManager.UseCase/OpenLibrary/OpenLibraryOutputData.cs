using MusicManager.Entity;

namespace MusicManager.UseCase.OpenLibrary;

public class OpenLibraryOutputData
{
    public OpenLibraryOutputData(string path, List<Music> songs) : this(path, songs, null)
    {
    }

    public OpenLibraryOutputData(string path, List<Music> songs, HashSet<string> onlyAudioPaths)
    {
        Path = path;
        Songs = songs;
        OnlyAudioPaths = onlyAudioPaths;
    }

    public string Path { get; }

    /// <summary>the songs that were read, in folder and file name order; a song's cover art is a small thumbnail</summary>
    public List<Music> Songs { get; }

    /// <summary>non-null when only some songs were read again (a refresh): the list then holds just those songs</summary>
    public HashSet<string> OnlyAudioPaths { get; }
}
