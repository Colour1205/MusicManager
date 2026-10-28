using MusicManager.UseCase.StandardizeAlbums;

namespace MusicManager.DataAccess;

/// <summary>
/// Puts songs in the folder named after their album (see <see cref="AlbumFolderMove"/>). When the songs are all of their
/// folder it is renamed as a whole, so the cover and everything else in it go along; otherwise each song is moved with the
/// lyric files that share its name (the same group of files <see cref="SongRenamer"/> keeps together), into the folder if it
/// is there already. Nothing is overwritten, a song is moved whole or not at all, and a folder that is empty afterwards is
/// removed.
/// </summary>
public static class AlbumFolders
{
    private static readonly string[] LyricExtensions = { ".lrc", ".ttml", ".hmrc" };

    public static AlbumFolderMoveResult Move(AlbumFolderMove move)
    {
        try
        {
            return MoveSongs(move);
        }
        catch (Exception e)
        {
            string reason = string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
            return new AlbumFolderMoveResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    move.AudioPaths.Select(path => new AlbumFolderMoveResult.Skipped(path, reason)).ToList(), false);
        }
    }

    private static AlbumFolderMoveResult MoveSongs(AlbumFolderMove move)
    {
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<AlbumFolderMoveResult.Skipped>();
        string source = Path.TrimEndingDirectorySeparator(move.SourceDirectory);
        if (!Directory.Exists(source))
        {
            return AllSkipped(move, "the folder is not there any more");
        }
        string parent = Path.GetDirectoryName(source);
        if (string.IsNullOrEmpty(parent))
        {
            return AllSkipped(move, "its folder has no folder around it to put the album folder in");
        }
        string target = Path.Combine(parent, move.TargetName);
        bool sameFolder = string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        bool targetTaken = !sameFolder && (Directory.Exists(target) || File.Exists(target));

        // the folder is the album: rename it, with everything in it
        if (sameFolder || (move.IsWholeFolder && !targetTaken))
        {
            int longest = LongestNewPath(source, target);
            if (longest > SongRenamer.MaxPathLength)
            {
                return AllSkipped(move, "the new folder name would make a path " + longest + " characters long (Windows allows "
                        + SongRenamer.MaxPathLength + ")");
            }
            RenameDirectory(source, target, sameFolder);
            foreach (string path in move.AudioPaths)
            {
                moved[path] = Path.Combine(target, Path.GetFileName(path));
            }
            return new AlbumFolderMoveResult(moved, skipped, true);
        }

        // song by song, into the album's folder
        bool created = false;
        foreach (string audioPath in move.AudioPaths)
        {
            if (!File.Exists(audioPath))
            {
                skipped.Add(new AlbumFolderMoveResult.Skipped(audioPath, "the file is not there any more"));
                continue;
            }
            string refusal = MoveSong(audioPath, target, move.TargetName, ref created);
            if (refusal == null)
            {
                moved[audioPath] = Path.Combine(target, Path.GetFileName(audioPath));
            }
            else
            {
                skipped.Add(new AlbumFolderMoveResult.Skipped(audioPath, refusal));
            }
        }

        if (moved.Count != 0 && move.IsWholeFolder)
        {
            // the folder is going into the album's folder as a whole (its songs are all there is in it): its cover goes too
            MoveLeftovers(source, target);
        }
        RemoveIfEmpty(source);
        if (created && moved.Count == 0)
        {
            RemoveIfEmpty(target); // made for songs that could not go there
        }
        return new AlbumFolderMoveResult(moved, skipped, false);
    }

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
    private static readonly string[] JunkNames = { "thumbs.db", "desktop.ini", ".ds_store" };

    /// <summary>
    /// When the last song has left a folder its cover goes after it (into the album's folder, unless it has one of that
    /// name) and the files Windows keeps for itself are deleted, so the folder is empty and goes. A folder with songs
    /// left in it is left as it is.
    /// </summary>
    private static void MoveLeftovers(string source, string target)
    {
        try
        {
            if (!Directory.Exists(source) || Directory.EnumerateFiles(source).Any(file => DataAccess.IsAudioFileName(Path.GetFileName(file))))
            {
                return;
            }
            foreach (string file in Directory.EnumerateFiles(source).ToList())
            {
                string name = Path.GetFileName(file);
                if (JunkNames.Contains(name.ToLowerInvariant()))
                {
                    File.Delete(file);
                }
                else if (ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
                {
                    string to = Path.Combine(target, name);
                    if (!File.Exists(to) && !Directory.Exists(to) && to.Length <= SongRenamer.MaxPathLength)
                    {
                        File.Move(file, to, false);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a cover that stays behind is no harm
        }
    }

    /// <summary>moves the song's files into the target folder; null when it went, otherwise why it stayed</summary>
    private static string MoveSong(string audioPath, string target, string targetName, ref bool created)
    {
        string directory = Path.GetDirectoryName(audioPath);
        string stem = Path.GetFileNameWithoutExtension(audioPath);

        // the files that go with the song: the same name, audio (also in another format) or lyrics
        var files = new List<string>();
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);
            if (string.Equals(Path.GetFileNameWithoutExtension(name), stem, StringComparison.OrdinalIgnoreCase)
                    && (DataAccess.IsAudioFileName(name) || LyricExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())))
            {
                files.Add(file);
            }
        }
        if (!files.Any(file => string.Equals(file, audioPath, StringComparison.OrdinalIgnoreCase)))
        {
            files.Add(audioPath);
        }

        foreach (string file in files)
        {
            string to = Path.Combine(target, Path.GetFileName(file));
            if (to.Length > SongRenamer.MaxPathLength)
            {
                return "the path would be " + to.Length + " characters long (Windows allows " + SongRenamer.MaxPathLength + ")";
            }
            if (File.Exists(to) || Directory.Exists(to))
            {
                return "\"" + Path.GetFileName(file) + "\" is in \"" + targetName + "\" already";
            }
        }

        if (!Directory.Exists(target))
        {
            Directory.CreateDirectory(target);
            created = true;
        }
        var done = new List<(string From, string To)>();
        try
        {
            foreach (string file in files)
            {
                string to = Path.Combine(target, Path.GetFileName(file));
                File.Move(file, to, false);
                done.Add((file, to));
            }
        }
        catch (Exception e)
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                try
                {
                    File.Move(done[i].To, done[i].From, false); // put it back where it was
                }
                catch (Exception)
                {
                    // nothing more can be done for this one; the song is reported as left either way
                }
            }
            return string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
        }
        return null;
    }

    /// <summary>the longest path any file of the folder would have after the folder is renamed</summary>
    private static int LongestNewPath(string source, string target)
    {
        int longest = target.Length;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            longest = Math.Max(longest, target.Length + file.Length - source.Length);
        }
        return longest;
    }

    /// <summary>renames a folder; a change of case only (which the file system would call the same name) goes through a temporary name</summary>
    private static void RenameDirectory(string source, string target, bool sameFolder)
    {
        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return;
        }
        if (sameFolder)
        {
            string temporary = source + "." + Guid.NewGuid().ToString("N") + ".renaming";
            Directory.Move(source, temporary);
            Directory.Move(temporary, target);
            return;
        }
        Directory.Move(source, target);
    }

    private static void RemoveIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a folder that stays is no harm
        }
    }

    private static AlbumFolderMoveResult AllSkipped(AlbumFolderMove move, string reason)
    {
        return new AlbumFolderMoveResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                move.AudioPaths.Select(path => new AlbumFolderMoveResult.Skipped(path, reason)).ToList(), false);
    }
}
