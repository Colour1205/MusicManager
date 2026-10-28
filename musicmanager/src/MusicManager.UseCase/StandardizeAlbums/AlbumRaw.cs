namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>
/// one raw album value of the library as the AI sees it: how many songs use it and a little context (who sings them and
/// where they are kept), which is what tells the "Greatest Hits" of one artist from another's and a ripped folder's name
/// from a real title
/// </summary>
public class AlbumRaw
{
    private readonly int _count;
    private readonly bool _fromFolder;
    private readonly List<string> _artists;
    private readonly List<string> _folders;
    private readonly List<string> _titles;

    public AlbumRaw(int count, bool fromFolder, List<string> artists, List<string> folders)
        : this(count, fromFolder, artists, folders, null)
    {
    }

    public AlbumRaw(int count, bool fromFolder, List<string> artists, List<string> folders, List<string> titles)
    {
        _count = count;
        _fromFolder = fromFolder;
        _artists = artists ?? new List<string>();
        _folders = folders ?? new List<string>();
        _titles = titles ?? new List<string>();
    }

    /// <summary>how many songs use this exact value</summary>
    public int Count => _count;

    /// <summary>true if no song with this value has an ALBUM tag: the value is the name of the folder the songs are in</summary>
    public bool IsFromFolder => _fromFolder;

    /// <summary>the artists of the songs, the commonest first</summary>
    public List<string> Artists => _artists;

    /// <summary>the names of the folders the songs are in, the commonest first</summary>
    public List<string> Folders => _folders;

    /// <summary>a few of the songs' titles: which songs these are can tell what album they belong on</summary>
    public List<string> Titles => _titles;
}
