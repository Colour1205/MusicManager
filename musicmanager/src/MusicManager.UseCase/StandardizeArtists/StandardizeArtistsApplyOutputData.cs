namespace MusicManager.UseCase.StandardizeArtists;

public class StandardizeArtistsApplyOutputData
{
    private readonly int _updatedCount;
    private readonly int _unchangedCount;
    private readonly List<string> _failedPaths;

    public StandardizeArtistsApplyOutputData(int updatedCount, int unchangedCount, List<string> failedPaths)
    {
        _updatedCount = updatedCount;
        _unchangedCount = unchangedCount;
        _failedPaths = failedPaths;
    }

    public int UpdatedCount => _updatedCount;

    public int UnchangedCount => _unchangedCount;

    public List<string> FailedPaths => _failedPaths;
}
