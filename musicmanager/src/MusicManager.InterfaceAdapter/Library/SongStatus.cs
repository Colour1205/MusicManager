namespace MusicManager.InterfaceAdapter.Library;

/// <summary>
/// what the library shows of one song's lyrics: a short line for its card, a category for the filter chips, and
/// longer lines for the song window. Made by <see cref="LibraryStatusMapper"/> from the preview of what Process would do.
/// </summary>
public sealed class SongStatus
{
    /// <summary>which filter chip the song belongs to</summary>
    public enum Category
    {
        /// <summary>something would change (a .lrc to build, deprecated files to delete, romaji to add or replace)</summary>
        NeedsWork,
        /// <summary>one word-synced .lrc and nothing left to do</summary>
        Standard,
        /// <summary>only a line-synced .lrc, with no word-synced source, and nothing left to do</summary>
        LineSynced,
        /// <summary>no lyric file at all</summary>
        NoLyrics,
        /// <summary>the lyric files could not be used</summary>
        Problem,
        /// <summary>not checked yet</summary>
        Unknown
    }

    /// <summary>how the card colors the line</summary>
    public enum Tone
    {
        /// <summary>will change: the accent color</summary>
        Active,
        /// <summary>fine as it is: the "success" blue</summary>
        Good,
        /// <summary>nothing to say: grey</summary>
        Muted,
        /// <summary>something is wrong: red</summary>
        Bad
    }

    public static readonly SongStatus Unknown = new SongStatus(Category.Unknown, Tone.Muted, "Checking lyrics...",
        new List<string>(), 0, 0, 0);

    private readonly Category _category;
    private readonly Tone _tone;
    private readonly string _headline;
    private readonly string _cardText;
    private readonly List<string> _details;
    // the three kinds of line are told apart, none counts inside another: add + replace is what Process writes
    private readonly int _romajiToAdd; // Korean/Japanese lines with no romaji yet that get a romaji line
    private readonly int _romajiToReplace; // lines whose old romaji (Chinese characters, say) is replaced by Latin letters
    private readonly int _romajiPresent; // lines that already have Latin romaji and keep it

    public SongStatus(Category category, Tone tone, string headline, List<string> details, int romajiToAdd,
        int romajiToReplace, int romajiPresent, string cardText = "")
    {
        _category = category;
        _tone = tone;
        _headline = headline;
        _cardText = cardText ?? "";
        _details = details;
        _romajiToAdd = romajiToAdd;
        _romajiToReplace = romajiToReplace;
        _romajiPresent = romajiPresent;
    }

    /// <summary>
    /// Java getCategory(). A method, not a property named Category: a C# member cannot share its name with the nested
    /// enum <see cref="Category"/> (the same rule as Lyric.GetSyncType()).
    /// </summary>
    public Category GetCategory() => _category;

    /// <summary>Java getTone(). A method for the same reason as <see cref="GetCategory"/>.</summary>
    public Tone GetTone() => _tone;

    /// <summary>one short line for the song's card, e.g. "Will build the .lrc from .ttml and delete 2 old files"</summary>
    public string Headline => _headline;

    /// <summary>
    /// what the song's card says, and nothing more: "Word-synced", "Line-synced", or empty when there is nothing to say
    /// (no lyrics, a problem, not checked yet). What Process would do is in the filter chips and the song window.
    /// </summary>
    public string CardText => _cardText;

    /// <summary>the lines of the song window: what is there and what Process would do, one fact per line</summary>
    public List<string> Details => _details;

    public int RomajiToAdd => _romajiToAdd;

    public int RomajiToReplace => _romajiToReplace;

    public int RomajiPresent => _romajiPresent;
}
