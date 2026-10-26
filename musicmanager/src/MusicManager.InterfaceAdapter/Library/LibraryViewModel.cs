namespace MusicManager.InterfaceAdapter.Library;

public class LibraryViewModel : ViewModel<LibraryState>
{
    // Java VIEW_NAME. "new": the base class has an instance property ViewName; this constant is reached as
    // LibraryViewModel.ViewName (and the base one through a ViewModel<LibraryState> reference).
    public new const string ViewName = "library";

    // property names the presenter fires; the view tells the events apart by these
    public const string Songs = "songs";
    public const string Progress = "progress";
    public const string Error = "error";

    public LibraryViewModel() : base(ViewName)
    {
        State = new LibraryState();
    }
}
