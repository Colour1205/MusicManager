using System.Globalization;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
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
        return ResultMessage(artistsSummary, lyricsSummary, "");
    }

    /// <summary>The same, with a part about the file names that were changed to "Artist - Title" between the two.</summary>
    public static string ResultMessage(string artistsSummary, string lyricsSummary, string renamedSummary)
    {
        return ResultMessage(artistsSummary, lyricsSummary, renamedSummary, "", "");
    }

    /// <summary>The same, with the parts about the album tags and the album folders (after the artists, before the file names).</summary>
    public static string ResultMessage(string artistsSummary, string lyricsSummary, string renamedSummary,
            string albumsSummary, string foldersSummary)
    {
        string artists = artistsSummary == null ? "" : JavaString.Trim(artistsSummary);
        string lyrics = lyricsSummary == null ? "" : JavaString.Trim(lyricsSummary);
        string renamed = renamedSummary == null ? "" : JavaString.Trim(renamedSummary);
        string albums = albumsSummary == null ? "" : JavaString.Trim(albumsSummary);
        string folders = foldersSummary == null ? "" : JavaString.Trim(foldersSummary);
        return JavaString.Trim((artists.Length == 0 ? "" : "Artists: " + artists + "\n\n")
                + (albums.Length == 0 ? "" : "Albums: " + albums + "\n\n")
                + (folders.Length == 0 ? "" : "Folders: " + folders + "\n\n")
                + (renamed.Length == 0 ? "" : "File names: " + renamed + "\n\n")
                + (lyrics.Length == 0 ? "" : "Lyrics: " + lyrics));
    }

    /// <summary>The line of the preview that says the files will be renamed too.</summary>
    public static string RenameNote(int songs)
    {
        return songs + (songs == 1 ? " song's files" : " songs' files") + " will be renamed to \"Artist - Title\" after their tags";
    }

    /// <summary>True if the options use Claude and no key was typed, so the environment has to be asked for one.</summary>
    public static bool NeedsKeyLookup(StandardizeOptions options)
    {
        bool romaji = options.IsLyrics && options.IsRomaji;
        bool artists = options.IsArtists && options.IsUseClaudeForArtists;
        bool albums = options.IsAlbums && options.IsUseClaudeForAlbums;
        return (romaji || artists || albums) && JavaString.IsBlank(options.ApiKey);
    }

    /// <summary>
    /// The message for options that need Claude when there is no API key at all (so the run is not even started), or null
    /// when the key is there or Claude is not needed.
    /// </summary>
    public static string MissingKeyMessage(StandardizeOptions options, bool keyInEnvironment)
    {
        bool romaji = options.IsLyrics && options.IsRomaji;
        bool artists = options.IsArtists && options.IsUseClaudeForArtists;
        bool albums = options.IsAlbums && options.IsUseClaudeForAlbums;
        if (!(romaji || artists || albums) || keyInEnvironment || !JavaString.IsBlank(options.ApiKey))
        {
            return null;
        }
        var uses = new List<string>();
        if (romaji)
        {
            uses.Add("romaji");
        }
        if (artists)
        {
            uses.Add("grouping the artist names");
        }
        if (albums)
        {
            uses.Add("grouping the album names");
        }
        string list = uses.Count == 1 ? uses[0] : string.Join(", ", uses.Take(uses.Count - 1)) + " and " + uses[uses.Count - 1];
        string what = char.ToUpperInvariant(list[0]) + list.Substring(1)
                + (uses.Count == 1 ? " uses Claude" : uses.Count == 2 ? " both use Claude" : " all use Claude");
        return what + ", but no Anthropic API key was found.\n\n"
                + "Type a key in Options, set the ANTHROPIC_API_KEY environment variable, or turn the Claude option off.";
    }

    // ------------------------------------------------------------------ albums

    /// <summary>The album cards worth reviewing: with a scope, only the albums the scope's songs belong to (names come from the whole folder).</summary>
    public static List<AlbumGroup> AlbumGroupsInScope(AlbumPlan plan)
    {
        if (plan.Scope == null)
        {
            return plan.Groups;
        }
        var keys = new HashSet<string>();
        foreach (SongAlbumTags song in plan.Songs)
        {
            if (plan.IsInScope(song))
            {
                AlbumGroup group = plan.GroupOf(song);
                if (group != null)
                {
                    keys.Add(group.Key);
                }
            }
        }
        return plan.Groups.Where(group => keys.Contains(group.Key)).ToList();
    }

    /// <summary>true if the card of this album is marked "please check" (the red ones): Claude is not sure of its name</summary>
    public static bool NeedsCheck(AlbumGroup group)
    {
        return group.IsLowConfidence;
    }

    /// <summary>The album cards in the order the page shows them: the ones marked "please check" first, then the others (each part keeps the plan's order).</summary>
    public static List<AlbumGroup> CheckFirst(IEnumerable<AlbumGroup> groups)
    {
        var red = new List<AlbumGroup>();
        var rest = new List<AlbumGroup>();
        foreach (AlbumGroup group in groups)
        {
            (NeedsCheck(group) ? red : rest).Add(group);
        }
        red.AddRange(rest);
        return red;
    }

    /// <summary>
    /// Whether an album card matches what was typed into the search box: the chosen name or any spelling the library uses
    /// for the album contains it, ignoring case and, as a second chance, everything that is not a letter or a digit. An
    /// empty search matches every card.
    /// </summary>
    public static bool MatchesSearch(AlbumGroup group, string query)
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
        foreach (AlbumVariant variant in group.Variants)
        {
            if (variant.Count > 0 && Matches(variant.Text, typed, letters))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether a name contains what was typed, ignoring case and, as a second chance, everything that is not a letter or a
    /// digit ("sour hires" finds "SOUR [Hi-Res]"). An empty query matches every name.
    /// </summary>
    public static bool NameMatches(string name, string query)
    {
        string typed = query == null ? "" : JavaString.Trim(query);
        return typed.Length == 0 || Matches(name, typed, LettersOnly(typed));
    }

    /// <summary>One song of an album card: its name as the cards show it, and the album tag it carries now.</summary>
    public sealed record AlbumSong(string Name, string AlbumTag);

    /// <summary>The songs the card of this album counts (its "N songs"), in the order of the library.</summary>
    public static List<AlbumSong> SongsOf(AlbumPlan plan, AlbumGroup group)
    {
        var songs = new List<AlbumSong>();
        foreach (SongAlbumTags song in plan.Songs)
        {
            AlbumGroup of = plan.GroupOf(song);
            if (of != null && of.Key == group.Key)
            {
                songs.Add(new AlbumSong(DisplayName(song.Path), song.Album ?? "(no album tag)"));
            }
        }
        return songs;
    }

    /// <summary>The line of the preview that says the songs will be put in album folders.</summary>
    public static string FolderNote(int songs)
    {
        return songs + (songs == 1 ? " song" : " songs") + " will be moved to the folder named after the album";
    }

    // ------------------------------------------------------------------ songs that are being added

    /// <summary>The line under the title of the review of an Add: the songs are not in the library until Apply.</summary>
    public static string AddCaption(int newSongs)
    {
        return SongCount(newSongs) + " to add: they are copied into your library only when you press Apply, and Back drops them";
    }

    /// <summary>The line under the title of the review when no song is new: library songs only get the lyric files of a copy that was left out.</summary>
    public static string LyricsOnlyCaption(int songs)
    {
        return SongCount(songs) + (songs == 1 ? " gets" : " get") + " the lyric files of the copy you added: they are copied into your library only when you press Apply, and Back drops them";
    }

    /// <summary>The line of the preview that says how many songs the library gets.</summary>
    public static string AddNote(int newSongs)
    {
        return SongCount(newSongs) + (newSongs == 1 ? " is" : " are") + " added to the library only when you press Apply";
    }

    /// <summary>The line of a song's card: where in the library the new song will be (the folder it arrives in).</summary>
    public static string AddLine(string libraryPath)
    {
        return AddLine(libraryPath, true);
    }

    /// <param name="withFolder">false when the folder tree says where the song goes: the folder of the path is the one it is copied to, not the one the album folders put it in</param>
    public static string AddLine(string libraryPath, bool withFolder)
    {
        string parent = Path.GetDirectoryName(libraryPath);
        string folder = parent == null || !withFolder ? "" : Path.GetFileName(parent);
        return folder.Length == 0 ? "ADD:  new song" : "ADD:  new song, into the folder \"" + folder + "\"";
    }

    // ------------------------------------------------------------------ the folder tree

    /// <summary>The headline of the folder changes: how many folders change.</summary>
    public static string FoldersHeadline(FolderPreview preview)
    {
        int folders = preview.FoldersChanged;
        return folders == 0 ? "No folders change" : folders + (folders == 1 ? " folder changes" : " folders change");
    }

    /// <summary>The lines under the headline: what happens to the songs, and how to read the tree.</summary>
    public static string FoldersDetail(FolderPreview preview)
    {
        var parts = new List<string>();
        if (preview.SongsMoved != 0)
        {
            parts.Add(SongCount(preview.SongsMoved) + (preview.SongsMoved == 1 ? " moves" : " move") + " to the folder named after the album");
        }
        if (preview.NewSongs != 0)
        {
            parts.Add(SongCount(preview.NewSongs) + (preview.NewSongs == 1 ? " is" : " are") + " added to the library");
        }
        if (preview.SongsStuck != 0)
        {
            parts.Add(SongCount(preview.SongsStuck) + (preview.SongsStuck == 1 ? " stays" : " stay") + " where it is: a song with the same name is in its folder already");
        }
        parts.Add("Only the folders that change are shown. Change an album or an artist and this follows.");
        return string.Join("\n", parts);
    }

    /// <summary>The part of the result of Apply about the songs that were put into the library.</summary>
    public static string AddedSummary(int songs)
    {
        return songs <= 0 ? "" : "Added " + SongCount(songs) + " to the library.";
    }

    /// <summary>The result message of an Apply that added songs: that part first.</summary>
    public static string WithAdded(string added, string result)
    {
        string head = added == null ? "" : JavaString.Trim(added);
        string rest = result == null ? "" : JavaString.Trim(result);
        return head.Length == 0 ? rest : rest.Length == 0 ? head : head + "\n\n" + rest;
    }
}
