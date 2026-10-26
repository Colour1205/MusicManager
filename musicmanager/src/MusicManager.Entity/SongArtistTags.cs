namespace MusicManager.Entity;

/// <summary>
/// The artist-related tag values of one audio file, exactly as stored (every value of a multi-valued field is kept
/// separate). Used to standardize artist names across a library.
/// </summary>
public class SongArtistTags
{
    private readonly string _path;
    private readonly List<string> _artist; // ARTIST values
    private readonly List<string> _albumArtist; // ALBUMARTIST values
    private readonly List<string> _artists; // ARTISTS values (multi-valued "all individual artists" field)
    private readonly string _title; // TITLE value, or null when the song has none (the file name is made of the artists and this)

    public SongArtistTags(string path, List<string> artist, List<string> albumArtist, List<string> artists)
        : this(path, artist, albumArtist, artists, null)
    {
    }

    public SongArtistTags(string path, List<string> artist, List<string> albumArtist, List<string> artists, string title)
    {
        _path = path;
        _artist = artist == null ? new List<string>() : new List<string>(artist);
        _albumArtist = albumArtist == null ? new List<string>() : new List<string>(albumArtist);
        _artists = artists == null ? new List<string>() : new List<string>(artists);
        _title = title;
    }

    public string Path => _path;

    public string Title => _title;

    public List<string> Artist => _artist;

    public List<string> AlbumArtist => _albumArtist;

    public List<string> Artists => _artists;
}
