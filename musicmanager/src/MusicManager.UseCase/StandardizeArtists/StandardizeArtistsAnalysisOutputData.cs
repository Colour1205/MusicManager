namespace MusicManager.UseCase.StandardizeArtists;

public class StandardizeArtistsAnalysisOutputData
{
    private readonly ArtistPlan _plan;
    private readonly List<SongArtistUpdate> _updates; // what would change, with the plan as it currently stands

    public StandardizeArtistsAnalysisOutputData(ArtistPlan plan, List<SongArtistUpdate> updates)
    {
        _plan = plan;
        _updates = updates;
    }

    public ArtistPlan Plan => _plan;

    public List<SongArtistUpdate> Updates => _updates;
}
