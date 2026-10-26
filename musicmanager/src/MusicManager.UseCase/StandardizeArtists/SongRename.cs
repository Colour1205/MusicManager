namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>a song whose files are named differently from its tags: the name they should have (without the extension)</summary>
public class SongRename
{
    private readonly string _audioPath;
    private readonly string _newBaseName;

    public SongRename(string audioPath, string newBaseName)
    {
        _audioPath = audioPath;
        _newBaseName = newBaseName;
    }

    public string AudioPath => _audioPath;

    /// <summary>"Artist - Title" (see <see cref="StandardFileName"/>), without the extension</summary>
    public string NewBaseName => _newBaseName;
}
