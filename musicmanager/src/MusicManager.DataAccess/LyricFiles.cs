using System.Text;
using Microsoft.VisualBasic.FileIO;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.DataAccess;

/// <summary>
/// file-system side of lyrics standardization: finding each song's lyric sidecars,
/// reading them with the right parser and text encoding, and writing/deleting safely.
/// DataAccess delegates its IStandardizeLyricsDataAccess methods here.
/// </summary>
public static class LyricFiles
{
    // GB18030 lives in the code pages provider, not in the default encodings; decoding must report bad bytes
    private static readonly Lazy<Encoding> StrictGb18030 = new Lazy<Encoding>(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    });

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    // String.getBytes(UTF_8) writes an unpaired surrogate as '?', .NET's default would write U+FFFD
    private static readonly Encoding Utf8Writer = Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"),
        DecoderFallback.ReplacementFallback);

    /// <summary>every audio file under root with the .lrc/.ttml/.hmrc files sharing its base name, in folder/name order</summary>
    public static List<SongLyricFiles> FindSongs(string root)
    {
        List<SongLyricFiles> songs = new List<SongLyricFiles>();
        // java.io.File spells a path with the platform separator and without a trailing one
        Walk(Path.TrimEndingDirectorySeparator(root.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)),
            songs);
        return songs;
    }

    private static void Walk(string dir, List<SongLyricFiles> output)
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
        children = children.OrderBy(child => Path.GetFileName(child).ToLowerInvariant(), StringComparer.Ordinal).ToArray();

        // files of this folder grouped by case-insensitive base name, e.g. "Song" -> [Song.flac, Song.lrc, Song.ttml]
        OrderedDictionary<string, List<string>> byBase = new OrderedDictionary<string, List<string>>();
        List<string> subDirs = new List<string>();
        foreach (string child in children)
        {
            if (Directory.Exists(child))
            {
                subDirs.Add(child);
            }
            else
            {
                string key = BaseName(Path.GetFileName(child)).ToLowerInvariant();
                if (!byBase.TryGetValue(key, out List<string> group))
                {
                    group = new List<string>();
                    byBase.Add(key, group);
                }
                group.Add(child);
            }
        }

        foreach (List<string> group in byBase.Values)
        {
            string audio = null;
            string lrc = null;
            string ttml = null;
            string hmrc = null;
            foreach (string f in group)
            {
                string name = Path.GetFileName(f);
                string ext = Extension(name);
                if (DataAccess.IsAudioFileName(name))
                {
                    if (audio == null || AudioRank(ext) < AudioRank(Extension(Path.GetFileName(audio))))
                    {
                        audio = f;
                    }
                }
                else if (ext == ".lrc")
                {
                    lrc = f;
                }
                else if (ext == ".ttml")
                {
                    ttml = f;
                }
                else if (ext == ".hmrc")
                {
                    hmrc = f;
                }
            }
            // sidecars without an audio file are left alone (the audio may just have been renamed)
            if (audio != null)
            {
                output.Add(new SongLyricFiles(audio, lrc, ttml, hmrc));
            }
        }

        foreach (string sub in subDirs)
        {
            Walk(sub, output);
        }
    }

    private static int AudioRank(string ext)
    {
        for (int i = 0; i < DataAccess.AudioExtensions.Length; i++)
        {
            if (DataAccess.AudioExtensions[i] == ext)
            {
                return i;
            }
        }
        return int.MaxValue;
    }

    private static string BaseName(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName.Substring(0, dot) : fileName;
    }

    private static string Extension(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot >= 0 ? fileName.Substring(dot).ToLowerInvariant() : "";
    }

    /// <summary>reads and parses a lyric file, choosing the parser by extension</summary>
    public static Lyric ReadLyric(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LyricParseException("Could not read " + Path.GetFileName(path), e);
        }

        string lower = path.ToLowerInvariant();
        if (lower.EndsWith(".ttml", StringComparison.Ordinal))
        {
            return TtmlParser.Parse(bytes, path);
        }
        string text = DecodeText(bytes);
        if (lower.EndsWith(".hmrc", StringComparison.Ordinal))
        {
            return HmrcParser.Parse(text, path);
        }
        return LrcParser.Parse(text, path);
    }

    /// <summary>
    /// decodes text of unknown encoding: BOM-marked UTF-8/UTF-16, then strict UTF-8,
    /// then GB18030 (common for older Chinese .lrc files), then lossy UTF-8
    /// </summary>
    public static string DecodeText(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        }

        string strictUtf8 = DecodeStrict(b, StrictUtf8);
        if (strictUtf8 != null)
        {
            return strictUtf8;
        }
        string gb = DecodeStrict(b, StrictGb18030.Value);
        if (gb != null && ContainsHan(gb))
        {
            return gb;
        }
        return Encoding.UTF8.GetString(b);
    }

    private static string DecodeStrict(byte[] b, Encoding strict)
    {
        try
        {
            return strict.GetString(b);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool ContainsHan(string s)
    {
        foreach (Rune rune in s.EnumerateRunes())
        {
            if (IsHan(rune.Value))
            {
                return true;
            }
        }
        return false;
    }

    // Character.UnicodeScript.HAN (Unicode 13 and later); .NET has no script property
    private static bool IsHan(int cp)
    {
        return (cp >= 0x2E80 && cp <= 0x2E99) || (cp >= 0x2E9B && cp <= 0x2EF3) || (cp >= 0x2F00 && cp <= 0x2FD5)
            || cp == 0x3005 || cp == 0x3007 || (cp >= 0x3021 && cp <= 0x3029) || (cp >= 0x3038 && cp <= 0x303B)
            || (cp >= 0x3400 && cp <= 0x4DBF) || (cp >= 0x4E00 && cp <= 0x9FFF) || (cp >= 0xF900 && cp <= 0xFA6D)
            || (cp >= 0xFA70 && cp <= 0xFAD9) || cp == 0x16FE2 || cp == 0x16FE3 || (cp >= 0x16FF0 && cp <= 0x16FF1)
            || (cp >= 0x20000 && cp <= 0x2A6DF) || (cp >= 0x2A700 && cp <= 0x2B739) || (cp >= 0x2B740 && cp <= 0x2B81D)
            || (cp >= 0x2B820 && cp <= 0x2CEA1) || (cp >= 0x2CEB0 && cp <= 0x2EBE0) || (cp >= 0x2F800 && cp <= 0x2FA1D)
            || (cp >= 0x30000 && cp <= 0x3134A);
    }

    /// <summary>writes UTF-8 (no BOM) via a temp file and an atomic replace, so a crash never leaves a half-written .lrc</summary>
    public static void WriteText(string path, string content)
    {
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, Utf8Writer.GetBytes(content));
            // MoveFileEx with MOVEFILE_REPLACE_EXISTING, the same call Java's REPLACE_EXISTING + ATOMIC_MOVE ends up in
            File.Move(tmp, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception)
            {
                // best effort cleanup
            }
            // Java wrapped this in an UncheckedIOException; .NET has no checked exceptions, so it is an IOException
            if (e is UnauthorizedAccessException)
            {
                throw new IOException(e.Message, e);
            }
            throw;
        }
    }

    // the shell's recycle bin call is not made for several threads at once: songs are processed in parallel, their recycling is queued
    private static readonly object RecycleBin = new object();

    /// <summary>deletes (or recycles) a file; when recycling was asked for but is unavailable the file is kept and false is returned</summary>
    public static bool Delete(string path, bool moveToTrash)
    {
        if (!File.Exists(path))
        {
            return true;
        }
        if (moveToTrash)
        {
            try
            {
                lock (RecycleBin)
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                return !File.Exists(path);
            }
            catch (Exception)
            {
                // fall through: not recycled
            }
            return false;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return !File.Exists(path);
    }
}
