using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.View.Library;

/// <summary>what a row of the folder tree is</summary>
public enum FolderNodeKind
{
    /// <summary>the folder that was opened</summary>
    Library,
    Folder,
    Song,
    /// <summary>a line of text under a folder ("11 other songs in this folder")</summary>
    Note,
}

/// <summary>how a row is drawn: plain = only there to show where things are, changed = something happens here, problem = a song that cannot go where it belongs, gone = a folder with no songs left</summary>
public enum FolderTone
{
    Plain,
    Changed,
    Problem,
    Gone,
}

/// <summary>One row of the folder tree: a folder, a song in it, or a line of text.</summary>
public sealed class FolderNode
{
    public FolderNode(FolderNodeKind kind, string name, string detail, FolderTone tone)
    {
        Kind = kind;
        Name = name ?? "";
        Detail = detail ?? "";
        Tone = tone;
    }

    public FolderNodeKind Kind { get; }

    /// <summary>the folder's name, or the song's file name as it will be</summary>
    public string Name { get; }

    /// <summary>what happens to it ("new folder", "moved from ..."), empty for a row that only shows where things are</summary>
    public string Detail { get; internal set; }

    public FolderTone Tone { get; internal set; }

    public List<FolderNode> Children { get; } = new List<FolderNode>();

    /// <summary>this row and everything under it</summary>
    public int TotalCount => 1 + Children.Sum(child => child.TotalCount);

    /// <summary>What a screen reader says for the row.</summary>
    public override string ToString()
    {
        return Detail.Length == 0 ? Name : Name + ", " + Detail;
    }
}

/// <summary>
/// What the library's folders will look like when the review is applied, as a tree: the folders that change (new, renamed,
/// joined by songs, left without songs) with the songs that arrive in them or get another file name, and the folders in
/// between. It replays what <see cref="AlbumStandardizer.ComputeFolderMoves"/> asks for the way the files are really moved
/// (see AlbumFolders in the data access: a folder is renamed as a whole when it can be, otherwise the songs go one by one
/// into the folder, which is made if it is not there, and a song never takes the place of a file with the same name), then
/// the renames to "Artist - Title". It reads no file: whether a folder exists is asked through a function.
/// </summary>
public sealed class FolderPreview
{
    private const string Separator = "  ·  ";

    private FolderPreview(FolderNode root, int foldersChanged, int songsMoved, int newSongs, int songsStuck, int songsRenamed)
    {
        Root = root;
        FoldersChanged = foldersChanged;
        SongsMoved = songsMoved;
        NewSongs = newSongs;
        SongsStuck = songsStuck;
        SongsRenamed = songsRenamed;
    }

    /// <summary>the folder that was opened, with the folders that change under it</summary>
    public FolderNode Root { get; }

    /// <summary>how many folders are made, renamed, get songs or are left without songs</summary>
    public int FoldersChanged { get; }

    /// <summary>how many songs end up in another folder (on their own, or because their folder is renamed)</summary>
    public int SongsMoved { get; }

    /// <summary>how many songs are added to the library (an Add that is not applied yet)</summary>
    public int NewSongs { get; }

    /// <summary>how many songs stay where they are because a song with the same name is in the folder they belong in</summary>
    public int SongsStuck { get; }

    /// <summary>how many songs get another file name ("Artist - Title")</summary>
    public int SongsRenamed { get; }

    /// <summary>true if there is anything to show but the folder itself</summary>
    public bool HasChanges => Root.Children.Count != 0;

