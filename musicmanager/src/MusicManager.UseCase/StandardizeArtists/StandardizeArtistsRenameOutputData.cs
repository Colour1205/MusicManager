namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>the outcome of renaming the songs' files to "Artist - Title" after their tags</summary>
public class StandardizeArtistsRenameOutputData
{
    private readonly int _renamedCount;
    private readonly int _alreadyNamedCount;
    private readonly List<SongRenameResult> _skipped; // not renamed, with the reason

    public StandardizeArtistsRenameOutputData(int renamedCount, int alreadyNamedCount, List<SongRenameResult> skipped)
    {
        _renamedCount = renamedCount;
        _alreadyNamedCount = alreadyNamedCount;
        _skipped = skipped;
    }

    public int RenamedCount => _renamedCount;

    public int AlreadyNamedCount => _alreadyNamedCount;

    public List<SongRenameResult> Skipped => _skipped;
}
