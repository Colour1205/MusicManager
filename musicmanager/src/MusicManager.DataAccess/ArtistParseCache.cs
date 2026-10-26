using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MusicManager.Common;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.DataAccess;

/// <summary>
/// remembers, on disk, which artists Claude (and the web check) found in each raw artist tag value, so a library's
/// artist names are only ever paid for once: the next run asks about the values it has not seen, and a library that
/// grows by a few songs costs a few lines instead of a full run. The key is the raw tag value exactly as stored in the
/// audio file, the value the artists in it (text, english, lowConfidence, keepNative).
///
/// Safe to use from several threads. An unreadable or damaged file is the same as an empty cache.
/// </summary>
public sealed class ArtistParseCache
{
    private const char ByteOrderMark = (char)0xFEFF;

    private readonly string _file;
    private readonly object _lock = new object();
    private OrderedDictionary<string, List<ParsedArtist>> _entries;
    private bool _dirty; // something changed since the file was last written (or the file needs replacing)
    private bool _readFailed; // the file exists but could not be read: it is left alone rather than overwritten

    public ArtistParseCache(string file)
    {
        _file = file;
    }

    /// <summary>~/.musicmanager/artist-cache.json, next to the app's other settings</summary>
    public static ArtistParseCache AtDefaultLocation()
    {
        return new ArtistParseCache(Path.Combine(UserHome.Path, ".musicmanager", "artist-cache.json"));
    }

    /// <summary>a copy of the artists stored for this raw value (the caller may change it), or null if it is not known</summary>
    public List<ParsedArtist> Get(string raw)
    {
        lock (_lock)
        {
            List<ParsedArtist> artists = null;
            if (raw != null)
            {
                Load().TryGetValue(raw, out artists);
            }
            return artists == null ? null : new List<ParsedArtist>(artists);
        }
    }

    /// <summary>blank values and empty answers are not worth remembering, and are ignored</summary>
    public void Put(string raw, IReadOnlyCollection<ParsedArtist> artists)
    {
        lock (_lock)
        {
            if (raw == null || JavaString.IsBlank(raw) || artists == null || artists.Count == 0)
            {
                return;
            }
            Load()[raw] = new List<ParsedArtist>(artists);
            _dirty = true;
        }
    }

    public int Size()
    {
        lock (_lock)
        {
            return Load().Count;
        }
    }

    /// <summary>
    /// writes the cache to disk (temp file, then an atomic move) if it changed; a disk problem is ignored, the cache is
    /// only a saving and keeps working from memory
    /// </summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_entries == null || !_dirty || _readFailed)
            {
                return;
            }
            JsonObject root = new JsonObject();
            foreach (KeyValuePair<string, List<ParsedArtist>> entry in _entries)
            {
                JsonArray artists = new JsonArray();
                foreach (ParsedArtist artist in entry.Value)
                {
                    JsonObject item = new JsonObject();
                    item["text"] = artist.Text;
                    item["english"] = artist.English;
                    item["lowConfidence"] = artist.IsLowConfidence;
                    item["keepNative"] = artist.IsKeepNative;
                    if (artist.IsResearched)
                    {
                        item["researched"] = true; // only written when set, so an older file reads the same as before
                    }
                    artists.Add(item);
                }
                root[entry.Key] = artists;
            }
            if (SettingsStore.WriteAtomically(_file, ClaudeJson.Pretty(root) + "\n"))
            {
                _dirty = false;
            }
        }
    }

    private OrderedDictionary<string, List<ParsedArtist>> Load()
    {
        if (_entries == null)
        {
            _entries = new OrderedDictionary<string, List<ParsedArtist>>(StringComparer.Ordinal);
            if (File.Exists(_file))
            {
                try
                {
                    // decoded by hand: bytes that are not UTF-8 become replacement characters instead of an exception
                    ReadInto(_entries, new UTF8Encoding(false).GetString(File.ReadAllBytes(_file)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _readFailed = true; // cannot tell what is in there, so do not replace it
                }
            }
        }
        return _entries;
    }

    /// <summary>takes every well-formed entry of the file's text; a damaged file or entry is dropped and the file is replaced on the next flush</summary>
    private void ReadInto(OrderedDictionary<string, List<ParsedArtist>> target, string text)
    {
        if (text.Length > 0 && text[0] == ByteOrderMark)
        {
            text = text.Substring(1);
        }
        JsonObject root;
        try
        {
            root = ClaudeJson.Parse(text) as JsonObject;
        }
        catch (Exception e) when (ClaudeJson.IsParseFailure(e))
        {
            root = null;
        }
        if (root == null)
        {
            _dirty = true; // not a cache file at all (or empty): start a new one
            return;
        }
        foreach (KeyValuePair<string, JsonNode> entry in root)
        {
            List<ParsedArtist> artists = ReadArtists(entry.Value);
            if (artists == null)
            {
                _dirty = true;
            }
            else
            {
                target[entry.Key] = artists;
            }
        }
    }

    /// <summary>the artists of one entry, or null if any of them is unusable (the raw value is then simply asked again)</summary>
    private static List<ParsedArtist> ReadArtists(JsonNode element)
    {
        if (element is not JsonArray array || array.Count == 0)
        {
            return null;
        }
        List<ParsedArtist> artists = new List<ParsedArtist>();
        foreach (JsonNode item in array)
        {
            if (item is not JsonObject jsonObject)
            {
                return null;
            }
            string text = GetText(jsonObject, "text");
            string english = GetText(jsonObject, "english");
            if (text == null || JavaString.IsBlank(text) || english == null || JavaString.IsBlank(english))
            {
                return null;
            }
            artists.Add(new ParsedArtist(text, english, Flag(jsonObject, "lowConfidence"), Flag(jsonObject, "keepNative"),
                    Flag(jsonObject, "researched")));
        }
        return artists;
    }

    private static string GetText(JsonObject jsonObject, string key)
    {
        return jsonObject[key] is JsonValue primitive && primitive.GetValueKind() == JsonValueKind.String
                ? primitive.GetValue<string>() : null;
    }

    private static bool Flag(JsonObject jsonObject, string key)
    {
        return jsonObject[key] is JsonValue primitive && primitive.GetValueKind() == JsonValueKind.True;
    }
}