    /// <param name="plan">the album plan: every song of the library (the songs of an Add at the paths they will have) and its root</param>
    /// <param name="moves">the folder changes the plan asks for now (<see cref="AlbumStandardizer.ComputeFolderMoves"/>)</param>
    /// <param name="newPaths">the songs that are not in the library yet (an Add), by the path they will have</param>
    /// <param name="renames">the files that will be renamed after the artist tags (<see cref="ArtistStandardizer.ComputeRenames"/>), by the path they have before the moves</param>
    /// <param name="folderExists">whether a folder is in the library now</param>
    public static FolderPreview Build(AlbumPlan plan, IEnumerable<AlbumFolderMove> moves, IEnumerable<string> newPaths,
            IEnumerable<SongRename> renames, Func<string, bool> folderExists)
    {
        var layout = new Layout(plan, newPaths ?? new List<string>(), renames ?? new List<SongRename>(),
                folderExists ?? (_ => false));
        foreach (AlbumFolderMove move in moves ?? new List<AlbumFolderMove>())
        {
            layout.Apply(move);
        }
        return layout.ToPreview();
    }

    /// <summary>one song of the library while the moves are replayed</summary>
    private sealed class Entry
    {
        public string Dir; // the folder it is in now (as the moves go)
        public string FileName; // the name it has now
        public string FinalName; // the name it ends up with
        public bool IsNew;
        public bool Relocated; // it is in another folder than it began in
        public string MovedFrom; // the folder it was moved out of on its own, or null
        public string Problem; // why it could not go where it belongs, or null
    }

    /// <summary>what became of a folder that is the target of a move</summary>
    private sealed class DirInfo
    {
        public string RenamedFrom; // the folder was a folder of another name
        public bool Created; // the folder was not there: songs were put in a new one
    }

    private sealed class Layout
    {
        private readonly string _root;
        private readonly Func<string, bool> _folderExists;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DirInfo> _infos = new Dictionary<string, DirInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _renamedAway = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _leftBy = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // folders a song was moved out of

        public Layout(AlbumPlan plan, IEnumerable<string> newPaths, IEnumerable<SongRename> renames, Func<string, bool> folderExists)
        {
            _root = Trim(plan.Root ?? "");
            _folderExists = folderExists;
            var isNew = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
            var stems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SongRename rename in renames)
            {
                stems[rename.AudioPath] = rename.NewBaseName;
            }
            foreach (SongAlbumTags song in plan.Songs)
            {
                string dir = Trim(Path.GetDirectoryName(song.Path) ?? "");
                if (dir.Length == 0)
                {
                    continue;
                }
                string file = Path.GetFileName(song.Path);
                _entries[song.Path] = new Entry
                {
                    Dir = dir,
                    FileName = file,
                    FinalName = stems.TryGetValue(song.Path, out string stem) ? stem + Path.GetExtension(file) : file,
                    IsNew = isNew.Contains(song.Path),
                };
                NamesIn(dir).Add(file);
            }
        }

        private static string Trim(string path)
        {
            return path.Length == 0 ? path : Path.TrimEndingDirectorySeparator(path);
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
        }

        private HashSet<string> NamesIn(string dir)
        {
            if (!_names.TryGetValue(dir, out HashSet<string> names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _names[dir] = names;
            }
            return names;
        }

        private DirInfo InfoOf(string dir)
        {
            if (!_infos.TryGetValue(dir, out DirInfo info))
            {
                info = new DirInfo();
                _infos[dir] = info;
            }
            return info;
        }

        /// <summary>true if the folder is there: it has songs, an earlier move made it, or it is in the library</summary>
        private bool Exists(string dir)
        {
            return (_names.TryGetValue(dir, out HashSet<string> names) && names.Count != 0) || _created.Contains(dir) || _folderExists(dir);
        }

        /// <summary>does what AlbumFolders.Move does with one move</summary>
        public void Apply(AlbumFolderMove move)
        {
            string source = Trim(move.SourceDirectory);
            string parent = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(parent))
            {
                return; // a folder with no folder around it: the songs stay
            }
            string target = Path.Combine(parent, move.TargetName);
            bool sameFolder = Same(source, target);
            bool taken = !sameFolder && Exists(target);

            // the folder is the album: it is renamed, with everything in it
            if (sameFolder || (move.IsWholeFolder && !taken))
            {
                RenameFolder(source, target, sameFolder);
                return;
            }

            // song by song, into the album's folder
            bool existed = Exists(target);
            int moved = 0;
            foreach (string path in move.AudioPaths)
            {
                if (!_entries.TryGetValue(path, out Entry entry) || !Same(entry.Dir, source))
                {
                    continue;
                }
                if (NamesIn(target).Contains(entry.FileName))
                {
                    entry.Problem = "stays here: \"" + entry.FileName + "\" is in \"" + move.TargetName + "\" already";
                    continue;
                }
                NamesIn(source).Remove(entry.FileName);
                NamesIn(target).Add(entry.FileName);
                entry.Dir = target;
                entry.MovedFrom = Path.GetFileName(source);
                entry.Relocated = true;
                moved++;
            }
            if (moved != 0)
            {
                _leftBy.Add(source);
                _created.Add(target);
                if (!existed)
                {
                    InfoOf(target).Created = true;
                }
            }
        }

