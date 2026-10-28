namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>the ALBUM tag to write to one audio file, and what it was (for the preview)</summary>
public class SongAlbumUpdate
{
    private readonly string _audioPath;
    private readonly string _album;
    private readonly string _before; // the tag now; null when the file has none

    public SongAlbumUpdate(string audioPath, string album, string before)
    {
        _audioPath = audioPath;
        _album = album;
        _before = before;
    }

    public string AudioPath => _audioPath;

    /// <summary>the value to write</summary>
    public string Album => _album;

    public string Before => _before;
}
