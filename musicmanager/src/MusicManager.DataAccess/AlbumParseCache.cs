using System.Text;
using System.Text.Json.Nodes;
using MusicManager.Common;
using MusicManager.UseCase.StandardizeAlbums;

namespace MusicManager.DataAccess;

/// <summary>
/// remembers, on disk, which album Claude found each raw album value to be, so a library's albums are only ever paid for
/// once: the next run asks about the values it has not seen, and a library that grows by a few songs costs a few lines
/// instead of a full run. The key is the raw value as <see cref="AlbumStandardizer.RawOf"/> gives it, the value the
/// album's name and whether Claude was unsure.
///
/// Safe to use from several threads. An unreadable or damaged file is the same as an empty cache.
/// </summary>
public sealed class AlbumParseCache
{
    private const char ByteOrderMark = (char)0xFEFF;

    private readonly string _file;
    private readonly object _lock = new object();
    private OrderedDictionary<string, ParsedAlbum> _entries;
    private bool _dirty;
    private bool _readFailed; // the file exists but could not be read: it is left alone rather than overwritten

    public AlbumParseCache(string file)
    {
        _file = file;
    }

    /// <summary>~/.musicmanager/album-cache.json, next to the app's other settings</summary>
    public static AlbumParseCache AtDefaultLocation()
    {
        return new AlbumParseCache(Path.Combine(UserHome.Path, ".musicmanager", "album-cache.json"));
    }

    /// <summary>what is remembered for this raw value, or null if it is not known</summary>
    public ParsedAlbum Get(string raw)
    {
        lock (_lock)
        {
            ParsedAlbum album = null;
            if (raw != null)
            {
                Load().TryGetValue(raw, out album);
            }
            return album;
        }
    }

    /// <summary>a blank value is not worth remembering, and is ignored (a blank album name is a real answer: "not an album")</summary>
    public void Put(string raw, ParsedAlbum album)
    {
        lock (_lock)
        {
            if (raw == null || JavaString.IsBlank(raw) || album == null)
            {
                return;
            }
            Load()[raw] = album;
            _dirty = true;
        }
    }

    /// <summary>the album names remembered, each once, the first found first: what later requests are told to reuse</summary>
    public List<string> Names()
    {
        lock (_lock)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ParsedAlbum album in Load().Values)
            {
                if (!album.IsNotAnAlbum && seen.Add(album.Album))
                {
                    names.Add(album.Album);
                }
            }
            return names;
        }
    }

    public int Size()
    {
        lock (_lock)
        {
            return Load().Count;
        }
    }

    /// <summary>writes the cache to disk (temp file, then an atomic move) if it changed; a disk problem is ignored, the cache keeps working from memory</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_entries == null || !_dirty || _readFailed)
            {
                return;
            }
            JsonObject root = new JsonObject();
            foreach (KeyValuePair<string, ParsedAlbum> entry in _entries)
            {
                JsonObject item = new JsonObject();
                item["album"] = entry.Value.Album;
                item["lowConfidence"] = entry.Value.IsLowConfidence;
                root[entry.Key] = item;
            }
            if (SettingsStore.WriteAtomically(_file, ClaudeJson.Pretty(root) + "\n"))
            {
                _dirty = false;
            }
        }
    }

    private OrderedDictionary<string, ParsedAlbum> Load()
    {
        if (_entries == null)
        {
            _entries = new OrderedDictionary<string, ParsedAlbum>(StringComparer.Ordinal);
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
    private void ReadInto(OrderedDictionary<string, ParsedAlbum> target, string text)
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
            _dirty = true; // not an object: the file is replaced on the next flush
            return;
        }
        foreach (KeyValuePair<string, JsonNode> entry in root)
        {
            if (entry.Value is not JsonObject item || item["album"] is not JsonValue name)
            {
                _dirty = true;
                continue;
            }
            bool low = item["lowConfidence"] is JsonValue flag && ClaudeJson.AsString(flag) == "true";
            target[entry.Key] = new ParsedAlbum(ClaudeJson.AsString(name), low);
        }
    }
}
