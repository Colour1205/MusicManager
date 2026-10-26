namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>the tag values to write to one audio file; a null field means "leave that tag untouched"</summary>
public class SongArtistUpdate
{
    private readonly string _audioPath;
    private readonly string _artistJoined; // single-string ARTIST value (artists joined with the separator), or null
    private readonly List<string> _artistsList; // multi-valued ARTISTS values; empty list = remove the field; null = leave
    private readonly string _albumArtistJoined; // single-string ALBUMARTIST value, or null
    private readonly List<FieldChange> _changes; // human-readable before/after of everything that changes

    public SongArtistUpdate(string audioPath, string artistJoined, List<string> artistsList, string albumArtistJoined,
            List<FieldChange> changes)
    {
        _audioPath = audioPath;
        _artistJoined = artistJoined;
        _artistsList = artistsList;
        _albumArtistJoined = albumArtistJoined;
        _changes = changes;
    }

    public string AudioPath => _audioPath;

    public string ArtistJoined => _artistJoined;

    public List<string> ArtistsList => _artistsList;

    public string AlbumArtistJoined => _albumArtistJoined;

    public List<FieldChange> Changes => _changes;
}
