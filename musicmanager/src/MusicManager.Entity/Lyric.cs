namespace MusicManager.Entity;

/// <summary>
/// A parsed lyric file: its lines, how they are synced, the LRC header tags, the offset and the problems met while
/// parsing.
/// </summary>
public class Lyric
{
    public enum SyncType
    {
        LineSync, WordSync, NoSync
    }

    private readonly SyncType _syncType;
    private List<LyricLine> _lines;
    private string _path; // path of the source lyric file, used to save converted output next to it

    // LRC header tags in file order, lower-cased keys (ti, ar, al, by, tool, ...); offset is kept separately
    private readonly OrderedDictionary<string, string> _metadata = new OrderedDictionary<string, string>();
    private long _offsetMs; // the [offset:] header, in milliseconds (0 when absent)
    // non-fatal problems met while parsing (repaired values, dropped garbage); surfaced to the user
    private readonly List<string> _warnings = new List<string>();

    public Lyric(SyncType syncType, List<LyricLine> lines, string path)
    {
        _syncType = syncType;
        _lines = lines;
        _path = path;
    }

    /// <summary>
    /// Java getSyncType(). A method, not a property named SyncType: a C# member cannot share its name with the nested
    /// enum <see cref="SyncType"/>.
    /// </summary>
    public SyncType GetSyncType() => _syncType;

    public List<LyricLine> Lines
    {
        get => _lines;
        set => _lines = value;
    }

    public string Path
    {
        get => _path;
        set => _path = value;
    }

    /// <summary>The LRC header tags in file order (insertion order is kept, as in the Java LinkedHashMap).</summary>
    public OrderedDictionary<string, string> Metadata => _metadata;

    public long OffsetMs
    {
        get => _offsetMs;
        set => _offsetMs = value;
    }

    public List<string> Warnings => _warnings;

    public void AddWarning(string warning)
    {
        _warnings.Add(warning);
    }
}
