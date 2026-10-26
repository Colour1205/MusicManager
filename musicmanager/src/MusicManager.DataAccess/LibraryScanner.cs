using System.Runtime.ExceptionServices;
using MusicManager.Common;
using MusicManager.Entity;

namespace MusicManager.DataAccess;

/// <summary>
/// lists the songs of a folder for the library page: title, artists and album from each file's tag, and its cover
/// art shrunk to a small PNG (see <see cref="Thumbnails"/>). <see cref="DataAccess.ListMusics"/> delegates here.
/// <para>
/// Reading thousands of tags is slow when done one file after another, so a few threads read several at once; the
/// result is still in folder and file name order and the progress callback is never called by two threads at once.
/// </para>
/// </summary>
public static class LibraryScanner
{
    /// <summary>
    /// how many songs are read at once: one for each core, between 4 and 16 (the pictures are the heavy part, and they are
    /// shrunk by an imaging component that any number of threads can use at once; more than 16 gains nothing measurable)
    /// </summary>
    public static int Threads { get; set; } = Math.Clamp(Environment.ProcessorCount, 4, 16);

    /// <summary>
    /// every audio file under path (subfolders too), folder by folder and by file name within a folder, ignoring case.
    /// A file whose tag cannot be read is still listed, with its file name as the title.
    /// </summary>
    /// <param name="onlyAudioPaths">read only these files (the paths an earlier call returned); null = all of them</param>
    /// <param name="progress">told (done, total) after each file, with done counting up from 1 to total; may be null</param>
    public static List<Music> List(string path, HashSet<string> onlyAudioPaths, Action<int, int> progress)
    {
        List<string> found = new List<string>();
        // java.io.File spells a path with the platform separator and without a trailing one
        Collect(Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)),
                onlyAudioPaths, found);
        List<string> files = Sorted(found);

        int total = files.Count;
        List<Music> songs = new List<Music>(total);
        if (total == 0)
        {
            return songs;
        }

        Thumbnails.Cache thumbnails = new Thumbnails.Cache();
        object progressLock = new object();
        int done = 0;
        int next = -1;
        bool failed = false;
        Music[] read = new Music[total];
        Exception[] failures = new Exception[total];

        // daemon threads (background threads in .NET), so a scan that is abandoned never keeps the program alive
        List<Thread> pool = new List<Thread>();
        int workers = Math.Min(Threads, total);
        for (int w = 1; w <= workers; w++)
        {
            Thread thread = new Thread(() =>
            {
                int i;
                while (!Volatile.Read(ref failed) && (i = Interlocked.Increment(ref next)) < total)
                {
                    try
                    {
                        Music music = Read(files[i], thumbnails);
                        // counted and reported under one lock so the numbers arrive in order, one caller at a time:
                        // the presenter that receives them keeps them in a shared state object
                        lock (progressLock)
                        {
                            ReportProgress(progress, ++done, total);
                        }
                        read[i] = music;
                    }
                    catch (Exception e)
                    {
                        failures[i] = e;
                        Volatile.Write(ref failed, true);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "library-scan-" + w;
            pool.Add(thread);
        }

        foreach (Thread thread in pool)
        {
            thread.Start();
        }
        foreach (Thread thread in pool)
        {
            thread.Join();
        }

        for (int i = 0; i < total; i++)
        {
            if (failures[i] != null)
            {
                ExceptionDispatchInfo.Capture(failures[i]).Throw();
            }
            songs.Add(read[i]);
        }
        return songs;
    }

    private static void ReportProgress(Action<int, int> progress, int done, int total)
    {
        if (progress == null)
        {
            return;
        }
        try
        {
            progress(done, total);
        }
        catch (Exception)
        {
            // progress is only a courtesy to the window: a listener that fails must not abandon the whole folder
        }
    }

    // ---- finding and ordering the files ----

    private static void Collect(string dir, HashSet<string> onlyAudioPaths, List<string> output)
    {
        string[] children;
        try
        {
            children = Directory.GetFileSystemEntries(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return; // File.listFiles() gives null when the folder cannot be listed
        }
        foreach (string child in children)
        {
            if (Directory.Exists(child))
            {
                Collect(child, onlyAudioPaths, output);
            }
            else if (DataAccess.IsAudioFileName(Path.GetFileName(child))
                    && (onlyAudioPaths == null || onlyAudioPaths.Contains(child)))
            {
                output.Add(child);
            }
        }
    }

    /// <summary>a file with the sort keys worked out once: the names of its folders, then its own name, in lower case</summary>
    private sealed class SortKey
    {
        internal readonly string File;
        internal readonly string[] Folders;
        internal readonly string Name;

        internal SortKey(string file)
        {
            File = file;
            Name = Path.GetFileName(file).ToLowerInvariant();
            List<string> names = new List<string>();
            string parent = Path.GetDirectoryName(file);
            while (!string.IsNullOrEmpty(parent))
            {
                string leaf = Path.GetFileName(parent);
                names.Insert(0, (leaf.Length == 0 ? parent : leaf).ToLowerInvariant());
                parent = Path.GetDirectoryName(parent);
            }
            Folders = names.ToArray();
        }
    }

    /// <summary>
    /// folder by folder, comparing folder names one level at a time so that a folder's own songs come before those of
    /// its subfolders and "Album" and "Album 2" are not split by the subfolders of "Album"; then by file name
    /// </summary>
    private static int FolderThenName(SortKey a, SortKey b)
    {
        int common = Math.Min(a.Folders.Length, b.Folders.Length);
        for (int i = 0; i < common; i++)
        {
            int order = string.CompareOrdinal(a.Folders[i], b.Folders[i]);
            if (order != 0)
            {
                return order;
            }
        }
        if (a.Folders.Length != b.Folders.Length)
        {
            return a.Folders.Length.CompareTo(b.Folders.Length);
        }
        int byName = string.CompareOrdinal(a.Name, b.Name);
        // two names that differ only in case (possible on case-sensitive disks) still get a fixed order
        return byName != 0 ? byName : string.CompareOrdinal(a.File, b.File);
    }

    private static List<string> Sorted(List<string> files)
    {
        List<SortKey> keys = new List<SortKey>(files.Count);
        foreach (string file in files)
        {
            keys.Add(new SortKey(file));
        }
        keys.Sort(FolderThenName);
        List<string> ordered = new List<string>(keys.Count);
        foreach (SortKey key in keys)
        {
            ordered.Add(key.File);
        }
        return ordered;
    }

    // ---- reading one file ----

    /// <summary>one song from its tag; whatever cannot be read is left out, the file name stands in for the title</summary>
    public static Music Read(string file, Thumbnails.Cache thumbnails)
    {
        Music.Builder builder = new Music.Builder()
                .SetTitle(StripExtension(Path.GetFileName(file)))
                .SetPath(file);

        AudioTags tag = null;
        try
        {
            tag = AudioTags.Open(file);
        }
        catch (Exception)
        {
            // no readable tag (unsupported or damaged file): it is still a song of the library
        }
        if (tag == null)
        {
            return builder.Build();
        }

        using (tag)
        {
            if (!tag.HasTag)
            {
                return builder.Build();
            }

            try
            {
                string title = tag.GetFirst(AudioTags.Field.Title);
                if (title != null && JavaString.Trim(title).Length != 0)
                {
                    builder.SetTitle(JavaString.Trim(title));
                }
                List<string> artists = new List<string>();
                foreach (string artist in tag.GetAll(AudioTags.Field.Artist))
                {
                    if (artist != null && JavaString.Trim(artist).Length != 0)
                    {
                        artists.Add(JavaString.Trim(artist));
                    }
                }
                if (artists.Count != 0)
                {
                    builder.SetArtists(artists);
                }
                string album = tag.GetFirst(AudioTags.Field.Album);
                if (album != null && JavaString.Trim(album).Length != 0)
                {
                    builder.SetAlbum(JavaString.Trim(album));
                }
            }
            catch (Exception)
            {
                // a field this tag format cannot hold: keep what was read so far
            }

            try
            {
                byte[] artwork = tag.GetFirstArtwork();
                if (artwork != null)
                {
                    // the raw cover is never kept: with thousands of songs it would not fit in memory
                    builder.SetCoverArt(thumbnails.PngFrom(artwork));
                }
            }
            catch (Exception)
            {
                // a damaged picture frame: the song simply has no cover
            }
        }
        return builder.Build();
    }

    private static string StripExtension(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName.Substring(0, dot) : fileName;
    }
}