        private void RenameFolder(string source, string target, bool sameFolder)
        {
            string from = Path.GetFileName(source);
            foreach (Entry entry in _entries.Values)
            {
                if (Same(entry.Dir, source))
                {
                    entry.Dir = target;
                    entry.Relocated |= !sameFolder;
                }
            }
            if (!sameFolder)
            {
                NamesIn(target).UnionWith(NamesIn(source));
                _names.Remove(source);
                _renamedAway.Add(source);
            }
            _created.Add(target);
            if (!sameFolder && !_folderExists(source))
            {
                InfoOf(target).Created = true; // the folder is one the songs of an Add were copied into: the library has no such folder to rename
            }
            else if (!string.Equals(from, Path.GetFileName(target), StringComparison.Ordinal))
            {
                InfoOf(target).RenamedFrom = from;
            }
        }

        public FolderPreview ToPreview()
        {
            // which songs each folder holds in the end, and which of them get a row
            var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rows = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // folders a song arrives in or cannot leave
            int moved = 0;
            int added = 0;
            int stuck = 0;
            int renamed = 0;
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                Entry entry = pair.Value;
                held[entry.Dir] = held.TryGetValue(entry.Dir, out int count) ? count + 1 : 1;
                bool renamedFile = !string.Equals(entry.FinalName, entry.FileName, StringComparison.Ordinal);
                moved += entry.Relocated ? 1 : 0;
                added += entry.IsNew ? 1 : 0;
                stuck += entry.Problem != null ? 1 : 0;
                renamed += renamedFile ? 1 : 0;
                if (entry.IsNew || entry.MovedFrom != null || entry.Problem != null)
                {
                    touched.Add(entry.Dir);
                }
                if (entry.IsNew || entry.MovedFrom != null || entry.Problem != null || renamedFile)
                {
                    if (!rows.TryGetValue(entry.Dir, out List<Entry> list))
                    {
                        list = new List<Entry>();
                        rows[entry.Dir] = list;
                    }
                    list.Add(entry);
                }
            }

