using System.Globalization;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.View.Library;

/// <summary>
/// The decisions and wording of the review page that need no UI: what counts as work, which songs and artists the
/// review lists first, and the text of the result and the "no key" messages.
/// </summary>
public static class ReviewText
{
    /// <summary>What romaji does, in the spec's words: the older Chinese-character romaji IS replaced; Latin romaji is kept.</summary>
    public const string RomajiAdds = "Adds a Latin-letter line above every Korean or Japanese line, in the LDDC style.";
    public const string RomajiReplaces = "Replaces older romaji written in Chinese characters (what LDDC's romaji option makes) "
            + "with Latin letters.";
    public const string RomajiKeeps = "Romaji that is already in Latin letters is kept.";

    /// <summary>True if standardizing writes or deletes something for this song.</summary>
    public static bool IsWork(LyricsAction action)
    {
        switch (action)
        {
            case LyricsAction.ConvertedFromTtml:
            case LyricsAction.ConvertedFromHmrc:
            case LyricsAction.NormalizedLrc:
            case LyricsAction.LineSyncedFromDeprecated:
            case LyricsAction.RomajiAdded:
                return true;
            default:
                return false;
        }
    }

    /// <summary>True if the preview found anything to write or delete.</summary>
    public static bool HasLyricsWork(StandardizeLyricsOutputData preview)
    {
        if (preview == null)
        {
            return false;
        }
        foreach (SongLyricsResult result in preview.Results)
        {
            if (IsWork(result.Action) || result.DeletedFiles.Count != 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The rows (as the presenter made them, one per result and in the same order) with the songs that failed first, then
    /// the ones that change, then the rest: a long list would otherwise bury the problems. Unchanged order otherwise.
    /// </summary>
    public static List<string[]> OrderRows(List<string[]> rows, StandardizeLyricsOutputData result)
    {
        if (result == null || result.Results.Count != rows.Count)
        {
            return rows;
        }
        var order = new List<int>();
        for (int rank = 0; rank <= 2; rank++)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                SongLyricsResult song = result.Results[i];
                int songRank = song.Action == LyricsAction.Failed ? 0
                        : (IsWork(song.Action) || song.DeletedFiles.Count != 0 ? 1 : 2);
                if (songRank == rank)
                {
                    order.Add(i);
                }
            }
        }
        var ordered = new List<string[]>(rows.Count);
        foreach (int index in order)
        {
            ordered.Add(rows[index]);
        }
        return ordered;
    }

    /// <summary>The artist cards worth reviewing: with a scope, only the artists the scope's songs mention (names come from the whole folder).</summary>
    public static List<ArtistGroup> GroupsInScope(ArtistPlan plan)
    {
        var scope = plan.Scope;
        if (scope == null)
        {
            return plan.Groups;
        }
        var keysByRaw = plan.GroupKeysByRaw;
        var keys = new HashSet<string>();
        if (keysByRaw != null)
        {
            foreach (SongArtistTags song in plan.Songs)
            {
                if (scope.Contains(song.Path))
                {
                    AddKeys(song.Artist, keysByRaw, keys);
                    AddKeys(song.AlbumArtist, keysByRaw, keys);
                    AddKeys(song.Artists, keysByRaw, keys);
                }
            }
        }
        var groups = new List<ArtistGroup>();
        foreach (ArtistGroup group in plan.Groups)
        {
            if (keys.Contains(group.Key))
            {
                groups.Add(group);
            }
        }
        return groups;
    }

    /// <summary>true if the card of this group is marked "please check" (the red ones): Claude suggested the name or is not sure of it</summary>
    public static bool NeedsCheck(ArtistGroup group)
    {
        return group.IsSuggestedByAi || group.IsLowConfidence;
    }

    /// <summary>
    /// The artist cards in the order the page shows them: the ones marked "please check" (the red ones) first, because
    /// they are the ones to look at, then the others. Each part keeps the order the plan has (most songs first).
    /// </summary>
    public static List<ArtistGroup> CheckFirst(IEnumerable<ArtistGroup> groups)
    {
        var red = new List<ArtistGroup>();
        var rest = new List<ArtistGroup>();
        foreach (ArtistGroup group in groups)
        {
            (NeedsCheck(group) ? red : rest).Add(group);
        }
        red.AddRange(rest);
        return red;
    }

    /// <summary>
    /// Whether an artist card matches what was typed into the search box: the chosen name or any spelling the library uses
    /// for the artist contains it, ignoring case and, as a second chance, ignoring everything that is not a letter or a
    /// digit ("kwill" finds "K.Will"). An empty search matches every card.
    /// </summary>
    public static bool MatchesSearch(ArtistGroup group, string query)
    {
        string typed = query == null ? "" : JavaString.Trim(query);
        if (typed.Length == 0)
        {
            return true;
        }
        string letters = LettersOnly(typed);
        if (Matches(group.CanonicalName, typed, letters))
        {
            return true;
        }
        foreach (ArtistVariant variant in group.Variants)
        {
            if (variant.Count > 0 && Matches(variant.Text, typed, letters))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Matches(string name, string typed, string letters)
    {
        if (name == null)
        {
            return false;
        }
        if (name.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }
        return letters.Length != 0 && LettersOnly(name).Contains(letters, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string LettersOnly(string text)
    {
        var kept = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                kept.Append(c);
            }
        }
        return kept.ToString();
    }

    /// <summary>One song of an artist card: its name as the cards show it, and the artist tag it carries.</summary>
    public sealed record ArtistSong(string Name, string ArtistTag);

    /// <summary>
    /// The songs the card of this group counts (its "N songs"): every song of the folder whose ARTIST, ALBUMARTIST or ARTISTS
    /// tag names the artist, in the order of the library. The same rule the plan counts them with.
    /// </summary>
    public static List<ArtistSong> SongsOf(ArtistPlan plan, ArtistGroup group)
    {
        var songs = new List<ArtistSong>();
        var keysByRaw = plan.GroupKeysByRaw;
        if (keysByRaw == null)
        {
            return songs;
        }
        foreach (SongArtistTags song in plan.Songs)
        {
            var keys = new HashSet<string>();
            AddKeys(song.Artist, keysByRaw, keys);
            AddKeys(song.AlbumArtist, keysByRaw, keys);
            AddKeys(song.Artists, keysByRaw, keys);
            if (keys.Contains(group.Key))
            {
                string tag = song.Artist.Count != 0 ? string.Join("; ", song.Artist)
                        : song.AlbumArtist.Count != 0 ? string.Join("; ", song.AlbumArtist) : string.Join("; ", song.Artists);
                songs.Add(new ArtistSong(DisplayName(song.Path), tag));
            }
        }
        return songs;
    }

    private static void AddKeys(IEnumerable<string> values, IDictionary<string, List<string>> keysByRaw, HashSet<string> keys)
    {
        if (values == null)
        {
            return;
        }
        foreach (string value in values)
        {
            if (keysByRaw.TryGetValue(ArtistStandardizer.NormalizeRaw(value), out List<string> found) && found != null)
            {
                keys.UnionWith(found);
            }
        }
    }

    /// <summary>"Album folder / Song name", so identically named songs in different albums are told apart.</summary>
    public static string DisplayName(string audioPath)
    {
        string name = Path.GetFileName(audioPath);
        int dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            name = JavaString.Substring(name, 0, dot);
        }
        string parent = Path.GetDirectoryName(audioPath);
        string parentName = parent == null ? "" : Path.GetFileName(parent);
        return parentName.Length == 0 ? name : parentName + " / " + name;
    }

    /// <summary>A presenter summary is "headline\ndetail lines": {headline, detail}, the detail empty when there is none.</summary>
    public static string[] SplitSummary(string summary)
    {
        if (summary == null)
        {
            return new[] { "", "" };
        }
        int newline = summary.IndexOf('\n');
        return newline < 0 ? new[] { summary, "" }
                : new[] { JavaString.Substring(summary, 0, newline), summary.Substring(newline + 1) };
    }

    public static string SongCount(int count)
    {
        return count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " song" : " songs");
    }

    // ------------------------------------------------------------------ messages

    /// <summary>What the result dialog says: the artists part, then the lyrics part, each with its own lines.</summary>
    public static string ResultMessage(string artistsSummary, string lyricsSummary)
    {
        string artists = artistsSummary == null ? "" : JavaString.Trim(artistsSummary);
        string lyrics = lyricsSummary == null ? "" : JavaString.Trim(lyricsSummary);
        return JavaString.Trim((artists.Length == 0 ? "" : "Artists: " + artists + "\n\n") + (lyrics.Length == 0 ? "" : "Lyrics: " + lyrics));
    }

    /// <summary>True if the options use Claude and no key was typed, so the environment has to be asked for one.</summary>
    public static bool NeedsKeyLookup(StandardizeOptions options)
    {
        bool romaji = options.IsLyrics && options.IsRomaji;
        bool artists = options.IsArtists && options.IsUseClaudeForArtists;
        return (romaji || artists) && JavaString.IsBlank(options.ApiKey);
    }

    /// <summary>
    /// The message for options that need Claude when there is no API key at all (so the run is not even started), or null
    /// when the key is there or Claude is not needed.
    /// </summary>
    public static string MissingKeyMessage(StandardizeOptions options, bool keyInEnvironment)
    {
        bool romaji = options.IsLyrics && options.IsRomaji;
        bool artists = options.IsArtists && options.IsUseClaudeForArtists;
        if (!(romaji || artists) || keyInEnvironment || !JavaString.IsBlank(options.ApiKey))
        {
            return null;
        }
        string what = romaji && artists ? "Romaji and grouping the artist names both use Claude"
                : romaji ? "Romaji uses Claude" : "Grouping the artist names uses Claude";
        return what + ", but no Anthropic API key was found.\n\n"
                + "Type a key in Options, set the ANTHROPIC_API_KEY environment variable, or turn the Claude option off.";
    }
}
