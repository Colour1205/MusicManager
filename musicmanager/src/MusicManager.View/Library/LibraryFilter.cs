using System.Globalization;
using System.Text;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;

namespace MusicManager.View.Library;

/// <summary>
/// The pure logic of the library page, kept out of the UI so it can be tested: which songs a search text and a filter
/// chip let through, how many songs each chip would show, and the texts the page builds from numbers and options.
/// </summary>
public static class LibraryFilter
{
    /// <summary>The filter chips, in the order they are shown.</summary>
    public enum Chip
    {
        All,
        NeedsWork,
        Standard,
        LineSynced,
        NoLyrics,
        Problems
    }

    /// <summary>The text of a chip, the Java <c>Chip.label()</c>.</summary>
    public static string Label(this Chip chip)
    {
        switch (chip)
        {
            case Chip.All: return "All";
            case Chip.NeedsWork: return "Needs work";
            case Chip.Standard: return "Standard";
            case Chip.LineSynced: return "Line-synced";
            case Chip.NoLyrics: return "No lyrics";
            case Chip.Problems: return "Problems only";
            default: return null;
        }
    }

    /// <summary>
    /// True if a song of this status category is shown by the chip (a song that is not checked yet is only in All);
    /// the Java <c>Chip.accepts(category)</c>.
    /// </summary>
    public static bool Accepts(this Chip chip, SongStatus.Category category)
    {
        switch (chip)
        {
            case Chip.All:
                return true;
            case Chip.NeedsWork:
                return category == SongStatus.Category.NeedsWork;
            case Chip.Standard:
                return category == SongStatus.Category.Standard;
            case Chip.LineSynced:
                return category == SongStatus.Category.LineSynced;
            case Chip.NoLyrics:
                return category == SongStatus.Category.NoLyrics;
            case Chip.Problems:
                return category == SongStatus.Category.Problem;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ search and chips

    /// <summary>
    /// The songs that the search text and the chip let through (both must agree). The search is case-insensitive and
    /// ignores accents, and every word of it must be found somewhere in the title, an artist, the album or the file name.
    /// </summary>
    /// <param name="search">What the user typed; null or blank = every song.</param>
    /// <param name="statuses">The status of each song by audio path; a song without an entry counts as not checked yet.</param>
    public static Func<Music, bool> Predicate(string search, Chip? chip, IReadOnlyDictionary<string, SongStatus> statuses)
    {
        string[] terms = Terms(search);
        Chip selected = chip ?? Chip.All;
        if (terms.Length == 0 && selected == Chip.All)
        {
            return song => true;
        }
        return song => MatchesChip(song, selected, statuses) && MatchesTerms(song, terms);
    }

    /// <summary>
    /// How many songs each chip would show for this search text. The counts follow the search (not the selected chip), so
    /// the chips tell what is in each group of the songs that are being searched for.
    /// </summary>
    public static Dictionary<Chip, int> Counts(IEnumerable<Music> songs, IReadOnlyDictionary<string, SongStatus> statuses,
            string search)
    {
        var counts = new Dictionary<Chip, int>();
        foreach (Chip chip in Enum.GetValues<Chip>())
        {
            counts[chip] = 0;
        }
        string[] terms = Terms(search);
        foreach (Music song in songs)
        {
            if (!MatchesTerms(song, terms))
            {
                continue;
            }
            SongStatus.Category category = CategoryOf(song, statuses);
            foreach (Chip chip in Enum.GetValues<Chip>())
            {
                if (chip.Accepts(category))
                {
                    counts[chip] = counts[chip] + 1;
                }
            }
        }
        return counts;
    }

    /// <summary>True if any song has a problem, which is when the Problems only chip is worth showing.</summary>
    public static bool HasProblems(IEnumerable<Music> songs, IReadOnlyDictionary<string, SongStatus> statuses)
    {
        foreach (Music song in songs)
        {
            if (CategoryOf(song, statuses) == SongStatus.Category.Problem)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True if the song's title, artists, album or file name contain every word of the search text.</summary>
    public static bool MatchesSearch(Music song, string search)
    {
        return MatchesTerms(song, Terms(search));
    }

    private static bool MatchesChip(Music song, Chip chip, IReadOnlyDictionary<string, SongStatus> statuses)
    {
        return chip.Accepts(CategoryOf(song, statuses));
    }

    private static SongStatus.Category CategoryOf(Music song, IReadOnlyDictionary<string, SongStatus> statuses)
    {
        SongStatus status = null;
        if (statuses != null && song.Path != null)
        {
            statuses.TryGetValue(song.Path, out status);
        }
        return status == null ? SongStatus.Category.Unknown : status.GetCategory();
    }

    private static bool MatchesTerms(Music song, string[] terms)
    {
        if (terms.Length == 0)
        {
            return true;
        }
        string haystack = Haystack(song);
        foreach (string term in terms)
        {
            if (!haystack.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string[] Terms(string search)
    {
        string normalized = Normalize(search);
        // Java's \s is the ASCII white space only: the other spaces were already folded to U+0020 by Normalize
        return normalized.Length == 0 ? Array.Empty<string>() : JavaString.Split(normalized, "[ \\t\\n\\x0B\\f\\r]+");
    }

    private static string Haystack(Music song)
    {
        var text = new StringBuilder();
        text.Append(song.Title ?? "").Append(' ');
        if (song.Artists != null)
        {
            foreach (string artist in song.Artists)
            {
                text.Append(artist).Append(' ');
            }
        }
        text.Append(song.Album ?? "").Append(' ');
        text.Append(song.Path == null ? "" : Path.GetFileName(song.Path));
        return Normalize(text.ToString());
    }

    /// <summary>
    /// Lower case, accents removed ("Beyoncé" -> "beyonce") and compatibility forms folded (full-width Latin letters
    /// become plain ones). Hangul and kana are put back together afterwards, so が is still not か.
    /// </summary>
    public static string Normalize(string text)
    {
        if (text == null)
        {
            return "";
        }
        string trimmed = JavaString.Trim(text);
        if (IsAscii(trimmed))
        {
            return trimmed.ToLowerInvariant(); // nothing to decompose: most of a library is plain letters
        }
        string decomposed = trimmed.Normalize(NormalizationForm.FormKD);
        var output = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (c >= '̀' && c <= 'ͯ')
            {
                continue; // combining accents; other marks (the kana dakuten, say) are letters of their own
            }
            output.Append(c);
        }
        return output.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static bool IsAscii(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x80)
            {
                return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------ texts

    /// <summary>"12,345"</summary>
    public static string FormatCount(int number)
    {
        return number.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>The chip's text: "Needs work (1,411)", or just "Needs work" while the count is not known.</summary>
    public static string ChipLabel(Chip chip, int? count)
    {
        return count == null ? chip.Label() : chip.Label() + " (" + FormatCount(count.Value) + ")";
    }

    /// <summary>"1,698 songs - 312 selected", or "212 of 1,698 songs - 3 selected" when the search or a chip hides songs.</summary>
    public static string CountCaption(int shown, int total, int selected)
    {
        if (total == 0)
        {
            return "No songs";
        }
        string songs = shown == total ? FormatCount(total) + (total == 1 ? " song" : " songs")
                : FormatCount(shown) + " of " + FormatCount(total) + (total == 1 ? " song" : " songs");
        return songs + " - " + (selected == 0 ? "none selected" : FormatCount(selected) + " selected");
    }

    /// <summary>
    /// The text of the Process button: "Process 12 selected", or, when nothing is selected, "Process all 1,698 songs"
    /// (or "Process all 212 shown songs" when the search or a chip hides some).
    /// </summary>
    public static string ProcessText(int selected, int shown, int total)
    {
        if (selected > 0)
        {
            return "Process " + FormatCount(selected) + " selected";
        }
        if (total == 0 || shown == 0)
        {
            return "Process songs";
        }
        if (shown == total)
        {
            return total == 1 ? "Process the song" : "Process all " + FormatCount(total) + " songs";
        }
        return shown == 1 ? "Process the shown song" : "Process all " + FormatCount(shown) + " shown songs";
    }

    /// <summary>
    /// What Process will do with the current Options, as a short line:
    /// "Standardize: lyrics, romaji, artists" or "Standardize: nothing selected in Options".
    /// </summary>
    /// <param name="convertAvailable">AppFeatures.LegacyConvertLrc; only then does the Convert formats mode exist.</param>
    /// <param name="formats">The target formats of the Convert formats mode (only read in that mode).</param>
    public static string OptionsCaption(StandardizeOptions options, IReadOnlyCollection<string> formats, bool convertAvailable)
    {
        if (convertAvailable && options.GetMode() == StandardizeOptions.Mode.Convert)
        {
            return formats == null || formats.Count == 0 ? "Convert formats: no format selected"
                    : "Convert formats: " + string.Join(", ", formats);
        }
        var parts = new StringBuilder();
        if (options.IsLyrics)
        {
            parts.Append("lyrics");
            if (options.IsRomaji)
            {
                parts.Append(", romaji");
            }
        }
        if (options.IsArtists)
        {
            parts.Append(parts.Length == 0 ? "artists" : ", artists");
        }
        return parts.Length == 0 ? "Standardize: nothing selected in Options" : "Standardize: " + parts;
    }
}
