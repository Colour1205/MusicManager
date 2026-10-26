namespace MusicManager.View.Library;

/// <summary>
/// The dialogs of a Process run, behind an interface so that a test can answer them without a window appearing.
/// The parent is whatever the window hands out as its frame (<see cref="ProcessFlow.IHost.Frame"/>); this layer never
/// looks at it. The real dialogs are made by the WinUI app.
/// </summary>
public interface IProcessPrompts
{
    /// <summary>A question with OK and Cancel; true for OK.</summary>
    bool Confirm(object parent, string title, string message);

    void Info(object parent, string title, string message);

    void Warn(object parent, string title, string message);

    void Error(object parent, string title, string message);
}
