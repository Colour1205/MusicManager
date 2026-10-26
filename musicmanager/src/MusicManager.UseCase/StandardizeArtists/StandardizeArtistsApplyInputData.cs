namespace MusicManager.UseCase.StandardizeArtists;

public class StandardizeArtistsApplyInputData
{
    private readonly ArtistPlan _plan;

    public StandardizeArtistsApplyInputData(ArtistPlan plan)
    {
        _plan = plan;
    }

    public ArtistPlan Plan => _plan;
}
