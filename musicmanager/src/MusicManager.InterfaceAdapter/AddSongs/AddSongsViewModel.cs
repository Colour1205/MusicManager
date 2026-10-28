namespace MusicManager.InterfaceAdapter.AddSongs;

public class AddSongsViewModel : ViewModel<AddSongsState>
{
    // Java VIEW_NAME. "new": the base class has an instance property ViewName; this constant is reached as
    // AddSongsViewModel.ViewName (and the base one through a ViewModel<AddSongsState> reference).
    public new const string ViewName = "addSongs";

    // property names the presenter fires; the view tells the events apart by these
    public const string Added = "added";
    public const string Committed = "committed";
    public const string Progress = "progress";
    public const string Error = "error";

    public AddSongsViewModel() : base(ViewName)
    {
        State = new AddSongsState();
    }
}
