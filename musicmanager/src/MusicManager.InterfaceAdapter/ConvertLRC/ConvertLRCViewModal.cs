namespace MusicManager.InterfaceAdapter.ConvertLRC;

public class ConvertLRCViewModal : ViewModel<ConvertLRCState>
{
    // Java VIEW_NAME. "new": the base class has an instance property ViewName; this constant is reached as
    // ConvertLRCViewModal.ViewName (and the base one through a ViewModel<ConvertLRCState> reference).
    public new const string ViewName = "convert lrc";

    public ConvertLRCViewModal() : base(ViewName)
    {
        State = new ConvertLRCState();
    }
}
