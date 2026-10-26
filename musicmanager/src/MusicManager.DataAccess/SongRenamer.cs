using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.DataAccess;

/// <summary>
/// Renames a song's files to "Artist - Title" after the tags the audio file has (see <see cref="StandardFileName"/>): the
/// audio file and the .lrc, .ttml and .hmrc files that share its name, which is how the app tells which lyrics belong to
/// which song. A song is renamed whole or not at all, and nothing is ever overwritten.
/// </summary>
public static class SongRenamer
{
    /// <summary>the longest path Windows accepts without the long-path opt-in, counting the folder</summary>
    public const int MaxPathLength = 259;

    private static readonly string[] LyricExtensions = { ".lrc", ".ttml", ".hmrc" };

    public static SongRenameResult Rename(string audioPath, string separator)
    {
        try
        {
            return RenameFiles(audioPath, separator);
        }
        catch (Exception e)
        {
            string reason = string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
            return new SongRenameResult(SongRenameResult.Kind.Failed, audioPath, audioPath, reason);
        }
    }

    private static SongRenameResult RenameFiles(string audioPath, string separator)
    {
        if (!File.Exists(audioPath))
        {
            return new SongRenameResult(SongRenameResult.Kind.Failed, audioPath, audioPath, "the file is not there any more");
        }
        (List<string> artists, string title) = ArtistTags.ReadNameParts(audioPath);
        string newBase = StandardFileName.Build(artists, title, separator);
        if (newBase == null)
        {
            return new SongRenameResult(SongRenameResult.Kind.NoTags, audioPath, audioPath,
                    "it has no artist or no title tag to name it after");
        }
        string directory = Path.GetDirectoryName(audioPath);
        string oldBase = Path.GetFileNameWithoutExtension(audioPath);
        if (string.Equals(newBase, oldBase, StringComparison.Ordinal))
        {
            return new SongRenameResult(SongRenameResult.Kind.AlreadyNamed, audioPath, audioPath, "");
        }

        // the files that go with the song: the same name, audio (also in another format) or lyrics
        List<(string From, string To)> moves = new List<(string From, string To)>();
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);
            string extension = Path.GetExtension(name);
            if (string.Equals(Path.GetFileNameWithoutExtension(name), oldBase, StringComparison.OrdinalIgnoreCase)
                    && (DataAccess.IsAudioFileName(name) || LyricExtensions.Contains(extension.ToLowerInvariant())))
            {
                moves.Add((file, Path.Combine(directory, newBase + extension)));
            }
        }
        if (!moves.Any(move => string.Equals(move.From, audioPath, StringComparison.OrdinalIgnoreCase)))
        {
            moves.Add((audioPath, Path.Combine(directory, newBase + Path.GetExtension(audioPath))));
        }

        foreach ((string from, string to) in moves)
        {
            if (to.Length > MaxPathLength)
            {
                return new SongRenameResult(SongRenameResult.Kind.TooLong, audioPath, audioPath,
                        "the new name would make the path " + to.Length + " characters long (Windows allows " + MaxPathLength + ")");
            }
            bool sameFile = string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase);
            if (!sameFile && (File.Exists(to) || Directory.Exists(to)))
            {
                return new SongRenameResult(SongRenameResult.Kind.Exists, audioPath, audioPath,
                        "\"" + Path.GetFileName(to) + "\" is there already");
            }
        }

        List<(string From, string To)> done = new List<(string From, string To)>();
        try
        {
            foreach ((string from, string to) in moves)
            {
                Move(from, to);
                done.Add((from, to));
            }
        }
        catch (Exception e)
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                try
                {
                    Move(done[i].To, done[i].From); // put it back where it was
                }
                catch (Exception)
                {
                    // nothing more can be done for this one; the song is reported as failed either way
                }
            }
            string reason = string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
            return new SongRenameResult(SongRenameResult.Kind.Failed, audioPath, audioPath, reason);
        }
        return new SongRenameResult(SongRenameResult.Kind.Renamed, audioPath,
                Path.Combine(directory, newBase + Path.GetExtension(audioPath)), "");
    }

    /// <summary>
    /// moves a file without overwriting; a change of case only (which the file system would call the same name) goes through
    /// a temporary name
    /// </summary>
    private static void Move(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return;
        }
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
        {
            string temporary = from + "." + Guid.NewGuid().ToString("N") + ".renaming";
            File.Move(from, temporary);
            File.Move(temporary, to);
            return;
        }
        File.Move(from, to, false);
    }
}
