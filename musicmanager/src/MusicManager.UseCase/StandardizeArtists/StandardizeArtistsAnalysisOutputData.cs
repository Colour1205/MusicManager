namespace MusicManager.UseCase.StandardizeArtists;

public class StandardizeArtistsAnalysisOutputData
{
    private readonly ArtistPlan _plan;
    private readonly List<SongArtistUpdate> _updates; // what would change, with the plan as it currently stands
    private readonly List<SongRename> _renames; // the songs whose files are not named "Artist - Title" after their tags

    public StandardizeArtistsAnalysisOutputData(ArtistPlan plan, List<SongArtistUpdate> updates)
        : this(plan, updates, new List<SongRename>())
    {
    }

    public StandardizeArtistsAnalysisOutputData(ArtistPlan plan, List<SongArtistUpdate> updates, List<SongRename> renames)
    {
        _plan = plan;
        _updates = updates;
        _renames = renames;
    }

    public ArtistPlan Plan => _plan;

    public List<SongArtistUpdate> Updates => _updates;

    public List<SongRename> Renames => _renames;
}
