using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.View.Library;

/// <summary>
/// One choice the album box offers while the user types: the album's name and the folder the songs go into with it. Picking
/// a row decides both, so the row says which folder it is: what was typed goes into a folder named after it, an album the
/// library has already goes into the folder it is in (which may be called something else, "SOUR [Hi-Res]").
/// </summary>
/// <param name="Name">the album name the songs get</param>
/// <param name="Detail">the one short line under the name: "as typed" for what was typed, the folder for a row that goes into a folder with another name than its own, otherwise empty</param>
/// <param name="IsOwn">the name is one of the album's own (its current name or a spelling its songs have)</param>
/// <param name="Folder">the name of the folder the songs go into when the row is picked</param>
public sealed record AlbumSuggestion(string Name, string Detail, bool IsOwn, string Folder)
{
    /// <summary>What the suggestion box writes for the suggestion when nothing else says.</summary>
    public override string ToString()
    {
        return Name;
    }
}

/// <summary>
/// The choices the album box offers: first what was typed (into a folder named after it), then the album's own spellings and
/// the other albums of the library, each with the folder it goes into (said only when it is not the one the name makes). A name the user types or picks is the user's decision,
/// folder included: typing "SOUR" means a folder called "SOUR" unless the row of the album that lives in "SOUR [Hi-Res]" is
/// picked. Nothing here reads a file: it works from the album plan.
/// </summary>
public static class AlbumSuggestions
{
    public const int MaxShown = 10;

    private sealed class Entry
    {
        public string Name;
        public string Folder;
        public bool IsOwn;
        public bool IsTyped;
        public int OwnOrder = int.MaxValue; // the order the album's own names come in: the current name first, then the spellings
        public int Songs; // how many songs have the name already, to put the biggest first
        public string Key; // the identity of the name
    }

