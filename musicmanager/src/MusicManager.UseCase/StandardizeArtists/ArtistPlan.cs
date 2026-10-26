using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>
/// the result of analysing a library's artist tags: which spellings were grouped
/// into which artist, and how each raw tag value splits into artists. Editing a
/// group's canonical name and re-running ArtistStandardizer.ComputeUpdates is how
/// the user's corrections take effect.
/// </summary>
public class ArtistPlan
{
    private readonly string _separator; // used to join several artists into the single-string ARTIST/ALBUMARTIST tag
    private readonly bool _usedAi;
    private readonly List<ArtistGroup> _groups;
    private readonly IDictionary<string, List<string>> _groupKeysByRaw; // raw tag value -> ordered group keys of the artists in it
    private readonly List<SongArtistTags> _songs;
    private readonly List<string> _warnings;
    private readonly HashSet<string> _scope; // audio paths of the songs whose tags may change; null = every song

    public ArtistPlan(string separator, bool usedAi, List<ArtistGroup> groups,
            IDictionary<string, List<string>> groupKeysByRaw, List<SongArtistTags> songs, List<string> warnings)
        : this(separator, usedAi, groups, groupKeysByRaw, songs, warnings, null)
    {
    }

    /// <param name="scope">
    /// the songs whose tags may change; the names (groups) always come from every song in the folder, so
    /// a few songs processed on their own end up with the same names as the rest of the library.
    /// null = every song. The paths are copied.
    /// </param>
    public ArtistPlan(string separator, bool usedAi, List<ArtistGroup> groups,
            IDictionary<string, List<string>> groupKeysByRaw, List<SongArtistTags> songs, List<string> warnings,
            IEnumerable<string> scope)
    {
        _separator = separator;
        _usedAi = usedAi;
        _groups = groups;
        _groupKeysByRaw = groupKeysByRaw;
        _songs = songs;
        _warnings = warnings;
        _scope = scope == null ? null : new HashSet<string>(scope);
    }

    /// <summary>the songs whose tags may change (audio paths), or null when every song may</summary>
    public HashSet<string> Scope => _scope;

    /// <summary>true if this song's tags may change: it is in the scope, or there is no scope</summary>
    public bool IsInScope(SongArtistTags song)
    {
        return _scope == null || _scope.Contains(song.Path);
    }

    /// <summary>how many of the plan's songs may change: the ones in the scope, or all of them without a scope</summary>
    public int ScopedSongCount
    {
        get
        {
            if (_scope == null)
            {
                return _songs.Count;
            }
            int count = 0;
            foreach (SongArtistTags song in _songs)
            {
                if (song != null && _scope.Contains(song.Path))
                {
                    count++;
                }
            }
            return count;
        }
    }

    public string Separator => _separator;

    public bool IsUsedAi => _usedAi;

    public List<ArtistGroup> Groups => _groups;

    public IDictionary<string, List<string>> GroupKeysByRaw => _groupKeysByRaw;

    public List<SongArtistTags> Songs => _songs;

    public List<string> Warnings => _warnings;
}
