namespace MusicManager.App;

/// <summary>
/// A page that wants to answer the title bar's Back button itself (for example to ask before it throws work away).
/// <see cref="OnBackRequested"/> returns true if the page took care of it (it then leaves by itself, e.g. through
/// <c>ProcessFlow.IHost.ShowLibrary()</c>), false to let the window go back. A page without this interface is simply
/// left by Back.
/// </summary>
public interface IBackHandler
{
    bool OnBackRequested();
}