    /// <summary>The choices for what is typed now (all of them when nothing is typed), what was typed first, then the best match first.</summary>
    /// <param name="editing">the album whose name is being typed: its own spellings are offered, and it is left out of "the other albums"</param>
    public static List<AlbumSuggestion> For(AlbumPlan plan, AlbumGroup editing, string typed)
    {
        string query = typed == null ? "" : ArtistStandardizer.NormalizeRaw(typed);
        string queryKey = AlbumStandardizer.IdentityKey(query);
        Dictionary<string, string> homes = AlbumStandardizer.HomeFolders(plan);
        List<string> parents = ParentsOfSongsToFile(plan, editing);

        // how many songs each album has, to put the biggest first (it is not shown)
        var all = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SongAlbumTags song in plan.Songs)
        {
            string key = GroupKeyOf(plan, song);
            if (key != null)
            {
                all[key] = all.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }

        // the folder the songs go into with a name: the folder the album lives in already when the songs can join it (it is
        // next to the one they are in), otherwise a folder named after the name
        string FolderFor(string name, out bool isHome)
        {
            string home = AlbumStandardizer.HomeFolderOfName(homes, name);
            isHome = home != null && parents.Any(parent => AlbumStandardizer.SamePath(parent, Path.GetDirectoryName(home)));
            return isHome ? Path.GetFileName(home) : AlbumFolderName.Clean(name);
        }

        var rows = new List<Entry>();

        // what was typed: a folder named after it, whatever the library has
        if (query.Length != 0)
        {
            AddRow(rows, new Entry { Name = query, Folder = AlbumFolderName.Clean(query), IsTyped = true, Key = queryKey });
        }

        // the album's own names: the current one, then the spellings its songs have
        if (editing != null)
        {
            int order = 0;
            string current = editing.CanonicalName == null ? "" : ArtistStandardizer.NormalizeRaw(editing.CanonicalName);
            string currentFolder = string.IsNullOrEmpty(editing.FolderName) ? FolderFor(current, out _) : editing.FolderName;
            AddOwn(rows, current, currentFolder, order++, AlbumStandardizer.IdentityKey(current));
            foreach (AlbumVariant variant in editing.Variants)
            {
                if (variant.Count > 0 && variant.Text != null)
                {
                    string name = ArtistStandardizer.NormalizeRaw(variant.Text);
                    AddOwn(rows, name, FolderFor(name, out _), order++, AlbumStandardizer.IdentityKey(name));
                }
            }
        }

        // the other albums of the library
        foreach (AlbumGroup group in plan.Groups)
        {
            string name = group.CanonicalName == null ? "" : ArtistStandardizer.NormalizeRaw(group.CanonicalName);
            string key = AlbumStandardizer.IdentityKey(name);
            if (name.Length == 0 || key.Length == 0 || ReferenceEquals(group, editing) || group.Key == editing?.Key)
            {
                continue;
            }
            all.TryGetValue(group.Key, out int total);
            AddRow(rows, new Entry { Name = name, Folder = FolderFor(name, out _), Songs = total, Key = key });
        }

        IEnumerable<Entry> matching = rows.Where(entry => entry.IsTyped || ReviewText.NameMatches(entry.Name, query)
                || ReviewText.NameMatches(entry.Folder, query));
        return matching
                .OrderBy(entry => entry.IsTyped ? 0 : 1)
                .ThenBy(entry => RankOf(entry, query, queryKey))
                .ThenBy(entry => entry.IsOwn ? 0 : 1)
                .ThenBy(entry => entry.OwnOrder)
                .ThenByDescending(entry => entry.Songs)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxShown)
                .Select(entry => new AlbumSuggestion(entry.Name, DetailOf(entry), entry.IsOwn, entry.Folder))
                .ToList();
    }

    private static void AddOwn(List<Entry> rows, string name, string folder, int order, string key)
    {
        if (name.Length == 0 || key.Length == 0)
        {
            return;
        }
        AddRow(rows, new Entry { Name = name, Folder = folder, IsOwn = true, OwnOrder = order, Key = key });
    }

    /// <summary>adds a row; a row with the same name and the same folder is one choice</summary>
    private static void AddRow(List<Entry> rows, Entry row)
    {
        Entry same = rows.FirstOrDefault(other => string.Equals(other.Name, row.Name, StringComparison.Ordinal)
                && string.Equals(other.Folder, row.Folder, StringComparison.OrdinalIgnoreCase));
        if (same == null)
        {
            rows.Add(row);
            return;
        }
        same.IsOwn |= row.IsOwn;
        same.IsTyped |= row.IsTyped;
        same.OwnOrder = Math.Min(same.OwnOrder, row.OwnOrder);
        same.Songs = Math.Max(same.Songs, row.Songs);
    }

    /// <summary>
    /// The one short line under a row, when there is something to say that the name does not: "as typed" for the row of what
    /// was typed, and the folder for a row that goes into a folder with another name than its own ("SOUR" into "SOUR
    /// [Hi-Res]"), because picking it would otherwise send the songs where the row does not show. Nothing for the others.
    /// </summary>
    private static string DetailOf(Entry entry)
    {
        if (entry.IsTyped)
        {
            return "as typed";
        }
        return string.Equals(entry.Folder, AlbumFolderName.Clean(entry.Name), StringComparison.OrdinalIgnoreCase)
                ? "" : "folder \"" + entry.Folder + "\"";
    }

    /// <summary>0 = the name is what was typed, 1 = it starts with it, 2 = it has it inside, 3 = only the folder or the letters match; 0 for everything when nothing is typed</summary>
    private static int RankOf(Entry entry, string query, string queryKey)
    {
        if (query.Length == 0)
        {
            return 0;
        }
        if (queryKey.Length != 0 && entry.Key == queryKey)
        {
            return 0;
        }
        if (entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        return entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    }

    /// <summary>the key of the album group a song belongs to, or null</summary>
    private static string GroupKeyOf(AlbumPlan plan, SongAlbumTags song)
    {
        string raw = AlbumStandardizer.RawOf(song, plan.Root);
        return raw != null && plan.GroupKeyByRaw.TryGetValue(raw, out string key) ? key : null;
    }

    /// <summary>
    /// The folders the album's songs that are to be filed are in the folder of (a folder named after the album is made next to
    /// the folder they are in, and an album folder can only be joined from there). Songs directly in the opened folder stay.
    /// </summary>
    private static List<string> ParentsOfSongsToFile(AlbumPlan plan, AlbumGroup editing)
    {
        var parents = new List<string>();
        if (editing == null)
        {
            return parents;
        }
        foreach (SongAlbumTags song in plan.Songs)
        {
            if (!plan.IsInScope(song) || GroupKeyOf(plan, song) != editing.Key)
            {
                continue;
            }
            string directory = Path.GetDirectoryName(song.Path);
            string parent = string.IsNullOrEmpty(directory) ? null : Path.GetDirectoryName(directory);
            if (parent != null && !AlbumStandardizer.SamePath(directory, plan.Root) && !parents.Any(known => AlbumStandardizer.SamePath(known, parent)))
            {
                parents.Add(parent);
            }
        }
        return parents;
    }
}

