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

    public SongArtistTags(string path, List<string> artist, List<string> albumArtist, List<string> artists)
    {
        _path = path;
        _artist = artist == null ? new List<string>() : new List<string>(artist);
        _albumArtist = albumArtist == null ? new List<string>() : new List<string>(albumArtist);
        _artists = artists == null ? new List<string>() : new List<string>(artists);
    }

    public string Path => _path;

    public List<string> Artist => _artist;

    public List<string> AlbumArtist => _albumArtist;

    public List<string> Artists => _artists;
}
