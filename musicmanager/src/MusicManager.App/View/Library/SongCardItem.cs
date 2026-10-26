using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// One song of the grid as its card binds to it: the strings of the row, the one word the card says about the lyrics
/// (Word-synced, Line-synced or nothing), whether the song is selected and its cover. The grid raises the change
/// notifications when the status or
/// the selection moves; the card itself only reads. The cover is asked for when a card first binds it, so only the cards
/// that are on screen decode anything.
/// </summary>
public sealed class SongCardItem : INotifyPropertyChanged
{
    private readonly SongGrid _grid;

    internal SongCardItem(SongGrid grid, SongGridModel.Row row)
    {
        _grid = grid;
        Row = row;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public SongGridModel.Row Row { get; }

    /// <summary>the position among the songs the grid shows (the grid sets it whenever the shown songs change)</summary>
    public int Index { get; internal set; } = -1;

    public string Title => Row.Title;

    public string Artists => Row.Artists;

    private SongStatus Status => _grid.StatusOf(Row.Path);

    // ---------------------------------------------------------------- status

    /// <summary>what the card says about the lyrics: "Word-synced", "Line-synced" or nothing (no tool tip, no romaji counts)</summary>
    public string StatusText => Status.CardText;

    /// <summary>
    /// the check has not seen this song yet: the card shows a static muted bar on its status line instead of a text (the
    /// results of Convert mode, "Converted" and "Failed", are statuses of their own and are not this one)
    /// </summary>
    public bool IsStatusUnknown => ReferenceEquals(Status, SongStatus.Unknown);

    /// <summary>something is wrong (a failed conversion): the critical color</summary>
    public Visibility BadVisibility => Show(Status.GetTone() == SongStatus.Tone.Bad);

    /// <summary>anything else: the secondary text color (there is no green in this app); not while unchecked</summary>
    public Visibility NeutralVisibility
    {
        get
        {
            SongStatus status = Status;
            return Show(status.GetTone() != SongStatus.Tone.Bad && !ReferenceEquals(status, SongStatus.Unknown));
        }
    }

    public string AutomationName
    {
        get
        {
            string artists = Artists.Length == 0 ? "" : ", " + Artists;
            string status = StatusText.Length == 0 ? "" : ", " + StatusText;
            return Title + artists + status;
        }
    }

    // ---------------------------------------------------------------- selection

    /// <summary>
    /// Whether the song is selected. The card's check box is bound two ways: a change by the user (or by a screen reader)
    /// reaches the grid through the setter, which moves the selection of the model and then notifies every card.
    /// </summary>
    public bool IsSelected
    {
        get => _grid.IsSelected(Row.Path);
        set
        {
            if (value != IsSelected)
            {
                _grid.ToggleFromUser(this);
            }
        }
    }

    public Visibility SelectedVisibility => Show(IsSelected);

    // ---------------------------------------------------------------- cover

    /// <summary>the decoded cover, or null while it is still being decoded or when the song has none</summary>
    public ImageSource Cover => CoverImageCache.Get(Row.Path, Row.Music.CoverArt, OnCoverReady);

    /// <summary>the placeholder note shows until a cover is there</summary>
    public Visibility NoCoverVisibility => Show(CoverImageCache.Get(Row.Path, Row.Music.CoverArt, null) == null);

    private void OnCoverReady()
    {
        Raise(nameof(Cover));
        Raise(nameof(NoCoverVisibility));
    }

    // ---------------------------------------------------------------- notifications (the grid calls these)

    internal void NotifyStatus()
    {
        Raise(nameof(StatusText));
        Raise(nameof(IsStatusUnknown));
        Raise(nameof(BadVisibility));
        Raise(nameof(NeutralVisibility));
        Raise(nameof(AutomationName));
    }

    internal void NotifySelection()
    {
        Raise(nameof(IsSelected));
        Raise(nameof(SelectedVisibility));
    }

    private void Raise(string property)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
