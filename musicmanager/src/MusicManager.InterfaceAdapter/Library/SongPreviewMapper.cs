using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using Tone = MusicManager.InterfaceAdapter.Library.SongStatus.Tone;

namespace MusicManager.InterfaceAdapter.Library;

/// <summary>
/// Turns what the use cases found for ONE song into what the song window shows (see <see cref="SongLyricsPreview"/> and
/// <see cref="SongTagsPreview"/>). Pure: no UI, no I/O. Plain ASCII only, and the same wording rules as
/// <see cref="LibraryStatusMapper"/>: lines that get romaji and old Chinese-character lines that are replaced are told
/// apart and never counted twice.
/// </summary>
public static class SongPreviewMapper
{
    /// <summary>how a tag value that is missing or blank is shown</summary>
    internal const string EmptyValue = "(empty)";

    /// <summary>the artist tag fields in the order they are shown, with the label of each</summary>
    private static readonly string[] FieldOrder = { "ARTIST", "ALBUMARTIST", "ARTISTS" };
    private static readonly string[] FieldLabels = { "Artist", "Album artist", "Artists" };

    // ---- lyrics ----

    /// <summary>the lyrics preview of a song from a detailed dry-run result (nothing was written)</summary>
    public static SongLyricsPreview Lyrics(SongLyricsResult result, StandardizeOptions options)
    {
        if (result == null)
        {
            return new SongLyricsPreview(null, "", null, null, "Nothing to preview.", Tone.Muted, 0);
        }

        List<SongLyricsPreview.FileLine> files = FileLines(result, options);

        List<SongLyricsPreview.LineLine> lines = new List<SongLyricsPreview.LineLine>();
        int added = 0;
        int replaced = 0;
        int withoutRomaji = 0;
        foreach (LyricLineChange change in OrEmpty(result.LineChanges))
        {
            if (change == null)
            {
                continue;
            }
            if (change.GetKind() == LyricLineChange.Kind.RomajiReplaced)
            {
                replaced++;
            }
            else
            {
                added++;
            }
            if (change.NewRomaji == null)
            {
                withoutRomaji++;
            }
            lines.Add(new SongLyricsPreview.LineLine(Time(change.TimeMs), change.Original,
                change.OldRomaji, change.NewRomaji));
        }

        List<string> notes = new List<string>();
        string detail = result.Detail == null ? "" : JavaString.Trim(result.Detail);
        if (detail.Length != 0 && result.Action != LyricsAction.Failed)
        {
            notes.Add(detail);
        }

        if (files.Count != 0 || lines.Count != 0)
        {
            return new SongLyricsPreview(files, LinesTitle(added, replaced), lines, notes, "", Tone.Active,
                withoutRomaji);
        }
        bool bad = result.Action == LyricsAction.Failed;
        return new SongLyricsPreview(files, "", lines, notes, NothingToShow(result.Action, detail),
            bad ? Tone.Bad : Tone.Muted, 0);
    }

    /// <summary>the lyrics part is off in Options (or romaji needs lyrics): one sentence says so</summary>
    public static SongLyricsPreview LyricsOff(string message)
    {
        return new SongLyricsPreview(null, "", null, null, message, Tone.Muted, 0);
    }

    /// <summary>the preview could not be made: the reason is shown as it is</summary>
    public static SongLyricsPreview LyricsFailed(string reason)
    {
        return new SongLyricsPreview(null, "", null, null, reason, Tone.Bad, 0);
    }

    /// <summary>the .lrc that is written, then the deprecated files that go; empty when the song's files stay as they are</summary>
    private static List<SongLyricsPreview.FileLine> FileLines(SongLyricsResult result, StandardizeOptions options)
    {
        List<SongLyricsPreview.FileLine> files = new List<SongLyricsPreview.FileLine>();
        string written = result.WrittenLrcPath;
        if (written != null && !JavaString.IsBlank(written))
        {
            files.Add(new SongLyricsPreview.FileLine(SongLyricsPreview.FileLine.Kind.Write, FileName(written),
                WriteNote(result.Action)));
        }
        string deleteNote = options != null && options.IsRecycleBin ? "goes to the Recycle Bin" : "deleted";
        foreach (string path in OrEmpty(result.DeletedFiles))
        {
            if (path != null && !JavaString.IsBlank(path))
            {
                files.Add(new SongLyricsPreview.FileLine(SongLyricsPreview.FileLine.Kind.Delete, FileName(path),
                    deleteNote));
            }
        }
        return files;
    }

    private static string WriteNote(LyricsAction action)
    {
        switch (action)
        {
            case LyricsAction.ConvertedFromTtml:
                return "new word-synced lyrics, built from the .ttml";
            case LyricsAction.ConvertedFromHmrc:
                return "new word-synced lyrics, built from the .hmrc";
            case LyricsAction.NormalizedLrc:
                return "rewritten in the standard word-synced format";
            case LyricsAction.LineSyncedFromDeprecated:
                return "new line-synced lyrics, built from the old file";
            case LyricsAction.RomajiAdded:
                return "romaji updated";
            default:
                return "written";
        }
    }