            // the folders a song was moved out of that have none left
            var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string source in _leftBy)
            {
                if (!held.ContainsKey(source) && !_renamedAway.Contains(source))
                {
                    emptied.Add(source);
                }
            }

            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // a folder whose songs only get another file name is no folder change and is left out (the songs in a folder that is shown do get their new names)
            shown.UnionWith(touched);
            shown.UnionWith(_infos.Keys);
            shown.UnionWith(emptied);

            var rootNode = new FolderNode(FolderNodeKind.Library, LeafOf(_root), "", FolderTone.Plain);
            var nodes = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
            int foldersChanged = 0;
            foreach (string dir in shown)
            {
                FolderNode folder = NodeFor(dir, rootNode, nodes);
                if (folder == rootNode)
                {
                    AddSongRows(folder, dir, rows);
                    continue;
                }

                _infos.TryGetValue(dir, out DirInfo info);
                List<Entry> here = rows.TryGetValue(dir, out List<Entry> list) ? list : new List<Entry>();
                int arriving = here.Count(entry => entry.IsNew || entry.MovedFrom != null);
                var parts = new List<string>();
                if (info != null && info.RenamedFrom != null)
                {
                    parts.Add("renamed from \"" + info.RenamedFrom + "\"");
                }
                bool existedBefore = info == null ? _folderExists(dir) : info.RenamedFrom == null && !info.Created;
                if (arriving != 0)
                {
                    parts.Add(!existedBefore && (info == null || info.RenamedFrom == null) ? "new folder"
                            : arriving + (arriving == 1 ? " song added" : " songs added"));
                }
                bool gone = emptied.Contains(dir);
                if (gone)
                {
                    parts.Add("no songs left");
                }
                folder.Detail = string.Join(Separator, parts);
                folder.Tone = gone ? FolderTone.Gone : parts.Count != 0 ? FolderTone.Changed : FolderTone.Plain;
                if (parts.Count != 0)
                {
                    foldersChanged++;
                }

                AddSongRows(folder, dir, rows);
                int others = (held.TryGetValue(dir, out int total) ? total : 0) - here.Count;
                if (others > 0)
                {
                    string text = others + (others == 1 ? " song" : " songs");
                    folder.Children.Add(new FolderNode(FolderNodeKind.Note,
                            here.Count == 0 ? text + " in this folder" : others + (others == 1 ? " other song" : " other songs") + " in this folder",
                            "", FolderTone.Plain));
                }
            }
            Sort(rootNode);
            return new FolderPreview(rootNode, foldersChanged, moved, added, stuck, renamed);
        }

        private static void AddSongRows(FolderNode folder, string dir, Dictionary<string, List<Entry>> rows)
        {
            if (!rows.TryGetValue(dir, out List<Entry> here))
            {
                return;
            }
            foreach (Entry entry in here)
            {
                var parts = new List<string>();
                if (entry.IsNew)
                {
                    parts.Add("new song");
                }
                if (entry.MovedFrom != null && !entry.IsNew) // a new song's folder is the one it was copied into: it is not in the library
                {
                    parts.Add("moved from \"" + entry.MovedFrom + "\"");
                }
                if (!string.Equals(entry.FinalName, entry.FileName, StringComparison.Ordinal))
                {
                    parts.Add("renamed from \"" + entry.FileName + "\"");
                }
                if (entry.Problem != null)
                {
                    parts.Add(entry.Problem);
                }
                folder.Children.Add(new FolderNode(FolderNodeKind.Song, entry.FinalName, string.Join(Separator, parts),
                        entry.Problem != null ? FolderTone.Problem : FolderTone.Changed));
            }
        }

        /// <summary>the node of a folder, with the nodes of the folders between it and the root made as they are needed</summary>
        private FolderNode NodeFor(string dir, FolderNode rootNode, Dictionary<string, FolderNode> nodes)
        {
            if (Same(dir, _root))
            {
                return rootNode;
            }
            if (nodes.TryGetValue(dir, out FolderNode node))
            {
                return node;
            }
            string parent = Path.GetDirectoryName(dir);
            FolderNode parentNode = !string.IsNullOrEmpty(parent) && IsUnderRoot(dir) ? NodeFor(parent, rootNode, nodes) : rootNode;
            node = new FolderNode(FolderNodeKind.Folder, LeafOf(dir), "", FolderTone.Plain);
            parentNode.Children.Add(node);
            nodes[dir] = node;
            return node;
        }

        private bool IsUnderRoot(string dir)
        {
            return _root.Length != 0 && dir.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string LeafOf(string dir)
        {
            string leaf = Path.GetFileName(dir);
            return leaf.Length == 0 ? dir : leaf;
        }

        /// <summary>folders first (the ones with no songs left last), then the songs by name, then the note</summary>
        private static void Sort(FolderNode node)
        {
            node.Children.Sort((a, b) =>
            {
                int rank = Rank(a).CompareTo(Rank(b));
                return rank != 0 ? rank : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (FolderNode child in node.Children)
            {
                Sort(child);
            }
        }

        private static int Rank(FolderNode node)
        {
            return node.Kind == FolderNodeKind.Note ? 3 : node.Kind == FolderNodeKind.Song ? 2 : node.Tone == FolderTone.Gone ? 1 : 0;
        }
    }
}
