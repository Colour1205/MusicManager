namespace MusicManager.UseCase.StandardizeAlbums;

public class StandardizeAlbumsApplyInputData
{
    private readonly AlbumPlan _plan;

    public StandardizeAlbumsApplyInputData(AlbumPlan plan)
    {
        _plan = plan;
    }

    public AlbumPlan Plan => _plan;
}