    /// <summary>the heading over the changed lines; the two kinds never overlap. Empty when no line changes.</summary>
    private static string LinesTitle(int added, int replaced)
    {
        string addText = added == 0 ? "" : Plural(added, "line gets romaji", "lines get romaji");
        string replaceText = replaced == 0 ? ""
            : Plural(replaced, "old Chinese-character line is", "old Chinese-character lines are")
                + " replaced with Latin letters";
        if (added > 0 && replaced > 0)
        {
            return addText + ", " + replaceText;
        }
        return added > 0 ? addText : replaceText;
    }

    /// <summary>the one sentence for a song whose lyrics have nothing to show</summary>
    private static string NothingToShow(LyricsAction action, string detail)
    {
        switch (action)
        {
            case LyricsAction.AlreadyStandard:
                return "Nothing changes: in standard format (word-synced)";
            case LyricsAction.KeptLineSynced:
                return "Nothing changes: in standard format (line-synced)";
            case LyricsAction.NoLyrics:
                return "There is no lyric file in the same folder";
            case LyricsAction.Failed:
                return "The lyrics cannot be processed: " + (detail.Length == 0 ? "The lyric files could not be used" : detail);
            default:
                return "Nothing changes in the lyrics.";
        }
    }

    /// <summary>a time in the song as minutes and seconds ("0:07", "1:05", "12:30"); minutes keep counting past an hour ("61:05") (Java: package-private)</summary>
    internal static string Time(long timeMs)
    {
        long seconds = Math.Max(0L, timeMs) / 1000;
        return (seconds / 60) + ":" + (seconds % 60 < 10 ? "0" : "") + (seconds % 60);
    }

    // ---- tags ----

    /// <summary>the tag changes of this song; updates are the artist plan's updates for this song only (may be empty)</summary>
    public static SongTagsPreview Tags(IEnumerable<SongArtistUpdate> updates, StandardizeOptions options)
    {
        List<SongTagsPreview.TagLine> changes = new List<SongTagsPreview.TagLine>();
        // the three artist fields come in this order, whatever order the plan listed them in; others follow
        for (int rank = 0; rank <= FieldOrder.Length; rank++)
        {
            foreach (SongArtistUpdate update in OrEmpty(updates))
            {
                if (update == null)
                {
                    continue;
                }
                foreach (FieldChange change in OrEmpty(update.Changes))
                {
                    if (change != null && Rank(change.Field) == rank)
                    {
                        AddTagLine(changes, change);
                    }
                }
            }
        }
        return new SongTagsPreview(changes, changes.Count == 0 ? "Nothing changes in the tags." : "");
    }

    /// <summary>artist names are off in Options</summary>
    public static SongTagsPreview TagsOff()
    {
        return new SongTagsPreview(null, "Artist names are off in Options.");
    }

    /// <summary>the artist preview could not be made: the reason is shown as it is</summary>
    public static SongTagsPreview TagsFailed(string reason)
    {
        return new SongTagsPreview(null, reason);
    }

    /// <summary>the place of a field in the list: 0..2 for the known ones, after them for anything else</summary>
    private static int Rank(string field)
    {
        string key = field == null ? "" : JavaString.Trim(field).ToUpperInvariant();
        for (int i = 0; i < FieldOrder.Length; i++)
        {
            if (FieldOrder[i] == key)
            {
                return i;
            }
        }
        return FieldOrder.Length;
    }

    /// <summary>one line per change; a before and after that read the same are no change and are left out</summary>
    private static void AddTagLine(List<SongTagsPreview.TagLine> changes, FieldChange change)
    {
        string before = Shown(change.Before);
        string after = Shown(change.After);
        if (before != after)
        {
            changes.Add(new SongTagsPreview.TagLine(Label(change.Field), before, after));
        }
    }

    private static string Label(string field)
    {
        int rank = Rank(field);
        if (rank < FieldLabels.Length)
        {
            return FieldLabels[rank];
        }
        string name = field == null ? "" : JavaString.Trim(field).Replace('_', ' ');
        if (name.Length == 0)
        {
            return "Tag";
        }
        return name.Substring(0, 1).ToUpperInvariant() + name.Substring(1).ToLowerInvariant();
    }

    private static string Shown(string value)
    {
        return value == null || JavaString.IsBlank(value) ? EmptyValue : value;
    }

    // ---- helpers ----

    /// <summary>the file name without folders, whichever way the path is written</summary>
    private static string FileName(string path)
    {
        int cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return cut < 0 ? path : path.Substring(cut + 1);
    }

    private static IEnumerable<T> OrEmpty<T>(IEnumerable<T> list)
    {
        return list == null ? new List<T>() : list;
    }

    private static string Plural(int count, string one, string many)
    {
        return count + " " + (count == 1 ? one : many);
    }
}
