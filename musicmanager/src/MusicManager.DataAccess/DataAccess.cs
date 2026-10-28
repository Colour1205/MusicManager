using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.AppSettings;
using MusicManager.UseCase.ConvertLRC;
using MusicManager.UseCase.AddSongs;
using MusicManager.UseCase.OpenLibrary;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.DataAccess;

/// <summary>
/// single data access gateway for the application, implementing the data
/// access interface(s) declared by each use case:
/// - IConvertLRCDataAccess: scans a directory for music files
///   (mirroring FileManager#listFiles) and parses each one's lyric file into
///   a Lyric entity, detecting its sync type the way
///   LyricsManager#isWordSynced and LyricsManager#toTTML infer it from the
///   LRC timestamp brackets.
/// - IAppSettingsDataAccess: reads/writes the app's persisted
///   preferences (last path, default target formats, the Options window's
///   choices) as a small JSON file (see <see cref="SettingsStore"/>).
/// - IStandardizeLyricsDataAccess: finds songs and their .lrc/.ttml/.hmrc
///   sidecars, parses all three formats, writes/deletes files (see <see cref="LyricFiles"/>).
/// - IStandardizeArtistsDataAccess: reads/writes artist tags (see
///   <see cref="ArtistTags"/>) and asks the Anthropic API to group artist names (see
///   <see cref="ClaudeArtistParser"/>), remembering the answers in <see cref="ArtistParseCache"/>.
/// </summary>
public class DataAccess : IConvertLRCDataAccess, IAppSettingsDataAccess, IOpenLibraryDataAccess,
        IStandardizeLyricsDataAccess, IStandardizeArtistsDataAccess, IStandardizeAlbumsDataAccess, IAddSongsDataAccess
{
    // String.getBytes(UTF_8) and PrintWriter write an unpaired surrogate as '?', .NET's default would write U+FFFD
    private static readonly Encoding Utf8Writer = Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"),
        DecoderFallback.ReplacementFallback);

    private readonly RomajiCache _romajiCache = RomajiCache.AtDefaultLocation();
    private readonly ArtistParseCache _artistParseCache;
    private readonly AlbumParseCache _albumParseCache;
    private readonly SettingsStore _settingsStore;
    private readonly string _pendingFolder; // where the songs of an add wait for the user's decision

    /// <summary>the add that waits for the user's decision (see <see cref="PendingAdd"/>), or null</summary>
    private volatile PendingAdd _pending;
    private volatile bool _committing; // the pending add is being moved into the library

    /// <summary>stores everything under ~/.musicmanager</summary>
    public DataAccess() : this(SettingsStore.AtDefaultLocation(), ArtistParseCache.AtDefaultLocation())
    {
    }

    /// <summary>for tests: settings and the artist cache in places of their own</summary>
    public DataAccess(SettingsStore settingsStore, ArtistParseCache artistParseCache)
        : this(settingsStore, artistParseCache, AlbumParseCache.AtDefaultLocation())
    {
    }

    /// <summary>for tests: settings and both caches in places of their own</summary>
    public DataAccess(SettingsStore settingsStore, ArtistParseCache artistParseCache, AlbumParseCache albumParseCache)
        : this(settingsStore, artistParseCache, albumParseCache, DefaultPendingFolder())
    {
    }

    /// <summary>for tests: the folder the songs of an add wait in is a place of its own too</summary>
    public DataAccess(SettingsStore settingsStore, ArtistParseCache artistParseCache, AlbumParseCache albumParseCache,
            string pendingFolder)
    {
        _settingsStore = settingsStore;
        _artistParseCache = artistParseCache;
        _albumParseCache = albumParseCache;
        _pendingFolder = pendingFolder;
    }

    /// <summary>~/.musicmanager/pending: a folder for each add the user has not decided on yet</summary>
    public static string DefaultPendingFolder()
    {
        return Path.Combine(UserHome.Path, ".musicmanager", "pending");
    }

    /// <summary>
    /// Throws away what adds left in the pending folder when the app was closed (or failed) before the user decided: the
    /// library never had those songs, and the originals are where they were picked from. Call it when the app starts.
    /// </summary>
    public void DiscardUndecidedAdds()
    {
        _pending = null;
        PendingAdd.DiscardAllIn(_pendingFolder);
    }

    public static readonly string[] AudioExtensions = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac" };

    public static bool IsAudioFileName(string fileName)
    {
        string lower = fileName.ToLowerInvariant();
        foreach (string extension in AudioExtensions)
        {
            if (lower.EndsWith(extension, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    // metadata header lines, e.g. [ar:Artist], [ti:Title], [offset:700]
    private static readonly Regex HeaderPattern = new Regex(@"\A\[[A-Za-z]+:[^\]]*\]\z", RegexOptions.CultureInvariant);

    // a single LRC timestamp bracket, e.g. [01:23.456] or [01:23]
    private static readonly Regex TimePattern = new Regex(@"\[([0-9]+):([0-9]+)(?:\.([0-9]+))?\]",
        RegexOptions.CultureInvariant);

    // java.util.Scanner#nextLine: a line ends at \r\n, \n, \r, U+2028, U+2029 or U+0085
    private static readonly Regex LineSeparator = new Regex("\r\n|[\n\r\u2028\u2029\u0085]", RegexOptions.CultureInvariant);

    // ---- IOpenLibraryDataAccess (delegates to LibraryScanner) ----

    public virtual List<Music> ListMusics(string path, HashSet<string> onlyAudioPaths, Action<int, int> progress)
    {
        return LibraryScanner.List(path, onlyAudioPaths, progress);
    }

    // ---- IConvertLRCDataAccess ----

    public virtual Queue<Music> GetMusics(string path)
    {
        Queue<Music> musics = new Queue<Music>();
        ListMusicFiles(JavaPath(path), musics);
        return musics;
    }

    public virtual IDictionary<Music, Lyric> GetLRCs(Queue<Music> musics)
    {
        OrderedDictionary<Music, Lyric> lyrics = new OrderedDictionary<Music, Lyric>();

        foreach (Music music in musics)
        {
            string lrcFile = FindLyricFile(music.Path);

            // if no lyric file is detected for this music, skip it for now
            if (lrcFile == null)
            {
                continue;
            }

            Lyric lyric = ParseLRC(lrcFile);
            if (lyric != null)
            {
                lyrics[music] = lyric;
            }
        }

        return lyrics;
    }

    public virtual void SaveTTML(Lyric lyric, string content)
    {
        SaveToFile(lyric.Path, content, "ttml");
    }

    public virtual void SaveHMRC(Lyric lyric, string content)
    {
        SaveToFile(lyric.Path, content, "hmrc");
    }

    public virtual void SaveLRC(Lyric lyric, string content)
    {
        SaveToFile(lyric.Path, content, "lrc"); // WILL REPLACE FILE
    }

    // ---- IAppSettingsDataAccess ----

    public virtual AppSettings Load()
    {
        return _settingsStore.Load();
    }

    public virtual void Save(AppSettings settings)
    {
        _settingsStore.Save(settings); // best effort, like saveToFile: a failed write is not an error for the user
    }

    // ---- IStandardizeLyricsDataAccess (delegates to LyricFiles and the lyric parsers) ----

    public virtual List<SongLyricFiles> FindSongs(string path)
    {
        List<SongLyricFiles> songs = LyricFiles.FindSongs(path);
        PendingAdd pending = _pending;
        return pending != null && pending.IsFor(path) ? pending.MergeLyricFiles(songs) : songs;
    }

    public virtual Lyric ReadLyric(string lyricFilePath)
    {
        return LyricFiles.ReadLyric(Resolve(lyricFilePath));
    }

    /// <summary>the file to read for a path: while an add waits for the user's decision, a song that is not in the library yet is read from where it waits</summary>
    private string Resolve(string path)
    {
        PendingAdd pending = _pending;
        return pending == null ? path : pending.Resolve(path);
    }

    public virtual Lyric ParseLrcText(string lrcText)
    {
        return LrcParser.Parse(lrcText, null);
    }

    public virtual Music ReadMusicTags(string audioPath)
    {
        return ReadMusic(JavaPath(Resolve(audioPath)));
    }

    public virtual void WriteLyricFile(string path, string content)
    {
        LyricFiles.WriteText(path, content);
    }

    public virtual bool IsStandardWordSyncedFile(string lrcPath)
    {
        try
        {
            return StandardLrcFormatter.IsStandardWordSynced(LyricFiles.DecodeText(File.ReadAllBytes(Resolve(lrcPath))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public virtual bool DeleteFile(string path, bool moveToTrash)
    {
        return LyricFiles.Delete(path, moveToTrash);
    }

    // ---- IStandardizeArtistsDataAccess (delegates to ArtistTags and the Claude client) ----

    public virtual List<SongArtistTags> ReadArtistTags(string path)
    {
        List<SongArtistTags> songs = ArtistTags.Read(path);
        PendingAdd pending = _pending;
        return pending != null && pending.IsFor(path) ? pending.MergeArtistTags(songs) : songs;
    }

    public virtual IDictionary<string, List<ParsedArtist>> ParseArtistsWithAi(IDictionary<string, int> rawCounts,
            string apiKey, string model, List<string> notes, Action<string> status)
    {
        return ClaudeArtistParser.Parse(new AnthropicClient(), rawCounts, apiKey, model, null, true, notes, status,
                _artistParseCache);
    }

    public virtual string ReadApiKeyFromEnvironment()
    {
        return WindowsEnvironment.Read("ANTHROPIC_API_KEY");
    }

    public virtual bool WriteArtistTags(SongArtistUpdate update)
    {
        return ArtistTags.Write(update);
    }

    public virtual SongRenameResult RenameSongFiles(string audioPath, string separator)
    {
        return SongRenamer.Rename(audioPath, separator);
    }

    // ---- IStandardizeAlbumsDataAccess (delegates to AlbumTags, AlbumFolders and the Claude client) ----

    public virtual List<SongAlbumTags> ReadAlbumTags(string path)
    {
        List<SongAlbumTags> songs = AlbumTags.Read(path);
        PendingAdd pending = _pending;
        return pending != null && pending.IsFor(path) ? pending.MergeAlbumTags(songs) : songs;
    }

    public virtual IDictionary<string, ParsedAlbum> ParseAlbumsWithAi(IDictionary<string, AlbumRaw> raws, string apiKey,
            string model, List<string> notes, Action<string> status)
    {
        return ClaudeAlbumParser.Parse(new AnthropicClient(), raws, apiKey, model, notes, status, _albumParseCache);
    }

    public virtual bool WriteAlbumTag(SongAlbumUpdate update)
    {
        return AlbumTags.Write(update);
    }

    public virtual AlbumFolderMoveResult MoveToAlbumFolder(AlbumFolderMove move)
    {
        return AlbumFolders.Move(move);
    }

    // ---- IAddSongsDataAccess (delegates to LibraryImporter and PendingAdd) ----

    public virtual AddSongsOutputData AddSongs(string libraryPath, IReadOnlyList<string> sources, string preferredFormat,
            Action<int, int> progress)
    {
        DiscardAdded(); // an add the user never decided on is not wanted any more
        var pending = new PendingAdd(libraryPath, Path.Combine(_pendingFolder, Guid.NewGuid().ToString("N")));
        AddSongsOutputData result;
        try
        {
            result = LibraryImporter.Add(pending, sources, preferredFormat, progress);
        }
        catch (Exception)
        {
            pending.Discard();
            throw;
        }
        if (pending.HasFiles())
        {
            _pending = pending;
        }
        else
        {
            pending.Discard(); // nothing to wait for (everything was in the library already, or could not be added)
        }
        return result;
    }

    public virtual AddCommitOutputData CommitAdded()
    {
        PendingAdd pending = _pending;
        if (pending == null)
        {
            return new AddCommitOutputData(0, null);
        }
        PendingAdd.CommitResult result;
        _committing = true;
        try
        {
            result = pending.Commit();
        }
        finally
        {
            _committing = false;
        }
        if (result.Problem == null)
        {
            _pending = null;
        }
        return new AddCommitOutputData(result.Songs, result.Problem);
    }

    public virtual void DiscardAdded()
    {
        if (_committing)
        {
            return; // the songs are being moved into the library: they are not dropped half-way
        }
        PendingAdd pending = _pending;
        _pending = null;
        pending?.Discard();
    }

    // ---- romaji (part of IStandardizeLyricsDataAccess; delegates to ClaudeRomanizer and its disk cache) ----

    public virtual RomajiEstimate EstimateRomaji(List<RomajiLine> lines)
    {
        return ClaudeRomanizer.Estimate(_romajiCache, lines);
    }

    public virtual RomajiResult Romanize(List<RomajiLine> lines, string apiKey, string model,
            RomajiProgressListener progress)
    {
        return ClaudeRomanizer.Run(new AnthropicClient(), _romajiCache, lines, apiKey, model, progress, Sleep,
                ClaudeRomanizer.DefaultWorkers);
    }

    /// <summary>Thread.sleep: a negative time is an error there, not "forever" as in .NET</summary>
    private static void Sleep(long millis)
    {
        if (millis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(millis), "timeout value is negative");
        }
        Thread.Sleep((int)Math.Min(millis, int.MaxValue));
    }

    /// <summary>java.io.File spells a path with the platform separator and without a trailing one</summary>
    private static string JavaPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// recursively walks a directory, collecting a Music for every audio file
    /// found, mirroring FileManager#listFiles
    /// </summary>
    private void ListMusicFiles(string file, Queue<Music> musics)
    {
        if (file == null || !(File.Exists(file) || Directory.Exists(file)))
        {
            return;
        }

        if (!Directory.Exists(file))
        {
            if (IsAudioFile(Path.GetFileName(file)))
            {
                musics.Enqueue(ReadMusic(file));
            }
            return;
        }

        string[] children;
        try
        {
            children = Directory.GetFileSystemEntries(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return; // File.listFiles() gives null when the folder cannot be listed
        }

        foreach (string child in children)
        {
            ListMusicFiles(child, musics);
        }
    }

    /// <summary>
    /// builds a Music from an audio file, preferring its embedded tag (title,
    /// artist, cover art) over the bare filename when one is readable; falls
    /// back to the filename alone otherwise
    /// </summary>
    private Music ReadMusic(string file)
    {
        Music.Builder builder = new Music.Builder()
                .SetTitle(StripExtension(Path.GetFileName(file)))
                .SetPath(file);

        try
        {
            using (AudioTags tag = AudioTags.Open(file))
            {
                if (tag.HasTag)
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

                    byte[] artwork = tag.GetFirstArtwork();
                    if (artwork != null)
                    {
                        builder.SetCoverArt(artwork);
                    }
                }
            }
        }
        catch (Exception)
        {
            // no readable tag (unsupported/corrupt file); fall back to the
            // filename-only Music already built above
        }

        return builder.Build();
    }

    private bool IsAudioFile(string fileName)
    {
        return IsAudioFileName(fileName);
    }

    /// <summary>
    /// a music's lyric file is expected to sit next to it with the same name
    /// and a ".lrc" extension, per Music's javadoc
    /// </summary>
    /// <returns>the lyric file, or null if none is detected</returns>
    private string FindLyricFile(string musicPath)
    {
        string lrcFile = StripExtension(musicPath) + ".lrc";
        return File.Exists(lrcFile) ? lrcFile : null;
    }

    /// <summary>
    /// parses a .lrc file into a Lyric, classifying it as word synced, line
    /// synced, or no sync depending on the timestamp brackets found
    /// </summary>
    private Lyric ParseLRC(string file)
    {
        List<string> rawLines = new List<string>();
        try
        {
            string text = DecodeLikeScanner(File.ReadAllBytes(file));
            string[] pieces = LineSeparator.Split(text);
            int count = pieces.Length > 0 && pieces[pieces.Length - 1].Length == 0 ? pieces.Length - 1 : pieces.Length;
            for (int i = 0; i < count; i++)
            {
                rawLines.Add(pieces[i].Replace("\uFEFF", ""));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // FileNotFoundException
        }

        List<ParsedLine> drafts = new List<ParsedLine>();
        bool anyWordSynced = false;
        bool anyLineSynced = false;

        foreach (string raw in rawLines)
        {
            string trimmed = JavaString.Trim(raw);
            if (trimmed.Length == 0 || HeaderPattern.IsMatch(trimmed))
            {
                continue;
            }

            List<long> times = new List<long>();
            List<int> ends = new List<int>();
            foreach (Match match in TimePattern.Matches(raw))
            {
                times.Add(ToMillis(match));
                ends.Add(match.Index + match.Length);
            }

            if (times.Count == 0)
            {
                // no timestamp at all: plain, unsynced lyric text
                drafts.Add(new ParsedLine(raw, 0L, 0L, null));
            }
            else if (times.Count == 1)
            {
                // one timestamp: line synced, end time resolved in a second pass below
                string text = raw.Substring(ends[0]);
                drafts.Add(new ParsedLine(text, times[0], null, null));
                anyLineSynced = true;
            }
            else
            {
                // 2+ timestamps: word synced, each bracket pair frames one word and the
                // final bracket marks the line's end time, mirroring toTTML's word loop
                List<WordTimeStamp> words = new List<WordTimeStamp>();
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < times.Count - 1; i++)
                {
                    int textStart = ends[i];
                    int textEnd = raw.IndexOf('[', textStart);
                    string word = textEnd == -1 ? raw.Substring(textStart) : raw.Substring(textStart, textEnd - textStart);
                    words.Add(new WordTimeStamp(word, times[i], times[i + 1]));
                    text.Append(word);
                }
                drafts.Add(new ParsedLine(text.ToString(), times[0], times[times.Count - 1], words));
                anyWordSynced = true;
            }
        }

        // line synced entries have no explicit end time; borrow the next line's start
        for (int i = 0; i < drafts.Count; i++)
        {
            ParsedLine draft = drafts[i];
            if (draft.EndTimeMs == null)
            {
                draft.EndTimeMs = (i + 1 < drafts.Count) ? drafts[i + 1].StartTimeMs : draft.StartTimeMs;
            }
        }

        Lyric.SyncType syncType;
        if (anyWordSynced)
        {
            syncType = Lyric.SyncType.WordSync;
        }
        else if (anyLineSynced)
        {
            syncType = Lyric.SyncType.LineSync;
        }
        else
        {
            syncType = Lyric.SyncType.NoSync;
        }

        List<LyricLine> lines = new List<LyricLine>();
        foreach (ParsedLine draft in drafts)
        {
            LyricLine line = new LyricLine(draft.Text, draft.StartTimeMs, draft.EndTimeMs.Value);
            if (draft.Words != null)
            {
                line.WordTimeStamps = draft.Words.ToArray();
            }
            lines.Add(line);
        }

        return new Lyric(syncType, lines, file);
    }

    /// <summary>
    /// the text java.util.Scanner(file, "UTF-8") gets out of the bytes: it reads 1024 characters at a time through a decoder
    /// that reports malformed input, so the read that meets a bad byte fails and its characters are lost, and the scanner
    /// takes that for the end of the file (a lyric file in GBK, say, gives no lines at all). The chunks are taken as
    /// 1024 characters each; Scanner's buffer can grow, which moves the chunk borders for files with very long lines.
    /// </summary>
    private static string DecodeLikeScanner(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException e)
        {
            string valid = Encoding.UTF8.GetString(bytes, 0, e.Index);
            return valid.Substring(0, valid.Length / 1024 * 1024);
        }
    }

    private long ToMillis(Match match)
    {
        long minutes = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        long seconds = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        long millis = ParseFractionToMillis(match.Groups[3].Success ? match.Groups[3].Value : null);
        return minutes * 60000 + seconds * 1000 + millis;
    }

    private long ParseFractionToMillis(string fraction)
    {
        if (fraction == null || fraction.Length == 0)
        {
            return 0;
        }
        string normalized = fraction.Length >= 3 ? fraction.Substring(0, 3) : (fraction + "000").Substring(0, 3);
        return long.Parse(normalized, CultureInfo.InvariantCulture);
    }

    private string StripExtension(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot == -1 ? path : path.Substring(0, dot);
    }

    private void SaveToFile(string sourcePath, string content, string extension)
    {
        if (sourcePath == null)
        {
            return;
        }
        try
        {
            File.WriteAllBytes(StripExtension(sourcePath) + "." + extension, Utf8Writer.GetBytes(content ?? "null"));
        }
        catch (Exception)
        {
            // best effort, mirrors FileManager#saveToFile
        }
    }

    /// <summary>intermediate holder for a parsed LRC line before end times are resolved</summary>
    private class ParsedLine
    {
        internal readonly string Text;
        internal readonly long StartTimeMs;
        internal long? EndTimeMs;
        internal readonly List<WordTimeStamp> Words;

        internal ParsedLine(string text, long startTimeMs, long? endTimeMs, List<WordTimeStamp> words)
        {
            Text = text;
            StartTimeMs = startTimeMs;
            EndTimeMs = endTimeMs;
            Words = words;
        }
    }
}
