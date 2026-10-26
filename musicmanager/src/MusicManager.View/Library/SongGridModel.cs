using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;

namespace MusicManager.View.Library;

/// <summary>
/// What the song grid shows, without any UI: all songs, the ones the filter lets through (in order), the selected
/// audio paths and the status of each song. Every change that can alter the selection says so in its result so the grid
/// can tell its listeners exactly once. Used from the UI thread only.
/// </summary>
public sealed class SongGridModel
{
    /// <summary>One song with the strings its card shows, made once instead of on every paint.</summary>
    public sealed class Row
    {
        public Music Music { get; }
        public string Path { get; }
        public string Title { get; }
        public string Artists { get; }

        public Row(Music music)
        {
            Music = music;
            Path = music.Path ?? "";
            Title = TitleOf(music, Path);
            Artists = music.Artists == null ? "" : string.Join(", ", music.Artists);
        }
    }

    private List<Row> _all = new();
    private Dictionary<string, int> _indexByPath = new();
    private List<Row> _shown = new();
    private IReadOnlyList<Music> _shownSongs = new List<Music>().AsReadOnly();
    private readonly HashSet<string> _selected = new();
    private readonly Dictionary<string, SongStatus> _statuses = new();
    private Func<Music, bool> _filter;

    // ---- songs ----

    /// <summary>Replaces all songs. The selection keeps the songs that are still there.</summary>
    /// <returns>True when the selection changed.</returns>
    public bool SetSongs(IEnumerable<Music> songs)
    {
        var rows = new List<Row>();
        var index = new Dictionary<string, int>();
        if (songs != null)
        {
            foreach (Music music in songs)
            {
                if (music == null)
                {
                    continue;
                }
                var row = new Row(music);
                index.TryAdd(row.Path, rows.Count);
                rows.Add(row);
            }
        }
        _all = rows;
        _indexByPath = index;
        bool selectionChanged = _selected.RemoveWhere(path => !index.ContainsKey(path)) > 0;
        Refilter();
        return selectionChanged;
    }

    /// <summary>Replaces the songs that have the same path as a refreshed one, keeping order and selection.</summary>
    public void MergeSongs(IEnumerable<Music> refreshed)
    {
        if (refreshed == null)
        {
            return;
        }
        var refreshedList = refreshed.ToList();
        if (refreshedList.Count == 0)
        {
            return;
        }
        var rows = new List<Row>(_all);
        foreach (Music music in refreshedList)
        {
            if (music == null || music.Path == null)
            {
                continue;
            }
            if (_indexByPath.TryGetValue(music.Path, out int position))
            {
                rows[position] = new Row(music);
            }
        }
        _all = rows;
        Refilter();
    }

    public int SongCount()
    {
        return _all.Count;
    }

    // ---- filter ----

    /// <returns>True when the shown songs changed.</returns>
    public bool SetFilter(Func<Music, bool> newFilter)
    {
        _filter = newFilter;
        return Refilter();
    }

    /// <summary>Runs the filter again (what it looks at may have changed).</summary>
    /// <returns>True when the shown songs changed.</returns>
    public bool Refilter()
    {
        var next = new List<Row>(_all.Count);
        foreach (Row row in _all)
        {
            if (_filter == null || _filter(row.Music))
            {
                next.Add(row);
            }
        }
        bool changed = next.Count != _shown.Count;
        for (int i = 0; !changed && i < next.Count; i++)
        {
            changed = !ReferenceEquals(next[i], _shown[i]);
        }
        if (changed)
        {
            _shown = next;
            var songs = new List<Music>(next.Count);
            foreach (Row row in next)
            {
                songs.Add(row.Music);
            }
            _shownSongs = songs.AsReadOnly();
        }
        return changed;
    }

    public int ShownCount()
    {
        return _shown.Count;
    }

    public Row ShownRow(int index)
    {
        return _shown[index];
    }

    /// <summary>A snapshot: later changes of the model never alter a list handed out earlier.</summary>
    public IReadOnlyList<Music> ShownSongs()
    {
        return _shownSongs;
    }

