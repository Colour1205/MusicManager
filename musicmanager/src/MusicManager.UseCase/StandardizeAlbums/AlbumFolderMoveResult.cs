namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>what became of one <see cref="AlbumFolderMove"/></summary>
public class AlbumFolderMoveResult
{
    /// <summary>a song that stayed where it was, and why</summary>
    public sealed record Skipped(string AudioPath, string Detail);

    private readonly Dictionary<string, string> _moved; // audio path before -> after
    private readonly List<Skipped> _skipped;
    private readonly bool _folderRenamed;

    public AlbumFolderMoveResult(Dictionary<string, string> moved, List<Skipped> skipped, bool folderRenamed)
    {
        _moved = moved;
        _skipped = skipped;
        _folderRenamed = folderRenamed;
    }

    public Dictionary<string, string> Moved => _moved;

    public List<Skipped> NotMoved => _skipped;

    /// <summary>true if the folder itself was renamed (otherwise the songs were moved one by one)</summary>
    public bool IsFolderRenamed => _folderRenamed;
}
