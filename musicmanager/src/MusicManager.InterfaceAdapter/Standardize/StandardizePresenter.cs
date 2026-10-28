using System.Text;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.InterfaceAdapter.Standardize;

/// <summary>
/// Turns the standardize use cases' results into the display-ready StandardizeState and
/// fires a property change per kind of event (see StandardizeViewModel's constants).
/// Interactors run on background threads, so every list is built fresh and then swapped in.
/// </summary>
public class StandardizePresenter : IStandardizeLyricsOutputBoundary, IStandardizeArtistsOutputBoundary,
        IStandardizeAlbumsOutputBoundary
{
    private readonly StandardizeViewModel _viewModel;

    public StandardizePresenter(StandardizeViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    // ---- lyrics ----

    public void Present(StandardizeLyricsOutputData outputData)
    {
        bool dry = outputData.IsDryRun;
        List<string[]> rows = new List<string[]>();
        foreach (SongLyricsResult r in outputData.Results)
        {
            int removed = r.DeletedFiles.Count;
            string removedText = removed == 0 ? ""
                : (dry ? "Will delete " : "Deleted ") + removed + " deprecated " + (removed == 1 ? "file" : "files");
            string level = r.Action == LyricsAction.Failed ? "error" : (r.Detail.Length == 0 ? "" : "note");
            string what = dry ? r.Action.GetPreviewDescription() : r.Action.GetDescription();
            rows.Add(new string[] { DisplayName(r.AudioPath), what, r.Detail, removedText, level });
        }

        StandardizeState state = _viewModel.State;
        state.LyricsResult = outputData;
        state.LyricsRows = rows;
        state.LyricsSummary = LyricsSummary(outputData);
        _viewModel.FirePropertyChange(StandardizeViewModel.Lyrics);
    }

    public void PresentProgress(int done, int total, string label)
    {
        FireProgress("Lyrics: " + label, done, total);
    }

    private string LyricsSummary(StandardizeLyricsOutputData output)
    {
        bool dry = output.IsDryRun;
        int fromTtml = output.Count(LyricsAction.ConvertedFromTtml);
        int fromHmrc = output.Count(LyricsAction.ConvertedFromHmrc);
        int normalized = output.Count(LyricsAction.NormalizedLrc);
        int lineFromOld = output.Count(LyricsAction.LineSyncedFromDeprecated);
        int already = output.Count(LyricsAction.AlreadyStandard);
        int kept = output.Count(LyricsAction.KeptLineSynced);
        int none = output.Count(LyricsAction.NoLyrics);
        int failed = output.Count(LyricsAction.Failed);
        int deleted = output.DeletedFileCount;

        // two lines: what happens to the library, then the breakdown (zero counts are left out)
        int built = fromTtml + fromHmrc + normalized + lineFromOld;
        string headline = built + (built == 1 ? " song " : " songs ") + (dry ? "will get" : "got") + " a standard .lrc, "
            + deleted + " deprecated " + (deleted == 1 ? "file " : "files ") + (dry ? "will be deleted" : "deleted");

        List<string> parts = new List<string>();
        AddIfPositive(parts, fromTtml, " from .ttml");
        AddIfPositive(parts, fromHmrc, " from .hmrc");
        AddIfPositive(parts, normalized, " reformatted");
        AddIfPositive(parts, lineFromOld, " line-synced");
        int romajiOnly = output.Count(LyricsAction.RomajiAdded);
        AddIfPositive(parts, romajiOnly, romajiOnly == 1 ? " standard file gets romaji" : " standard files get romaji");
        AddIfPositive(parts, already, " already standard");
        AddIfPositive(parts, kept, " keep their line-synced .lrc");
        AddIfPositive(parts, none, " without lyrics");
        AddIfPositive(parts, failed, " failed (left untouched)");

        string text = headline + "\n" + Wrap(parts, 3);
        string romaji = RomajiSummaryText(output.Romaji, dry);
        return romaji.Length == 0 ? text : text + "\n" + romaji;
    }

    /// <summary>
    /// the romaji lines of the summary: how many lines are added and how many old Chinese-character ones are replaced
    /// (in a preview as well, so the user sees it before anything is written), and in a preview what Claude would be
    /// asked, afterwards what it did
    /// </summary>
    private static string RomajiSummaryText(RomajiSummary romaji, bool dry)
    {
        if (!romaji.IsRequested)
        {
            return "";
        }
        string changes = RomajiChanges(romaji.LinesAdded, romaji.LinesReplaced, dry);
        if (dry)
        {
            int toAsk = romaji.UniqueLines - romaji.CachedLines;
            if (romaji.UniqueLines == 0 && changes.Length == 0)
            {
                return "Romaji: no Korean or Japanese lines need it";
            }
            string asking;
            if (romaji.UniqueLines == 0)
            {
                asking = "";
            }
            else if (toAsk <= 0)
            {
                asking = "Claude will not be needed: every line was saved from an earlier run";
            }
            else
            {
                asking = "API will be called with ~" + toAsk + (toAsk == 1 ? " different line" : " different lines")
                    + " in ~" + romaji.Requests + (romaji.Requests == 1 ? " request" : " requests");
                if (romaji.CachedLines > 0)
                {
                    asking += " (" + romaji.CachedLines + " already saved from before)";
                }
            }
            if (changes.Length == 0)
            {
                return "Romaji: " + asking;
            }
            return "Romaji: " + changes + (asking.Length == 0 ? "" : "\n" + asking);
        }
        string text = "Romaji: " + (changes.Length == 0 ? "0 lines added" : changes);
        if (romaji.LinesMissing > 0)
        {
            text += ", " + romaji.LinesMissing + " missing (run it again to finish)";
        }
        if (romaji.StoppedBecause != null)
        {
            text += ". " + romaji.StoppedBecause;
        }
        return text;
    }

    /// <summary>
    /// "18 lines added, 12 old Chinese-character lines replaced with Latin letters": the two kinds of line are told
    /// apart and none is counted in both. The summary's "lines added" counts the replaced lines too, so the lines that
    /// had no romaji at all are the difference. Empty when nothing is added or replaced. (Java: package-private)
    /// </summary>
    public static string RomajiChanges(int linesAdded, int linesReplaced, bool dry)
    {
        int replaced = Math.Max(0, Math.Min(linesReplaced, linesAdded));
        int added = Math.Max(0, linesAdded) - replaced;
        string addedVerb = dry ? " will be added" : " added";
        string replacedVerb = dry ? " will be replaced" : " replaced";
        List<string> parts = new List<string>();
        if (added > 0)
        {
            parts.Add(added + (added == 1 ? " line" : " lines") + addedVerb);
        }
        if (replaced > 0)
        {
            parts.Add(replaced + (replaced == 1 ? " old Chinese-character line" : " old Chinese-character lines")
                + replacedVerb + " with Latin letters");
        }
        return string.Join(", ", parts);
    }

    /// <summary>the parts joined with a dot between them, a new line after every perLine of them, so none runs off the window</summary>
    private static string Wrap(List<string> parts, int perLine)
    {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                text.Append(i % perLine == 0 ? "\n" : "  ·  ");
            }
            text.Append(parts[i]);
        }
        return text.ToString();
    }

    private static void AddIfPositive(List<string> parts, int count, string label)
    {
        if (count > 0)
        {
            parts.Add(count + label);
        }
    }

    // ---- artists ----

    public void PresentAnalysis(StandardizeArtistsAnalysisOutputData outputData)
    {
        ArtistPlan plan = outputData.Plan;

        StandardizeState state = _viewModel.State;
        state.ArtistPlan = plan;
        state.ArtistUpdates = outputData.Updates;
        state.ArtistRenameCount = outputData.Renames.Count;
        state.ArtistRenames = outputData.Renames;
        state.ArtistsSummary = AnalysisSummary(plan, outputData.Updates.Count);
        _viewModel.FirePropertyChange(StandardizeViewModel.ArtistsAnalysis);
    }

    /// <summary>
    /// two lines: what changes, then how the names were grouped. With a scope (some songs were chosen) the sentence is
    /// about those songs only, while the artists are still those of the whole folder.
    /// </summary>
    private string AnalysisSummary(ArtistPlan plan, int changedSongs)
    {
        int artists = plan.Groups.Count;
        string found = artists + (artists == 1 ? " artist" : " artists") + " found";
        string headline;
        if (plan.Scope == null)
        {
            int songs = plan.Songs.Count;
            headline = found + ", " + changedSongs + " of " + songs + (songs == 1 ? " song" : " songs")
                + " will change";
        }
        else
        {
            int selected = plan.ScopedSongCount;
            headline = found + " in the folder, " + changedSongs + " of " + selected + " selected "
                + (selected == 1 ? "song" : "songs") + " will change";
        }
        string how = plan.IsUsedAi ? "Grouped by AI. Check the names marked in red."
            : "Grouped without AI, so review every name carefully.";
        if (plan.Warnings.Count != 0)
        {
            how += "  ·  " + plan.Warnings.Count + " warning(s)";
        }
        return headline + "\n" + how;
    }

    public void PresentApplied(StandardizeArtistsApplyOutputData outputData)
    {
        string text = "Updated " + outputData.UpdatedCount + " songs, " + outputData.UnchangedCount
            + " were already consistent";
        if (outputData.FailedPaths.Count != 0)
        {
            text += ", " + outputData.FailedPaths.Count + " could not be written";
        }

        StandardizeState state = _viewModel.State;
        state.ArtistsAppliedSummary = text;
        _viewModel.FirePropertyChange(StandardizeViewModel.ArtistsApplied);
    }

    public void PresentRenamed(StandardizeArtistsRenameOutputData outputData)
    {
        List<string> parts = new List<string>();
        if (outputData.RenamedCount > 0)
        {
            parts.Add("Renamed " + outputData.RenamedCount + (outputData.RenamedCount == 1 ? " song's files" : " songs' files")
                + " to \"Artist - Title\"");
        }
        if (outputData.Skipped.Count != 0)
        {
            List<string> why = outputData.Skipped.Take(5)
                .Select(skipped => Path.GetFileName(skipped.AudioPath) + ": " + skipped.Detail).ToList();
            string more = outputData.Skipped.Count > 5 ? "\n... and " + (outputData.Skipped.Count - 5) + " more" : "";
            parts.Add(outputData.Skipped.Count + (outputData.Skipped.Count == 1 ? " song was" : " songs were")
                + " left as they are:\n" + string.Join("\n", why) + more);
        }

        StandardizeState state = _viewModel.State;
        state.ArtistsRenamedSummary = string.Join(", ", parts);
        _viewModel.FirePropertyChange(StandardizeViewModel.ArtistsRenamed);
    }

    public void PresentProgress(string message, int done, int total)
    {
        FireProgress(message, done, total);
    }

    public void PresentError(string message)
    {
        StandardizeState state = _viewModel.State;
        state.ErrorMessage = message;
        _viewModel.FirePropertyChange(StandardizeViewModel.Error);
    }

    // ---- albums ----

    public void PresentAlbumsAnalysis(StandardizeAlbumsAnalysisOutputData outputData)
    {
        AlbumPlan plan = outputData.Plan;

        StandardizeState state = _viewModel.State;
        state.AlbumPlan = plan;
        state.AlbumUpdates = outputData.Updates;
        state.AlbumFolderMoves = outputData.Moves;
        state.AlbumsSummary = AlbumsSummary(plan, outputData.Updates, outputData.Moves);
        _viewModel.FirePropertyChange(StandardizeViewModel.AlbumsAnalysis);
    }

    /// <summary>
    /// two lines: what changes, then how the names were grouped and what happens to the folders. With a scope (some songs
    /// were chosen) the sentence is about those songs only, while the albums are still those of the whole folder.
    /// </summary>
    private static string AlbumsSummary(AlbumPlan plan, List<SongAlbumUpdate> updates, List<AlbumFolderMove> moves)
    {
        int albums = plan.Groups.Count;
        string found = albums + (albums == 1 ? " album" : " albums") + " found";
        var changed = new HashSet<string>(updates.Select(update => update.AudioPath));
        foreach (AlbumFolderMove move in moves)
        {
            changed.UnionWith(move.AudioPaths);
        }
        string headline;
        if (plan.Scope == null)
        {
            int songs = plan.Songs.Count;
            headline = found + ", " + changed.Count + " of " + songs + (songs == 1 ? " song" : " songs") + " will change";
        }
        else
        {
            int selected = plan.ScopedSongCount;
            headline = found + " in the folder, " + changed.Count + " of " + selected + " selected "
                + (selected == 1 ? "song" : "songs") + " will change";
        }

        string how = plan.IsUsedAi ? "Grouped by AI. Check the albums marked in red."
            : "Grouped without AI, so review every name carefully.";
        int moving = moves.Sum(move => move.AudioPaths.Count);
        if (moving != 0)
        {
            how += "\n" + moving + (moving == 1 ? " song moves" : " songs move") + " to the folder named after "
                + (moving == 1 ? "its album" : "their albums");
        }
        if (plan.Warnings.Count != 0)
        {
            how += "  ·  " + plan.Warnings.Count + " warning(s)";
        }
        return headline + "\n" + how;
    }

    public void PresentAlbumsApplied(StandardizeAlbumsApplyOutputData outputData)
    {
        string text = "Updated " + outputData.UpdatedCount + " album " + (outputData.UpdatedCount == 1 ? "tag" : "tags") + ", "
            + outputData.UnchangedCount + " were already right";
        if (outputData.FailedPaths.Count != 0)
        {
            text += ", " + outputData.FailedPaths.Count + " could not be written";
        }

        StandardizeState state = _viewModel.State;
        state.AlbumsAppliedSummary = text;
        _viewModel.FirePropertyChange(StandardizeViewModel.AlbumsApplied);
    }

    public void PresentAlbumsMoved(StandardizeAlbumsMoveOutputData outputData)
    {
        var parts = new List<string>();
        if (outputData.SongsMoved > 0)
        {
            string renamed = outputData.FoldersRenamed == 0 ? ""
                : " (" + outputData.FoldersRenamed + (outputData.FoldersRenamed == 1 ? " folder was" : " folders were") + " renamed)";
            parts.Add("Moved " + outputData.SongsMoved + (outputData.SongsMoved == 1 ? " song" : " songs")
                + " into the folders named after their albums" + renamed);
        }
        if (outputData.Skipped.Count != 0)
        {
            List<string> why = outputData.Skipped.Take(5)
                .Select(skipped => Path.GetFileName(skipped.AudioPath) + ": " + skipped.Detail).ToList();
            string more = outputData.Skipped.Count > 5 ? "\n... and " + (outputData.Skipped.Count - 5) + " more" : "";
            parts.Add(outputData.Skipped.Count + (outputData.Skipped.Count == 1 ? " song was" : " songs were")
                + " left in their folders:\n" + string.Join("\n", why) + more);
        }

        StandardizeState state = _viewModel.State;
        state.AlbumMovedPaths = outputData.MovedPaths;
        state.AlbumsMovedSummary = string.Join(", ", parts);
        _viewModel.FirePropertyChange(StandardizeViewModel.AlbumsMoved);
    }

    // ---- shared ----

    private void FireProgress(string message, int done, int total)
    {
        StandardizeState state = _viewModel.State;
        state.ProgressMessage = message;
        state.ProgressDone = done;
        state.ProgressTotal = total;
        _viewModel.FirePropertyChange(StandardizeViewModel.Progress);
    }

    /// <summary>"Album folder / Song name" so identically named songs in different albums are told apart</summary>
    private static string DisplayName(string audioPath)
    {
        string name = Path.GetFileName(audioPath);
        int dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            name = name.Substring(0, dot);
        }
        string parent = Path.GetDirectoryName(audioPath);
        return string.IsNullOrEmpty(parent) ? name : Path.GetFileName(parent) + " / " + name;
    }
}
