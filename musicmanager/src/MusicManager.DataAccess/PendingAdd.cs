using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.DataAccess;

/// <summary>
/// The songs of an Add that the user has not confirmed yet. They are copied into a folder of their own that is laid out
/// exactly as the library will be (<c>staging\New songs\Song.flac</c> for <c>library\New songs\Song.flac</c>), so the
/// library is not touched. While this is open the reading side of the data access shows the staged files at the path they
/// will have in the library: a preview then looks like the library after the add, and when it is confirmed
/// <see cref="Commit"/> only has to move the files in (all or none); <see cref="Discard"/> throws them away.
/// </summary>
public sealed class PendingAdd
{
    /// <summary>what <see cref="Commit"/> did: how many files and songs went into the library, or why none did</summary>
    public sealed record CommitResult(int Files, int Songs, string Problem);

    private readonly string _libraryRoot;
    private readonly string _stagingRoot;

    public PendingAdd(string libraryRoot, string stagingRoot)
    {
        _libraryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        _stagingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
    }

    /// <summary>the library folder the files will go into</summary>
    public string LibraryRoot => _libraryRoot;

    /// <summary>the folder the files wait in</summary>
    public string StagingRoot => _stagingRoot;

    /// <summary>true when this is the library folder the add is for (case and a closing separator do not matter)</summary>
    public bool IsFor(string folder)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), _libraryRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>the path a file of the library has in the staging folder; null for a path that is not in the library</summary>
    public string StagedPathOf(string libraryPath)
    {
        string relative = RelativeToLibrary(libraryPath);
        return relative == null ? null : Path.Combine(_stagingRoot, relative);
    }

    /// <summary>the path a staged file will have in the library</summary>
    public string LibraryPathOf(string stagedPath)
    {
        return Path.Combine(_libraryRoot, Path.GetRelativePath(_stagingRoot, stagedPath));
    }

    private string RelativeToLibrary(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            return full.StartsWith(_libraryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? full.Substring(_libraryRoot.Length + 1) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>the file to read for a path of the library: the staged one when the library does not have the file yet</summary>
    public string Resolve(string path)
    {
        if (path == null || File.Exists(path))
        {
            return path;
        }
        string staged = StagedPathOf(path);
        return staged != null && File.Exists(staged) ? staged : path;
    }

    /// <summary>true when something is waiting in the staging folder</summary>
    public bool HasFiles()
    {
        return Directory.Exists(_stagingRoot) && Directory.EnumerateFiles(_stagingRoot, "*", SearchOption.AllDirectories).Any();
    }

    // ------------------------------------------------------------------ what the readers see

    /// <summary>
    /// The songs of the library with their lyric files, then the staged songs at their library paths; a library song that
    /// has no lyric file of some kind gets the staged one that waits for it (see LibraryImporter: a song the library has
    /// without lyrics is given those of the copy that was left out).
    /// </summary>
    public List<SongLyricFiles> MergeLyricFiles(List<SongLyricFiles> librarySongs)
    {
        if (!Directory.Exists(_stagingRoot))
        {
            return new List<SongLyricFiles>(librarySongs);
        }
        var waiting = new HashSet<string>(Directory.EnumerateFiles(_stagingRoot, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(_stagingRoot, file)), StringComparer.OrdinalIgnoreCase);
        var merged = new List<SongLyricFiles>(librarySongs.Count);
        foreach (SongLyricFiles song in librarySongs)
        {
            merged.Add(new SongLyricFiles(song.AudioPath, LyricOr(song.LrcPath, song.AudioPath, ".lrc", waiting),
                    LyricOr(song.TtmlPath, song.AudioPath, ".ttml", waiting), LyricOr(song.HmrcPath, song.AudioPath, ".hmrc", waiting)));
        }
        foreach (SongLyricFiles staged in LyricFiles.FindSongs(_stagingRoot))
        {
            merged.Add(new SongLyricFiles(LibraryPathOf(staged.AudioPath), Moved(staged.LrcPath), Moved(staged.TtmlPath),
                    Moved(staged.HmrcPath)));
        }
        return merged;
    }

    /// <summary>the song's lyric file of this kind, or, when it has none, the one that waits for it in the staging folder (at its library path)</summary>
    private string LyricOr(string existing, string audioPath, string extension, HashSet<string> waiting)
    {
        if (existing != null)
        {
            return existing;
        }
        string candidate = Path.ChangeExtension(audioPath, extension);
        string relative = RelativeToLibrary(candidate);
        return relative != null && waiting.Contains(relative) ? candidate : null;
    }

    private string Moved(string stagedPath)
    {
        return stagedPath == null ? null : LibraryPathOf(stagedPath);
    }

    public List<SongArtistTags> MergeArtistTags(List<SongArtistTags> librarySongs)
    {
        var merged = new List<SongArtistTags>(librarySongs);
        if (Directory.Exists(_stagingRoot))
        {
            foreach (SongArtistTags staged in ArtistTags.Read(_stagingRoot))
            {
                merged.Add(new SongArtistTags(LibraryPathOf(staged.Path), staged.Artist, staged.AlbumArtist, staged.Artists, staged.Title));
            }
        }
        return merged;
    }

    public List<SongAlbumTags> MergeAlbumTags(List<SongAlbumTags> librarySongs)
    {
        var merged = new List<SongAlbumTags>(librarySongs);
        if (Directory.Exists(_stagingRoot))
        {
            foreach (SongAlbumTags staged in AlbumTags.Read(_stagingRoot))
            {
                merged.Add(new SongAlbumTags(LibraryPathOf(staged.Path), staged.Album, staged.Artist, staged.AlbumArtist, staged.Title));
            }
        }
        return merged;
    }

    // ------------------------------------------------------------------ the decision

    /// <summary>
    /// Moves the staged files into the library, each to the path it has been shown at. All or none: nothing is moved when
    /// a file of the library is in the way (or a path would be too long), and a move that fails half-way is undone, so the
    /// library is either as it was or has all the songs. The staging folder is removed afterwards.
    /// </summary>
    public CommitResult Commit()
    {
        if (!Directory.Exists(_stagingRoot))
        {
            return new CommitResult(0, 0, null);
        }
        List<string> staged = Directory.EnumerateFiles(_stagingRoot, "*", SearchOption.AllDirectories)
                .OrderBy(file => file.ToLowerInvariant(), StringComparer.Ordinal).ToList();

        foreach (string file in staged)
        {
            string target = LibraryPathOf(file);
            if (target.Length > SongRenamer.MaxPathLength)
            {
                return Failed("the path in the library would be " + target.Length + " characters long (Windows allows "
                        + SongRenamer.MaxPathLength + "): " + Path.GetRelativePath(_libraryRoot, target));
            }
            if (File.Exists(target) || Directory.Exists(target))
            {
                return Failed("\"" + Path.GetRelativePath(_libraryRoot, target) + "\" is in the library already");
            }
        }

        var done = new List<(string From, string To)>();
        var created = new List<string>();
        try
        {
            foreach (string file in staged)
            {
                string target = LibraryPathOf(file);
                CreateDirectories(Path.GetDirectoryName(target), created);
                File.Move(file, target, false);
                done.Add((file, target));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                try
                {
                    File.Move(done[i].To, done[i].From, false); // back where it was
                }
                catch (Exception)
                {
                    // nothing more can be done for this one
                }
            }
            for (int i = created.Count - 1; i >= 0; i--)
            {
                RemoveIfEmpty(created[i]);
            }
            return Failed(string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message);
        }

        Discard(); // what is left of the staging folder: the empty folders
        return new CommitResult(done.Count, done.Count(file => DataAccess.IsAudioFileName(Path.GetFileName(file.To))), null);
    }

    private static CommitResult Failed(string problem)
    {
        return new CommitResult(0, 0, "The songs could not be added to the library, so nothing was added: " + problem);
    }

    /// <summary>throws the staged files away (the originals the user picked were never touched)</summary>
    public void Discard()
    {
        try
        {
            if (Directory.Exists(_stagingRoot))
            {
                Directory.Delete(_stagingRoot, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a staging folder that stays is cleaned up the next time the app starts
        }
    }

    private static void CreateDirectories(string directory, List<string> created)
    {
        var missing = new Stack<string>();
        for (string current = directory; !string.IsNullOrEmpty(current) && !Directory.Exists(current); current = Path.GetDirectoryName(current))
        {
            missing.Push(current);
        }
        foreach (string folder in missing)
        {
            Directory.CreateDirectory(folder);
            created.Add(folder);
        }
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

    /// <summary>removes every staging folder under <paramref name="folder"/>: the ones of adds that were never decided (the app was closed)</summary>
    public static void DiscardAllIn(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (string staging in Directory.EnumerateDirectories(folder))
                {
                    Directory.Delete(staging, true);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // cleaned up the next time
        }
    }
}
