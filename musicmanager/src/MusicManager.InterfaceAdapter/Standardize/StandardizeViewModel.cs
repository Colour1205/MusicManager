namespace MusicManager.InterfaceAdapter.Standardize;

public class StandardizeViewModel : ViewModel<StandardizeState>
{
    // Java VIEW_NAME. "new": the base class has an instance property ViewName; this constant is reached as
    // StandardizeViewModel.ViewName (and the base one through a ViewModel<StandardizeState> reference).
    public new const string ViewName = "standardize";

    // property names the presenter fires; the view tells the events apart by these
    public const string Lyrics = "lyrics";
    public const string ArtistsAnalysis = "artistsAnalysis";
    public const string ArtistsApplied = "artistsApplied";
    public const string Progress = "progress";
    public const string Error = "error";

    public StandardizeViewModel() : base(ViewName)
    {
        State = new StandardizeState();
    }
}