/// <summary>
/// What a name the user typed or picked does to the album: the name, and the folder the songs go into. What the user types
/// or picks is the user's decision, folder included, so nothing happens that the user was not shown: a typed name puts the
/// songs in a folder named after it, a picked suggestion in the folder the suggestion shows.
/// </summary>
public static class AlbumNameChoice
{
    /// <summary>Applies the name (and the folder) to the album; an empty name changes nothing.</summary>
    /// <param name="folder">the folder of the suggestion that was picked, or null when the name was typed</param>
    /// <param name="changed">true if the album's name or folder is another than it was (the preview has to be made again)</param>
    /// <returns>the name the album has now</returns>
    public static string Apply(AlbumGroup group, string typed, string folder, out bool changed)
    {
        changed = false;
        string name = JavaString.Trim(typed ?? "");
        if (name.Length == 0)
        {
            return group.CanonicalName;
        }
        bool nameChanged = !string.Equals(name, group.CanonicalName, StringComparison.Ordinal);
        if (folder == null && !nameChanged)
        {
            return name; // the box was only left: nothing was edited, so the program's choice of folder stands
        }
        string wanted = folder ?? AlbumFolderName.Clean(name);
        string chosen = wanted.Length == 0 ? null : wanted;
        if (nameChanged || !string.Equals(chosen, group.FolderName, StringComparison.Ordinal))
        {
            group.CanonicalName = name;
            group.FolderName = chosen;
            changed = true;
        }
        return name;
    }
}

/// <summary>
/// The note under an album's name when the album goes into a folder that is not called like it by itself: the program named
/// the album, and a song named "SOUR" is put in the folder the other songs of SOUR are in, whatever that is called ("SOUR
/// [Hi-Res]"). The note says so on the card. Nothing is said when the user chose the folder (a name typed or picked is shown
/// with its folder), or when the folder is the one the name makes.
/// </summary>
public static class AlbumFolderNotes
{
    /// <summary>group key -> the note, for the albums that have one (their songs go into a folder with another name than the album's)</summary>
    public static Dictionary<string, string> For(AlbumPlan plan, IEnumerable<AlbumFolderMove> moves)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // moved song -> the folder it goes into
        foreach (AlbumFolderMove move in moves)
        {
            string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(move.SourceDirectory));
            if (string.IsNullOrEmpty(parent))
            {
                continue;
            }
            string folder = Path.Combine(parent, move.TargetName);
            foreach (string path in move.AudioPaths)
            {
                target[path] = folder;
            }
        }
        if (target.Count == 0)
        {
            return notes;
        }

        var folders = new Dictionary<string, List<string>>(StringComparer.Ordinal); // group key -> the folders its moved songs go into, in order
        foreach (SongAlbumTags song in plan.Songs)
        {
            string raw = AlbumStandardizer.RawOf(song, plan.Root);
            if (raw == null || !plan.GroupKeyByRaw.TryGetValue(raw, out string key) || !target.TryGetValue(song.Path, out string folder))
            {
                continue;
            }
            if (!folders.TryGetValue(key, out List<string> list))
            {
                list = new List<string>();
                folders[key] = list;
            }
            string leaf = Path.GetFileName(folder);
            if (!list.Contains(leaf, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(leaf);
            }
        }
        foreach (AlbumGroup group in plan.Groups)
        {
            if (!string.IsNullOrEmpty(group.FolderName) || !folders.TryGetValue(group.Key, out List<string> leaves))
            {
                continue; // the user chose this folder, or no song of the album moves
            }
            string named = AlbumFolderName.Clean(group.CanonicalName ?? "");
            List<string> unusual = leaves.Where(leaf => !string.Equals(leaf, named, StringComparison.OrdinalIgnoreCase)).ToList();
            if (unusual.Count != 0)
            {
                notes[group.Key] = (unusual.Count == 1 ? "Goes into the folder " : "Goes into the folders ")
                        + string.Join(", ", unusual.Select(leaf => "\"" + leaf + "\"")) + ", where this album is already";
            }
        }
        return notes;
    }
}
