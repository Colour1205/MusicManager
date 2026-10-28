using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>
/// the result of analysing a library's albums: which spellings were grouped into which album. Editing a group's name and
/// running <see cref="AlbumStandardizer.ComputeUpdates"/> again is how the user's corrections take effect.
/// </summary>
public class AlbumPlan
{
    private readonly string _root; // the folder that was opened: songs directly in it are not moved into an album folder
    private readonly bool _usedAi;
    private readonly List<AlbumGroup> _groups;
    private readonly IDictionary<string, string> _groupKeyByRaw; // normalized raw value -> key of its group
    private readonly List<SongAlbumTags> _songs;
    private readonly List<string> _warnings;
    private readonly HashSet<string> _scope; // audio paths of the songs that may change; null = every song

    /// <param name="scope">
    /// the songs whose tags and folders may change; the names (groups) always come from every song in the folder, so a
    /// few songs processed on their own end up named like the rest of the library. null = every song. The paths are copied.
    /// </param>
    public AlbumPlan(string root, bool usedAi, List<AlbumGroup> groups, IDictionary<string, string> groupKeyByRaw,
            List<SongAlbumTags> songs, List<string> warnings, IEnumerable<string> scope)
    {
        _root = root;
        _usedAi = usedAi;
        _groups = groups;
        _groupKeyByRaw = groupKeyByRaw;
        _songs = songs;
        _warnings = warnings;
        _scope = scope == null ? null : new HashSet<string>(scope);
    }

    public string Root => _root;

    public bool IsUsedAi => _usedAi;

    public List<AlbumGroup> Groups => _groups;

    public IDictionary<string, string> GroupKeyByRaw => _groupKeyByRaw;

    public List<SongAlbumTags> Songs => _songs;

    public List<string> Warnings => _warnings;

    /// <summary>the songs that may change (audio paths), or null when every song may</summary>
    public HashSet<string> Scope => _scope;

    public bool IsInScope(SongAlbumTags song)
    {
        return _scope == null || _scope.Contains(song.Path);
    }

    /// <summary>how many of the plan's songs may change</summary>
    public int ScopedSongCount
    {
        get
        {
            if (_scope == null)
            {
                return _songs.Count;
            }
            int count = 0;
            foreach (SongAlbumTags song in _songs)
            {
                if (_scope.Contains(song.Path))
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>the group a song belongs to, or null when it has no album to name (no tag and no folder to take it from, or no album name at all)</summary>
    public AlbumGroup GroupOf(SongAlbumTags song)
    {
        string raw = AlbumStandardizer.RawOf(song, _root);
        if (raw == null || !_groupKeyByRaw.TryGetValue(raw, out string key))
        {
            return null;
        }
        foreach (AlbumGroup group in _groups)
        {
            if (group.Key == key)
            {
                return group;
            }
        }
        return null;
    }
}
