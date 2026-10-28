using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.AddSongs;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>
/// The logic of standardizing albums that needs no files and no AI: where a song's album name comes from, how the raw
/// values are grouped into albums once something has said which album each one is, what that changes in the tags, and
/// which songs belong in another folder. Albums are grouped by their NAME, not by album artist: the songs of a compilation
/// whose album artist differs from song to song are still one album.
/// </summary>
public static class AlbumStandardizer
{
    /// <summary>a bracketed note about the file's quality or format, which is no part of an album's name: "[Hi-Res 24bit-96khz]", "(FLAC)"</summary>
    private static readonly Regex QualityTag = new Regex(
            @"\s*[\[\(（【](?:[^\]\)）】]*?(?:hi-?res|\d+\s*-?\s*bit|\d+\s*k?hz|flac|lossless|alac|\bwav\b|\bdsd\b|\bmp3\b|\d{2,3}\s*kbps?|blu-?ray)[^\]\)）】]*)[\]\)）】]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>a bracketed year: "(2017)", "[2021]"</summary>
    private static readonly Regex BracketedYear = new Regex(
            @"\s*[\[\(（【]\s*(?:19|20)\d{2}\s*[\]\)）】]", RegexOptions.CultureInvariant);

    private const int MaxContextNames = 3;

    // ------------------------------------------------------------------ the raw values

    /// <summary>
    /// The text a song's album name is worked out from: its ALBUM tag, or, when it has none, the name of the folder it is in
    /// (unless that is the folder that was opened). Null when there is nothing to go by. The text is normalized like the
    /// artist tags are (<see cref="ArtistStandardizer.NormalizeRaw"/>).
    /// </summary>
    public static string RawOf(SongAlbumTags song, string root)
    {
        string album = song.Album == null ? "" : ArtistStandardizer.NormalizeRaw(song.Album);
        if (album.Length != 0)
        {
            return album;
        }
        string directory = Path.GetDirectoryName(song.Path);
        if (string.IsNullOrEmpty(directory) || SamePath(directory, root))
        {
            return null;
        }
        string leaf = ArtistStandardizer.NormalizeRaw(Path.GetFileName(directory));
        // a folder like "New songs" or "Downloads" says nothing about the album
        return leaf.Length == 0 || LibraryFolderNames.IsGeneric(leaf) ? null : leaf;
    }

    /// <summary>true if the song has no ALBUM tag, so its raw value is the name of its folder</summary>
    public static bool IsFromFolder(SongAlbumTags song)
    {
        return song.Album == null || ArtistStandardizer.NormalizeRaw(song.Album).Length == 0;
    }

    /// <summary>
    /// every distinct raw value of the songs (the most used first, a stable order otherwise) with how many songs use it and
    /// who sings and where they are kept, which is what the AI is asked about
    /// </summary>
    public static OrderedDictionary<string, AlbumRaw> CollectRaw(IEnumerable<SongAlbumTags> songs, string root)
    {
        var order = new List<string>();
        var found = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (SongAlbumTags song in songs)
        {
            string raw = RawOf(song, root);
            if (raw == null)
            {
                continue;
            }
            if (!found.TryGetValue(raw, out Accumulator accumulator))
            {
                accumulator = new Accumulator();
                found[raw] = accumulator;
                order.Add(raw);
            }
            accumulator.Count++;
            accumulator.AnyTag |= !IsFromFolder(song);
            foreach (string artist in song.Artist.Count != 0 ? song.Artist : song.AlbumArtist)
            {
                Count(accumulator.Artists, ArtistStandardizer.NormalizeRaw(artist));
            }
            string directory = Path.GetDirectoryName(song.Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Count(accumulator.Folders, ArtistStandardizer.NormalizeRaw(Path.GetFileName(directory)));
            }
            if (song.Title != null)
            {
                Count(accumulator.Titles, ArtistStandardizer.NormalizeRaw(song.Title));
            }
        }

        var result = new OrderedDictionary<string, AlbumRaw>(StringComparer.Ordinal);
        foreach (string raw in order.OrderByDescending(raw => found[raw].Count))
        {
            Accumulator accumulator = found[raw];
            result[raw] = new AlbumRaw(accumulator.Count, !accumulator.AnyTag, Top(accumulator.Artists), Top(accumulator.Folders),
                    Top(accumulator.Titles));
        }
        return result;
    }

    private sealed class Accumulator
    {
        public int Count;
        public bool AnyTag;
        public readonly Dictionary<string, int> Artists = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Folders = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Titles = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    private static void Count(Dictionary<string, int> counts, string name)
    {
        if (name.Length != 0)
        {
            counts[name] = counts.TryGetValue(name, out int count) ? count + 1 : 1;
        }
    }

    private static List<string> Top(Dictionary<string, int> counts)
    {
        return counts.OrderByDescending(pair => pair.Value).Take(MaxContextNames).Select(pair => pair.Key).ToList();
    }

    // ------------------------------------------------------------------ without the AI

    /// <summary>
    /// The conservative answer when Claude is not used: the value without the notes about the file's quality or format
    /// ("Palette [Hi-Res 24bit-96khz]" is "Palette") and without a bracketed year. Spellings that differ only in case,
    /// spacing or punctuation are one album when the plan is built, whatever answers say.
    /// </summary>
    public static OrderedDictionary<string, ParsedAlbum> ParseLocally(IEnumerable<string> raws)
    {
        var parsed = new OrderedDictionary<string, ParsedAlbum>(StringComparer.Ordinal);
        foreach (string raw in raws)
        {
            if (raw != null && !parsed.ContainsKey(raw))
            {
                parsed[raw] = new ParsedAlbum(CleanLocally(raw), false);
            }
        }
        return parsed;
    }

    /// <summary>the value without the file-quality notes and the bracketed year; the value itself when nothing else would be left</summary>
    public static string CleanLocally(string raw)
    {
        string text = ArtistStandardizer.NormalizeRaw(raw);
        string cleaned = BracketedYear.Replace(QualityTag.Replace(text, ""), "");
        cleaned = ArtistStandardizer.NormalizeRaw(cleaned).Trim('-', ' ');
        return cleaned.Length == 0 ? text : cleaned;
    }

    // ------------------------------------------------------------------ the plan

    /// <summary>
    /// The identity of an album's name: accents, case, punctuation and spacing are ignored ("Love poem" and "LOVE POEM" are
    /// one album, so are "TWICEcoaster : LANE 1" and "Twicecoaster: Lane 1"). A plus sign is part of the title ("Real" and
    /// "Real+" are two albums, "Real+" and "Real Plus" are one) and so is an ampersand, which is the word "and".
    /// </summary>
    public static string IdentityKey(string name)
    {
        if (name == null)
        {
            return "";
        }
        string words = name.Normalize(System.Text.NormalizationForm.FormKC).Replace("+", " plus ").Replace("&", " and ");
        return ArtistStandardizer.LooseKey(words);
    }

    /// <summary>
    /// groups the songs' albums: every raw value takes the name the answers give it, and the values whose names are one album
    /// become one group. A value that has no answer is kept as it is, marked "please check".
    /// </summary>
    /// <param name="parsed">raw value (as <see cref="RawOf"/> gives it) -> what it is</param>
    /// <param name="scope">the songs that may change; null = every song</param>
    public static AlbumPlan BuildPlan(IEnumerable<SongAlbumTags> songs, IDictionary<string, ParsedAlbum> parsed, bool usedAi,
            string root, IEnumerable<string> scope)
    {
        List<SongAlbumTags> songList = songs.ToList();
        var builders = new OrderedDictionary<string, GroupBuilder>(StringComparer.Ordinal);
        var keyByRaw = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        int notAnAlbum = 0;

        foreach (SongAlbumTags song in songList)
        {
            string raw = RawOf(song, root);
            if (raw == null)
            {
                continue;
            }
            if (!parsed.TryGetValue(raw, out ParsedAlbum answer) || answer == null)
            {
                answer = new ParsedAlbum(CleanLocally(raw), true);
            }
            string name = ArtistStandardizer.NormalizeRaw(answer.Album);
            string key = IdentityKey(name);
            if (answer.IsNotAnAlbum || key.Length == 0)
            {
                notAnAlbum++;
                continue;
            }
            keyByRaw[raw] = key;
            if (!builders.TryGetValue(key, out GroupBuilder builder))
            {
                builder = new GroupBuilder(key);
                builders[key] = builder;
            }
            builder.Add(raw, name, answer.IsLowConfidence);
        }
        if (notAnAlbum != 0)
        {
            warnings.Add(notAnAlbum + (notAnAlbum == 1 ? " song has" : " songs have") + " no usable album name and "
                    + (notAnAlbum == 1 ? "is" : "are") + " left as " + (notAnAlbum == 1 ? "it is" : "they are") + ".");
        }

        List<AlbumGroup> groups = builders.Values.Select(builder => builder.ToGroup()).OrderByDescending(group => group.SongCount).ToList();
        return new AlbumPlan(root, usedAi, groups, keyByRaw, songList, warnings, scope);
    }

    /// <summary>the spellings found for one album while the plan is built</summary>
    private sealed class GroupBuilder
    {
        private readonly string _key;
        private readonly OrderedDictionary<string, int> _raws = new OrderedDictionary<string, int>(StringComparer.Ordinal);
        private readonly OrderedDictionary<string, int> _proposals = new OrderedDictionary<string, int>(StringComparer.Ordinal);
        private bool _lowConfidence;
        private int _songs;

        public GroupBuilder(string key)
        {
            _key = key;
        }

        public void Add(string raw, string proposedName, bool lowConfidence)
        {
            _raws[raw] = _raws.TryGetValue(raw, out int rawCount) ? rawCount + 1 : 1;
            _proposals[proposedName] = _proposals.TryGetValue(proposedName, out int proposalCount) ? proposalCount + 1 : 1;
            // a doubt about a value that is left as it is needs no look: only a change or a merge can be wrong
            _lowConfidence |= lowConfidence && !string.Equals(proposedName, raw, StringComparison.Ordinal);
            _songs++;
        }

        public AlbumGroup ToGroup()
        {
            // the spelling most songs were given, the first one found on a tie
            string name = null;
            int best = 0;
            foreach (KeyValuePair<string, int> proposal in _proposals)
            {
                if (proposal.Value > best)
                {
                    best = proposal.Value;
                    name = proposal.Key;
                }
            }
            List<AlbumVariant> variants = _raws.OrderByDescending(pair => pair.Value)
                    .Select(pair => new AlbumVariant(pair.Key, pair.Value)).ToList();
            // spellings the answers did not agree on (they only look the same to the plan) are for the user to settle
            return new AlbumGroup(_key, name, _lowConfidence || _proposals.Count > 1, variants, _songs);
        }
    }

    // ------------------------------------------------------------------ what changes

    /// <summary>the ALBUM tags that differ from the plan's names (only for the songs in the plan's scope)</summary>
    public static List<SongAlbumUpdate> ComputeUpdates(AlbumPlan plan)
    {
        var updates = new List<SongAlbumUpdate>();
        foreach (SongAlbumTags song in plan.Songs)
        {
            if (!plan.IsInScope(song))
            {
                continue;
            }
            AlbumGroup group = plan.GroupOf(song);
            string name = group == null ? "" : ArtistStandardizer.NormalizeRaw(group.CanonicalName);
            if (name.Length == 0 || string.Equals(song.Album, name, StringComparison.Ordinal))
            {
                continue;
            }
            updates.Add(new SongAlbumUpdate(song.Path, name, song.Album));
        }
        return updates;
    }

    /// <summary>
    /// The songs that are kept in a folder with another name than their album's, grouped by the folder they are in and the
    /// folder they belong in. Songs directly in the opened folder stay where they are (they are not in an album folder
    /// to rename), and so do songs whose album has no name.
    /// </summary>
    public static List<AlbumFolderMove> ComputeFolderMoves(AlbumPlan plan)
    {
        // every song of the plan by the folder it is in: whether the folder can be renamed as a whole depends on all of them
        var directories = new OrderedDictionary<string, List<(SongAlbumTags Song, string Target)>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> homes = HomeFolders(plan);
        foreach (SongAlbumTags song in plan.Songs)
        {
            string directory = Path.GetDirectoryName(song.Path);
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }
            AlbumGroup group = plan.GroupOf(song);
            string target = group == null ? "" : AlbumFolderName.Clean(group.CanonicalName);
            if (group != null && !string.IsNullOrEmpty(group.FolderName))
            {
                // the user chose the folder (typed a name, or picked a suggestion with its folder): that is the folder
                target = group.FolderName;
            }
            else if (group != null && homes.TryGetValue(NameKey(group), out string home)
                    && SamePath(Path.GetDirectoryName(home), Path.GetDirectoryName(directory)))
            {
                // the album has a folder already, with its other songs in it (they may not change now): this song joins them
                // there, whatever the folder is called, instead of making a second folder for the album
                target = Path.GetFileName(home);
            }
            if (!directories.TryGetValue(directory, out List<(SongAlbumTags Song, string Target)> songs))
            {
                songs = new List<(SongAlbumTags Song, string Target)>();
                directories[directory] = songs;
            }
            songs.Add((song, target));
        }

        var moves = new List<AlbumFolderMove>();
        foreach (KeyValuePair<string, List<(SongAlbumTags Song, string Target)>> entry in directories)
        {
            string directory = entry.Key;
            if (SamePath(directory, plan.Root))
            {
                continue;
            }
            string leaf = Path.GetFileName(directory);
            var moving = entry.Value.Where(item => plan.IsInScope(item.Song) && item.Target.Length != 0
                    && !string.Equals(item.Target, leaf, StringComparison.Ordinal)).ToList();
            if (moving.Count == 0)
            {
                continue;
            }
            bool oneTarget = moving.Select(item => item.Target).Distinct(StringComparer.Ordinal).Count() == 1;
            bool whole = oneTarget && moving.Count == entry.Value.Count;
            foreach (IGrouping<string, (SongAlbumTags Song, string Target)> target in moving.GroupBy(item => item.Target, StringComparer.Ordinal))
            {
                moves.Add(new AlbumFolderMove(directory, target.Key, target.Select(item => item.Song.Path).ToList(), whole));
            }
        }
        return moves;
    }

    /// <summary>
    /// What decides which folder an album belongs in: the identity of the name it has NOW. It is the group's key until the
    /// user changes the name; an album renamed to the name of another one (a playlist name changed to "SOUR") is that album
    /// from then on, so its songs join the folder the other one lives in.
    /// </summary>
    private static string NameKey(AlbumGroup group)
    {
        string key = group.CanonicalName == null ? "" : IdentityKey(ArtistStandardizer.NormalizeRaw(group.CanonicalName));
        return key.Length == 0 ? group.Key : key;
    }

    /// <summary>
    /// The folder (its full path) an album called this already lives in, from <see cref="HomeFolders"/>, or null: what a
    /// song named after this album is put with, whatever that folder is called.
    /// </summary>
    public static string HomeFolderOfName(IDictionary<string, string> homes, string albumName)
    {
        string key = albumName == null ? "" : IdentityKey(ArtistStandardizer.NormalizeRaw(albumName));
        return key.Length != 0 && homes.TryGetValue(key, out string home) ? home : null;
    }

    /// <summary>
    /// Where an album already lives when only some songs may change: the folder that holds most of the album's songs that
    /// may not change, if the album is at least half of what is in that folder, by the album's current name (see
    /// <see cref="NameKey"/>). Empty when every song may change (then the folders are named after the albums). The key is
    /// the identity of the name (<see cref="IdentityKey"/>), the value the folder's full path.
    /// </summary>
    public static Dictionary<string, string> HomeFolders(AlbumPlan plan)
    {
        var homes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (plan.Scope == null)
        {
            return homes;
        }
        var all = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byAlbum = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (SongAlbumTags song in plan.Songs)
        {
            string directory = Path.GetDirectoryName(song.Path);
            if (plan.IsInScope(song) || string.IsNullOrEmpty(directory) || SamePath(directory, plan.Root))
            {
                continue;
            }
            all[directory] = all.TryGetValue(directory, out int count) ? count + 1 : 1;
            AlbumGroup group = plan.GroupOf(song);
            if (group != null)
            {
                string albumKey = NameKey(group);
                if (!byAlbum.TryGetValue(albumKey, out Dictionary<string, int> directories))
                {
                    directories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    byAlbum[albumKey] = directories;
                }
                directories[directory] = directories.TryGetValue(directory, out int inDirectory) ? inDirectory + 1 : 1;
            }
        }
        foreach (KeyValuePair<string, Dictionary<string, int>> album in byAlbum)
        {
            KeyValuePair<string, int> best = album.Value.OrderByDescending(pair => pair.Value).First();
            if (best.Value * 2 >= all[best.Key])
            {
                homes[album.Key] = best.Key;
            }
        }
        return homes;
    }

    /// <summary>true if both paths name the same place (case and a closing separator do not matter)</summary>
    public static bool SamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
