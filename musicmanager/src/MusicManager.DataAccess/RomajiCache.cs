using System.Text;
using System.Text.Json.Nodes;
using MusicManager.Common;

namespace MusicManager.DataAccess;

/// <summary>
/// remembers every line Claude has romanized, on disk, so a library is never paid for twice: a run that stops halfway
/// (rate limit, no network) keeps what it did, and the next run only asks about the rest. The key is the line's words,
/// the value their romanization.
/// </summary>
public sealed class RomajiCache
{
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    // a file that is not UTF-8 is an unreadable cache, not one full of replacement characters (Files.readString throws)
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private const char ByteOrderMark = (char)0xFEFF;

    private readonly string _file;
    private readonly object _lock = new object();
    private Dictionary<string, List<string>> _entries;

    public RomajiCache(string file)
    {
        _file = file;
    }

    /// <summary>~/.musicmanager/romaji-cache.json, next to the app's other settings</summary>
    public static RomajiCache AtDefaultLocation()
    {
        return new RomajiCache(Path.Combine(UserHome.Path, ".musicmanager", "romaji-cache.json"));
    }

    public IReadOnlyList<string> Get(string key)
    {
        lock (_lock)
        {
            Dictionary<string, List<string>> entries = Load();
            return key != null && entries.TryGetValue(key, out List<string> words) ? words : null;
        }
    }

    public void Put(string key, IEnumerable<string> romanizedWords)
    {
        lock (_lock)
        {
            Load()[key] = new List<string>(romanizedWords);
        }
    }

    public int Size()
    {
        lock (_lock)
        {
            return Load().Count;
        }
    }

    /// <summary>writes the cache to disk (temp file, then an atomic move); a disk problem is ignored, the cache is only a saving</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_entries == null)
            {
                return;
            }
            try
            {
                JsonObject root = new JsonObject();
                foreach (KeyValuePair<string, List<string>> entry in _entries)
                {
                    JsonArray words = null;
                    if (entry.Value != null)
                    {
                        words = new JsonArray();
                        foreach (string word in entry.Value)
                        {
                            words.Add(word == null ? null : JsonValue.Create(word));
                        }
                    }
                    root[entry.Key] = words;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file)));
                string temp = _file + ".tmp";
                File.WriteAllText(temp, ClaudeJson.CompactHtmlSafe(root), Utf8);
                File.Move(temp, _file, true);
            }
            catch (Exception)
            {
                // keep working from memory: a disk problem (or anything else) must not stop a run
            }
        }
    }

    private Dictionary<string, List<string>> Load()
    {
        if (_entries == null)
        {
            _entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (File.Exists(_file))
            {
                try
                {
                    Dictionary<string, List<string>> stored = Read(File.ReadAllText(_file, StrictUtf8));
                    if (stored != null)
                    {
                        foreach (KeyValuePair<string, List<string>> entry in stored)
                        {
                            _entries[entry.Key] = entry.Value;
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException
                        || ClaudeJson.IsParseFailure(e))
                {
                    // an unreadable cache is no better than an empty one; it is replaced on the next flush
                }
            }
        }
        return _entries;
    }

    /// <summary>
    /// what Gson makes of the text as a Map&lt;String, List&lt;String&gt;&gt;: null for an empty file or the literal null,
    /// an exception for anything that is not an object of arrays of strings (numbers and booleans are taken as their text)
    /// </summary>
    private static Dictionary<string, List<string>> Read(string text)
    {
        if (text.Length > 0 && text[0] == ByteOrderMark)
        {
            text = text.Substring(1); // Gson's reader skips a byte order mark
        }
        if (JavaString.IsBlank(text))
        {
            return null;
        }
        JsonNode parsed = ClaudeJson.Parse(text);
        if (parsed == null)
        {
            return null;
        }
        if (parsed is not JsonObject root)
        {
            throw new InvalidOperationException("not an object");
        }
        Dictionary<string, List<string>> stored = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode> entry in root)
        {
            if (entry.Value == null)
            {
                stored[entry.Key] = null;
                continue;
            }
            if (entry.Value is not JsonArray array)
            {
                throw new InvalidOperationException("not an array");
            }
            List<string> words = new List<string>(array.Count);
            foreach (JsonNode word in array)
            {
                if (word == null)
                {
                    words.Add(null);
                }
                else if (word is JsonValue value)
                {
                    words.Add(ClaudeJson.AsString(value));
                }
                else
                {
                    throw new InvalidOperationException("not a string");
                }
            }
            stored[entry.Key] = words;
        }
        return stored;
    }
}
