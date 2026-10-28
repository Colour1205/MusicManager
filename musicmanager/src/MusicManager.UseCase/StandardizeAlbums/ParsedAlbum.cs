namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>
/// what was found out about one raw album value of the tags (or one folder name that stands in for a missing tag): the
/// name of the album it belongs to. Every spelling of one album gets the same name.
/// </summary>
public class ParsedAlbum
{
    private readonly string _album;
    private readonly bool _lowConfidence;

    /// <param name="album">the album's standard name; blank when the value is not an album name at all ("Unknown", "Track 01")</param>
    /// <param name="lowConfidence">true if the source was unsure (a guessed title, spellings merged on a hunch)</param>
    public ParsedAlbum(string album, bool lowConfidence)
    {
        _album = album ?? "";
        _lowConfidence = lowConfidence;
    }

    public string Album => _album;

    public bool IsLowConfidence => _lowConfidence;

    /// <summary>true if the value is no album name, so the song is left as it is</summary>
    public bool IsNotAnAlbum => string.IsNullOrWhiteSpace(_album);
}
