namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>every spelling of one album, and the single name chosen for them (the user may change it before applying)</summary>
public class AlbumGroup
{
    private readonly string _key; // stable identity key; what AlbumPlan.GroupKeyByRaw refers to
    private string _canonicalName;
    private readonly bool _lowConfidence;
    private readonly List<AlbumVariant> _variants; // the most used first
    private readonly int _songCount;

    public AlbumGroup(string key, string canonicalName, bool lowConfidence, List<AlbumVariant> variants, int songCount)
    {
        _key = key;
        _canonicalName = canonicalName;
        _lowConfidence = lowConfidence;
        _variants = variants;
        _songCount = songCount;
    }

    public string Key => _key;

    public string CanonicalName
    {
        get => _canonicalName;
        set => _canonicalName = value;
    }

    /// <summary>
    /// The name of the folder the user chose for this album's songs (a folder next to the one they are in), or null while
    /// the album is named by the program: then the songs join the folder the album lives in already, whatever it is called,
    /// or a folder named after the album. A name the user types or picks decides its folder too, so nothing happens that the
    /// user was not shown.
    /// </summary>
    public string FolderName { get; set; }

    /// <summary>true if the name is a guess: the card is marked "please check"</summary>
    public bool IsLowConfidence => _lowConfidence;

    public List<AlbumVariant> Variants => _variants;

    /// <summary>how many songs of the library belong to this album</summary>
    public int SongCount => _songCount;
}
