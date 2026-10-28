namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>the plan, and what it would change with the names as they stand</summary>
public class StandardizeAlbumsAnalysisOutputData
{
    private readonly AlbumPlan _plan;
    private readonly List<SongAlbumUpdate> _updates;
    private readonly List<AlbumFolderMove> _moves;

    public StandardizeAlbumsAnalysisOutputData(AlbumPlan plan, List<SongAlbumUpdate> updates, List<AlbumFolderMove> moves)
    {
        _plan = plan;
        _updates = updates;
        _moves = moves;
    }

    public AlbumPlan Plan => _plan;

    /// <summary>the ALBUM tags that would be written</summary>
    public List<SongAlbumUpdate> Updates => _updates;

    /// <summary>the songs that would move to another folder</summary>
    public List<AlbumFolderMove> Moves => _moves;
}

/// <summary>the outcome of writing the ALBUM tags</summary>
public class StandardizeAlbumsApplyOutputData
{
    private readonly int _updatedCount;
    private readonly int _unchangedCount;
    private readonly List<string> _failedPaths;

    public StandardizeAlbumsApplyOutputData(int updatedCount, int unchangedCount, List<string> failedPaths)
    {
        _updatedCount = updatedCount;
        _unchangedCount = unchangedCount;
        _failedPaths = failedPaths;
    }

    public int UpdatedCount => _updatedCount;

    public int UnchangedCount => _unchangedCount;

    public List<string> FailedPaths => _failedPaths;
}

/// <summary>the outcome of putting the songs in their album folders</summary>
public class StandardizeAlbumsMoveOutputData
{
    private readonly int _foldersRenamed;
    private readonly int _songsMoved;
    private readonly List<AlbumFolderMoveResult.Skipped> _skipped;
    private readonly Dictionary<string, string> _movedPaths;

    public StandardizeAlbumsMoveOutputData(int foldersRenamed, int songsMoved, List<AlbumFolderMoveResult.Skipped> skipped,
            Dictionary<string, string> movedPaths)
    {
        _foldersRenamed = foldersRenamed;
        _songsMoved = songsMoved;
        _skipped = skipped;
        _movedPaths = movedPaths;
    }

    /// <summary>how many folders were renamed as a whole</summary>
    public int FoldersRenamed => _foldersRenamed;

    /// <summary>how many songs are in another folder now (the songs of renamed folders too)</summary>
    public int SongsMoved => _songsMoved;

    public List<AlbumFolderMoveResult.Skipped> Skipped => _skipped;

    /// <summary>every moved song's audio path before -> after: the steps that come next find the files by it</summary>
    public Dictionary<string, string> MovedPaths => _movedPaths;
}