    /// <summary>The position among the shown songs of the song with this path, or -1.</summary>
    public int ShownIndexOf(string path)
    {
        if (path == null)
        {
            return -1;
        }
        for (int i = 0; i < _shown.Count; i++)
        {
            if (_shown[i].Path == path)
            {
                return i;
            }
        }
        return -1;
    }

    // ---- status ----

    public void SetStatuses(IReadOnlyDictionary<string, SongStatus> newStatuses)
    {
        _statuses.Clear();
        if (newStatuses != null)
        {
            foreach (KeyValuePair<string, SongStatus> entry in newStatuses)
            {
                if (entry.Key != null && entry.Value != null)
                {
                    _statuses[entry.Key] = entry.Value;
                }
            }
        }
    }

    public void SetStatus(string path, SongStatus status)
    {
        if (path == null)
        {
            return;
        }
        if (status == null)
        {
            _statuses.Remove(path);
        }
        else
        {
            _statuses[path] = status;
        }
    }

    public SongStatus StatusOf(string path)
    {
        SongStatus status = null;
        if (path != null)
        {
            _statuses.TryGetValue(path, out status);
        }
        return status ?? SongStatus.Unknown;
    }

    // ---- selection ----

    public bool IsSelected(string path)
    {
        return path != null && _selected.Contains(path);
    }

    public int SelectedCount()
    {
        return _selected.Count;
    }

    /// <summary>A copy: the caller may keep it while the selection moves on.</summary>
    public HashSet<string> SelectedPaths()
    {
        return new HashSet<string>(_selected);
    }

    /// <summary>Selects exactly these songs (paths that are not in the library are ignored).</summary>
    /// <returns>True when it changed.</returns>
    public bool SetSelectedPaths(IEnumerable<string> paths)
    {
        var next = new HashSet<string>();
        if (paths != null)
        {
            foreach (string path in paths)
            {
                if (path != null && _indexByPath.ContainsKey(path))
                {
                    next.Add(path);
                }
            }
        }
        if (next.SetEquals(_selected))
        {
            return false;
        }
        _selected.Clear();
        _selected.UnionWith(next);
        return true;
    }

    /// <summary>Selects every shown song, keeping the ones already selected (also the hidden ones).</summary>
    public bool SelectAllVisible()
    {
        bool changed = false;
        foreach (Row row in _shown)
        {
            changed |= _selected.Add(row.Path);
        }
        return changed;
    }

    public bool ClearSelection()
    {
        if (_selected.Count == 0)
        {
            return false;
        }
        _selected.Clear();
        return true;
    }

    /// <summary>Flips the selection of the shown song at this position.</summary>
    /// <returns>True when it changed (always, for a valid one).</returns>
    public bool Toggle(int shownIndex)
    {
        if (shownIndex < 0 || shownIndex >= _shown.Count)
        {
            return false;
        }
        string path = _shown[shownIndex].Path;
        if (!_selected.Remove(path))
        {
            _selected.Add(path);
        }
        return true;
    }

    /// <summary>Selects or deselects the shown songs from one position to the other, both included.</summary>
    public bool SetRange(int fromShownIndex, int toShownIndex, bool select)
    {
        int from = Math.Max(0, Math.Min(fromShownIndex, toShownIndex));
        int to = Math.Min(_shown.Count - 1, Math.Max(fromShownIndex, toShownIndex));
        bool changed = false;
        for (int i = from; i <= to; i++)
        {
            string path = _shown[i].Path;
            changed |= select ? _selected.Add(path) : _selected.Remove(path);
        }
        return changed;
    }

    // ---- text ----

    private static string TitleOf(Music music, string path)
    {
        string title = music.Title;
        if (title != null && !JavaString.IsBlank(title))
        {
            return title;
        }
        // no title tag: the file name without its extension, like the library scan does
        int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        string name = path.Substring(slash + 1);
        int dot = name.LastIndexOf('.');
        return dot > 0 ? JavaString.Substring(name, 0, dot) : name;
    }
}
