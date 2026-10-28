using MusicManager.Entity;
using MusicManager.UseCase.AddSongs;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.DataAccess;

/// <summary>
/// Stages the songs the user picked for the library. Each audio file is copied into a folder named after the folder it came
/// from (so an album picked as a folder arrives as that folder, and the folders inside it as folders of their own),
/// together with the lyric files that share its name and the album's cover. The copies are made in the staging folder of a
/// <see cref="PendingAdd"/>, laid out as the library will be, and the library itself is not touched: they go in when the
/// user has seen what the run that follows would do and confirms. Nothing is overwritten and the originals stay where they
/// are.
/// </summary>
public static class LibraryImporter
{
    /// <summary>the folder of songs that were picked from a folder with no name worth keeping (the desktop, the downloads)</summary>
    public const string LooseSongsFolder = LibraryFolderNames.LooseSongs;

    private static readonly string[] LyricExtensions = { ".lrc", ".ttml", ".hmrc" };
    private static readonly string[] CoverNames = { "cover", "folder", "front", "albumart", "album" };
    private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png" };

    /// <summary>
    /// The paths in the result are the paths the songs will have in the library; a song that is in the library already (or
    /// was picked from it) has its path there, and the songs that are waiting in the staging folder are the ones listed as
    /// new.
    /// </summary>
    /// <param name="pending">the library the songs are for, and the folder to stage them in (it is made when the first file is copied)</param>
    /// <param name="preferredFormat">
    /// the format to add when a folder has the same song in several (see <see cref="PreferredFormat"/>): "Song.flac" and
    /// "Song.mp3" side by side are one song, and only one of them is added
    /// </param>
    public static AddSongsOutputData Add(PendingAdd pending, IReadOnlyList<string> sources, string preferredFormat,
            Action<int, int> progress)
    {
        string root = pending.LibraryRoot;
        var problems = new List<AddSongsOutputData.Problem>();
        List<string> found = Find(sources, problems);
        int otherFormats = DropOtherFormats(found, preferredFormat, problems);
        int foundCount = found.Count; // (the ones the library has already are songs that were found too)
        var lyricsAddedTo = new List<string>();
        List<AddSongsOutputData.Problem> alreadyInLibrary = DropSongsTheLibraryHas(found, pending, progress, lyricsAddedTo);

        var libraryPaths = new List<string>(lyricsAddedTo); // (a song that got lyrics is standardized with the new ones)
        var newPaths = new List<string>();
        var coversHandled = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // source folders whose cover was looked at
        int copied = 0;
        int alreadyThere = 0;
        int done = 0;
        foreach (string file in found)
        {
            try
            {
                if (IsInside(file, root))
                {
                    libraryPaths.Add(file); // in the library already: nothing to copy, it is standardized with the others
                    alreadyThere++;
                }
                else
                {
                    string destinationFolder = Path.Combine(root, FolderNameFor(file));
                    string destination = Path.Combine(destinationFolder, Path.GetFileName(file));
                    string there = pending.Resolve(destination); // the library's file, or the one staged for it
                    if (destination.Length > SongRenamer.MaxPathLength)
                    {
                        problems.Add(new AddSongsOutputData.Problem(file, "the path in the library would be " + destination.Length
                                + " characters long (Windows allows " + SongRenamer.MaxPathLength + ")"));
                    }
                    else if (File.Exists(there))
                    {
                        if (new FileInfo(there).Length == new FileInfo(file).Length)
                        {
                            if (!libraryPaths.Contains(destination))
                            {
                                libraryPaths.Add(destination); // the same song, added before: it is picked up again
                            }
                            alreadyThere++;
                        }
                        else
                        {
                            problems.Add(new AddSongsOutputData.Problem(file, "a different file called \""
                                    + Path.GetFileName(file) + "\" is in \"" + Path.GetFileName(destinationFolder) + "\" already"));
                        }
                    }
                    else
                    {
                        string staged = pending.StagedPathOf(destination);
                        Directory.CreateDirectory(Path.GetDirectoryName(staged));
                        CopyWhole(file, staged);
                        CopyWithTheSong(file, destinationFolder, pending, coversHandled);
                        libraryPaths.Add(destination);
                        newPaths.Add(destination);
                        copied++;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                problems.Add(new AddSongsOutputData.Problem(file, string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message));
            }
            progress?.Invoke(++done, found.Count);
        }
        return new AddSongsOutputData(libraryPaths, copied, alreadyThere, foundCount, otherFormats, alreadyInLibrary, lyricsAddedTo,
                newPaths, problems);
    }

    /// <summary>
    /// Takes out of the list the songs the library has already: the same title, first artist and album in the tags, in a format
    /// that is as good or better. They are not copied again (a song added a second time would only end up next to the first
    /// under another file name). A better format than the library's is still added. Songs that are in the library folder
    /// already are not looked at: they are the library's own.
    /// </summary>
    internal static List<AddSongsOutputData.Problem> DropSongsTheLibraryHas(List<string> found, PendingAdd pending,
            Action<int, int> progress, List<string> lyricsAddedTo)
    {
        string root = pending.LibraryRoot;
        var duplicates = new List<AddSongsOutputData.Problem>();
        int outside = found.Count(file => !IsInside(file, root));
        if (outside == 0)
        {
            return duplicates;
        }

        // what the library has, by identity; the best copy of each
        var inLibrary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SongAlbumTags song in AlbumTags.Read(root, progress == null ? null : (done, total) => progress(done, total + outside)))
        {
            string key = IdentityOf(song.Title, song.Artist.FirstOrDefault(), song.Album);
            if (key != null && (!inLibrary.TryGetValue(key, out string known)
                    || PreferredFormat.QualityRank(Path.GetExtension(song.Path)) < PreferredFormat.QualityRank(Path.GetExtension(known))))
            {
                inLibrary[key] = song.Path;
            }
        }

        foreach (string file in found.ToList())
        {
            if (IsInside(file, root))
            {
                continue;
            }
            string key = SongIdentity(file);
            if (key != null && inLibrary.TryGetValue(key, out string existing)
                    && PreferredFormat.QualityRank(Path.GetExtension(existing)) <= PreferredFormat.QualityRank(Path.GetExtension(file)))
            {
                duplicates.Add(new AddSongsOutputData.Problem(file, "the library has it already as \"" + Path.GetRelativePath(root, existing) + "\""));
                found.Remove(file);
                if (GiveLyrics(file, existing, pending))
                {
                    lyricsAddedTo.Add(existing);
                }
            }
        }
        return duplicates;
    }

    /// <summary>
    /// A song the library has without lyrics is to get the lyric files of the copy that was left out (named after the
    /// library's file); a song that has lyrics already keeps them. The lyric files are staged with the new songs: they go
    /// into the library when the add is confirmed. True if any lyric file was staged.
    /// </summary>
    private static bool GiveLyrics(string picked, string existing, PendingAdd pending)
    {
        try
        {
            if (LyricExtensions.Any(extension => File.Exists(pending.Resolve(Path.ChangeExtension(existing, extension)))))
            {
                return false; // the library has lyrics for it (or they are staged for it already)
            }
            bool given = false;
            foreach (string extension in LyricExtensions)
            {
                string from = Path.ChangeExtension(picked, extension);
                string to = pending.StagedPathOf(Path.ChangeExtension(existing, extension));
                if (File.Exists(from) && to != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(from, to, false);
                    given = true;
                }
            }
            return given;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // the song is in the library either way
        }
    }

    /// <summary>the title, first artist and album of a file, as one key for "the same song on the same release"; null without a title</summary>
    private static string SongIdentity(string file)
    {
        try
        {
            using AudioTags tag = AudioTags.Open(file);
            if (!tag.HasTag)
            {
                return null;
            }
            return IdentityOf(ArtistTags.Values(tag, AudioTags.Field.Title).FirstOrDefault(),
                    ArtistTags.Values(tag, AudioTags.Field.Artist).FirstOrDefault(),
                    ArtistTags.Values(tag, AudioTags.Field.Album).FirstOrDefault());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex ArtistSeparators = new System.Text.RegularExpressions.Regex(
            @"\s*(?:[/&;,、]|\s(?:feat\.?|ft\.?|with|x)\s)\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>"title|artist|album" loosely (case, accents and punctuation do not matter); the artist is the first one named; null without a title</summary>
    internal static string IdentityOf(string title, string artist, string album)
    {
        string loose = title == null ? "" : ArtistStandardizer.LooseKey(title);
        if (loose.Length == 0)
        {
            return null;
        }
        string first = artist == null ? "" : ArtistSeparators.Split(artist.Trim())[0];
        return loose + "|" + ArtistStandardizer.LooseKey(first) + "|" + ArtistStandardizer.LooseKey(album ?? "");
    }

    /// <summary>
    /// Takes out of the list the copies of a song that are not the one to add. Files of one folder are the same song when
    /// they have the same name apart from the extension ("Song.flac", "Song.mp3"), or the same title and artist in their
    /// tags (a folder with "Song.flac", "Song Master.flac" and "Song Master.wav"). Of the copies the preferred format stays
    /// (the largest of it), and when there is none in that format the largest file does. Returns how many files were taken out.
    /// </summary>
    internal static int DropOtherFormats(List<string> found, string preferredFormat, List<AddSongsOutputData.Problem> problems)
    {
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, string> folder in found.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
        {
            List<string> files = folder.ToList();
            int[] leader = Enumerable.Range(0, files.Count).ToArray();
            int Leader(int i)
            {
                while (leader[i] != i)
                {
                    leader[i] = leader[leader[i]];
                    i = leader[i];
                }
                return i;
            }
            var firstWithKey = new Dictionary<string, int>(StringComparer.Ordinal);
            void Join(string key, int i)
            {
                if (firstWithKey.TryGetValue(key, out int other))
                {
                    leader[Leader(i)] = Leader(other);
                }
                else
                {
                    firstWithKey[key] = i;
                }
            }
            for (int i = 0; i < files.Count; i++)
            {
                Join("name:" + Path.GetFileNameWithoutExtension(files[i]).ToLowerInvariant(), i);
                string song = SongKey(files[i]);
                if (song != null)
                {
                    Join("song:" + song, i);
                }
            }

            foreach (IGrouping<int, int> copies in Enumerable.Range(0, files.Count).GroupBy(Leader).Where(group => group.Count() > 1))
            {
                List<string> versions = copies.Select(i => files[i]).ToList();
                var described = new List<(string Extension, long Size)>();
                foreach (string version in versions)
                {
                    long size = 0;
                    try
                    {
                        size = new FileInfo(version).Length;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // a file that cannot be measured counts as the smallest, and shows its problem when it is copied
                    }
                    described.Add((Path.GetExtension(version), size));
                }
                int chosen = PreferredFormat.Choose(described, preferredFormat);
                for (int i = 0; i < versions.Count; i++)
                {
                    if (i != chosen)
                    {
                        removed.Add(versions[i]);
                    }
                }
            }
        }
        found.RemoveAll(removed.Contains);
        return removed.Count;
    }

    /// <summary>the title and the first artist of a file's tags, as one key for "the same song"; null when it has no title (or no readable tag)</summary>
    private static string SongKey(string file)
    {
        try
        {
            using AudioTags tag = AudioTags.Open(file);
            if (!tag.HasTag)
            {
                return null;
            }
            string title = ArtistTags.Values(tag, AudioTags.Field.Title).FirstOrDefault();
            if (title == null || ArtistStandardizer.LooseKey(title).Length == 0)
            {
                return null;
            }
            string artist = ArtistTags.Values(tag, AudioTags.Field.Artist).FirstOrDefault();
            return ArtistStandardizer.LooseKey(title) + "|" + ArtistStandardizer.LooseKey(artist);
        }
        catch (Exception)
        {
            return null; // unreadable: only the name can tell
        }
    }

    /// <summary>every audio file of the picked files and folders (folders are searched all the way down), each once, in path order per source</summary>
    private static List<string> Find(IReadOnlyList<string> sources, List<AddSongsOutputData.Problem> problems)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string source in sources)
        {
            try
            {
                if (File.Exists(source))
                {
                    if (!DataAccess.IsAudioFileName(Path.GetFileName(source)))
                    {
                        problems.Add(new AddSongsOutputData.Problem(source, "it is not an audio file"));
                    }
                    else if (seen.Add(Path.GetFullPath(source)))
                    {
                        found.Add(Path.GetFullPath(source));
                    }
                }
                else if (Directory.Exists(source))
                {
                    var inFolder = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                            .Where(file => DataAccess.IsAudioFileName(Path.GetFileName(file)))
                            .Select(Path.GetFullPath)
                            .OrderBy(file => file.ToLowerInvariant(), StringComparer.Ordinal);
                    foreach (string file in inFolder)
                    {
                        if (seen.Add(file))
                        {
                            found.Add(file);
                        }
                    }
                }
                else
                {
                    problems.Add(new AddSongsOutputData.Problem(source, "it is not there"));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                problems.Add(new AddSongsOutputData.Problem(source, string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message));
            }
        }
        return found;
    }

    /// <summary>the name of the library folder a song picked from this file's folder goes into</summary>
    internal static string FolderNameFor(string file)
    {
        string leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(file) ?? ""));
        string name = AlbumFolderName.Clean(leaf);
        return name.Length == 0 || LibraryFolderNames.IsGeneric(name) ? LooseSongsFolder : name;
    }

    private static bool IsInside(string file, string root)
    {
        return Path.GetFullPath(file).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>copies the file without overwriting; a copy that stops halfway is removed</summary>
    private static void CopyWhole(string from, string to)
    {
        try
        {
            File.Copy(from, to, false);
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(to) && new FileInfo(to).Length != new FileInfo(from).Length)
                {
                    File.Delete(to);
                }
            }
            catch (Exception)
            {
                // nothing more can be done
            }
            throw;
        }
    }

