namespace MusicManager.UseCase.AddSongs;

public class AddSongsInputData
{
    private readonly string _libraryPath;
    private readonly List<string> _sources;
    private readonly string _preferredFormat;

    /// <param name="libraryPath">the folder the library is kept in (the one that is open)</param>
    /// <param name="sources">the files and folders the user picked</param>
    public AddSongsInputData(string libraryPath, IEnumerable<string> sources) : this(libraryPath, sources, PreferredFormat.Default)
    {
    }

    /// <param name="preferredFormat">the format to add when a song is there in several (see <see cref="PreferredFormat"/>)</param>
    public AddSongsInputData(string libraryPath, IEnumerable<string> sources, string preferredFormat)
    {
        _libraryPath = libraryPath;
        _sources = sources == null ? new List<string>() : new List<string>(sources);
        _preferredFormat = PreferredFormat.Normalize(preferredFormat);
    }

    public string LibraryPath => _libraryPath;

    public List<string> Sources => _sources;

    /// <summary>the extension (no dot) of the format to add when a song is there in several</summary>
    public string FormatPreference => _preferredFormat;
}
