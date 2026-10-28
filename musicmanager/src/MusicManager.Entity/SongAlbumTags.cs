namespace MusicManager.Entity;

/// <summary>
/// The tags of one audio file that say which album it belongs to, exactly as stored, and where the file is. Used to
/// standardize album names across a library (and to put the songs of an album in one folder).
/// </summary>
public class SongAlbumTags
{
    private readonly string _path;
    private readonly string _album; // the first ALBUM value, or null when the file has none
    private readonly List<string> _artist; // ARTIST values (what the album name is said to belong to: a hint for the AI)
    private readonly List<string> _albumArtist; // ALBUMARTIST values
    private readonly string _title; // TITLE value, or null

    public SongAlbumTags(string path, string album, List<string> artist, List<string> albumArtist, string title)
    {
        _path = path;
        _album = album;
        _artist = artist == null ? new List<string>() : new List<string>(artist);
        _albumArtist = albumArtist == null ? new List<string>() : new List<string>(albumArtist);
        _title = title;
    }

    public string Path => _path;

    /// <summary>the ALBUM tag as stored, or null when the file has no (non-blank) album</summary>
    public string Album => _album;

    public List<string> Artist => _artist;

    public List<string> AlbumArtist => _albumArtist;

    public string Title => _title;
}
