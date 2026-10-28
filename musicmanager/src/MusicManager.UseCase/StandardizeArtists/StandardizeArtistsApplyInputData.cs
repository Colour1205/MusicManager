namespace MusicManager.UseCase.StandardizeArtists;

public class StandardizeArtistsApplyInputData
{
    private readonly ArtistPlan _plan;
    private readonly IReadOnlyDictionary<string, string> _movedPaths;

    public StandardizeArtistsApplyInputData(ArtistPlan plan) : this(plan, null)
    {
    }

    /// <param name="movedPaths">
    /// songs whose files were moved to another folder since the plan was made (audio path before -> after); the steps that
    /// work on files look for such a song at its new path. null = nothing moved.
    /// </param>
    public StandardizeArtistsApplyInputData(ArtistPlan plan, IReadOnlyDictionary<string, string> movedPaths)
    {
        _plan = plan;
        _movedPaths = movedPaths;
    }

    public ArtistPlan Plan => _plan;

    /// <summary>where a song's file is now: the path the plan has for it, or the one it was moved to</summary>
    public string CurrentPath(string audioPath)
    {
        return _movedPaths != null && _movedPaths.TryGetValue(audioPath, out string moved) ? moved : audioPath;
    }
}