    /// <summary>
    /// the files that go with a song, staged next to it: its lyric files, and once for each folder the cover that lies next
    /// to it (not when the library, or an earlier song of this add, has a file of that name in the folder)
    /// </summary>
    private static void CopyWithTheSong(string audioFile, string destinationFolder, PendingAdd pending, HashSet<string> coversHandled)
    {
        foreach (string extension in LyricExtensions)
        {
            string lyrics = Path.ChangeExtension(audioFile, extension);
            string target = Path.Combine(destinationFolder, Path.GetFileName(lyrics));
            if (File.Exists(lyrics) && !File.Exists(pending.Resolve(target)))
            {
                CopyQuietly(lyrics, pending.StagedPathOf(target));
            }
        }

        string folder = Path.GetDirectoryName(audioFile);
        if (folder != null && coversHandled.Add(folder + "|" + destinationFolder))
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string target = Path.Combine(destinationFolder, Path.GetFileName(file));
                if (CoverNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                        && CoverExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                        && !File.Exists(pending.Resolve(target)))
                {
                    CopyQuietly(file, pending.StagedPathOf(target));
                }
            }
        }
    }

    /// <summary>a lyric file or a cover is a nicety: when it cannot be copied the song is added without it</summary>
    private static void CopyQuietly(string from, string to)
    {
        try
        {
            File.Copy(from, to, false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the song itself was copied
        }
    }
}
