using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.DataAccess;

/// <summary>
/// asks Claude which album each raw album value of the library is and what that album is officially called. The model's
/// answer is never trusted blindly: every input gets an entry no matter what the model returned, so the plan can't silently
/// lose a value, and the answers are remembered in an <see cref="AlbumParseCache"/>.
/// </summary>
public static class ClaudeAlbumParser
{
    public const string ToolName = "record_albums";
    public const int ChunkSize = 80;
    public const int MaxTokens = 16000;
    public const int MaxSearches = 8;
    // earlier answers' names are repeated in every later request; bounded so a huge library can't make each request grow
    public const int MaxKnownNames = 800;

    private const string ToolDescription = "Record which album each raw album value is, and the album's standard name.";

    private const string Schema =
            "{\"type\":\"object\",\"properties\":{\"results\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"n\":{\"type\":\"integer\"},\"album\":{\"type\":\"string\"},\"lowConfidence\":{\"type\":\"boolean\"}}," +
            "\"required\":[\"n\",\"album\"]}}},\"required\":[\"results\"]}";

    public const string SystemPrompt =
            "You standardize the album names of a personal music library. Each request lists raw album values exactly as " +
            "they are stored in the audio files' ALBUM tags - or, for a song that has no album tag, the name of the folder " +
            "it is kept in (source \"folder\") - with how many songs use each, who sings them and the folders they are in. " +
            "The values come from many sources, so one album is spelled in many ways. For EVERY value say which album it " +
            "is and give that album's standard name. Report by calling the record_albums tool exactly once.\n" +
            "\n" +
            "WHAT IS THE SAME ALBUM\n" +
            "- Group by the album's NAME, never by its artist: the songs of a compilation or a soundtrack whose artists " +
            "differ from song to song are still one album.\n" +
            "- Spellings of one release are one album: differences of case, spacing, punctuation, full-width or half-width " +
            "characters, Traditional or Simplified Chinese; notes about the file such as [Hi-Res 24bit-96khz], " +
            "[24bit Hi-Res], [BluRay 24bit Hi-Res], (FLAC) or [320K]; a bracketed year; a cover or edition variant that " +
            "has the same tracks, such as (NINGNING Special Version), (Limited Edition) or (Clean).\n" +
            "- A different release is a different album, even when its name starts the same: a single and the album it " +
            "comes from, an EP and the full album, a Deluxe or Repackage edition with more tracks, a version in another " +
            "language (JP Ver., Chinese Ver.), a live album, a remix album, an instrumental, a part of a soundtrack. Do " +
            "not merge those. When you are not sure whether two values are one release, keep them apart and set " +
            "lowConfidence.\n" +
            "\n" +
            "THE STANDARD NAME\n" +
            "- Change as little as possible. A value that stands alone - it has no note about quality, format or a year, " +
            "and no other spelling of the same album is in the request - is its own album name: return it exactly as it is " +
            "written (only stray spaces tidied). Do not shorten it, reorder it, translate it, romanize it, or \"fix\" its " +
            "capitalization, quotes or punctuation. Keep every part of the title, such as \"- The 2nd Album\", \"- EP\", " +
            "\"- Single\", \"(Live)\", \"Pt. 1\", \"(Original Soundtrack)\" or \"NewJeans 2nd EP 'Get Up'\".\n" +
            "- When several values are one album, the standard name is the spelling most songs use, without the notes about " +
            "quality, file format, the edition's cover or a year (\"Palette [Hi-Res 24bit-96khz]\" => \"Palette\"). When the " +
            "spellings differ only in case, spacing or punctuation, take the official one (as the label writes it on Apple " +
            "Music or Spotify) if you know it, otherwise the commonest.\n" +
            "- Language and script stay as the release has them: a Chinese-language album keeps its Chinese title (Simplified " +
            "or Traditional, as the library mostly writes it), a Korean release with an English title keeps the English " +
            "title. Never translate a title and never romanize it.\n" +
            "- A value from a folder name may carry the artist (\"IU - Palette\"), a year or a format: the album is only " +
            "the title.\n" +
            "- A value that is no album name at all (\"Unknown\", \"Track 01\", \"Various\", \"Single\", a bare number or a " +
            "path) gets the album \"\" (empty): the song is left as it is.\n" +
            "- A value that is the name of a playlist or a compilation of other people's songs rather than of a release " +
            "(\"Bittersweet Indie\", \"Top Hits 2021\", \"K-pop Workout\") is not the song's album. The titles listed with it " +
            "say which songs these are: when you are sure which release they are really on, give that album and set " +
            "lowConfidence; when you are not, keep the value as it is and set lowConfidence.\n" +
            "- The SAME album must get byte-identical album strings, in this request and in every other one. If the " +
            "request lists \"Names already used\", an album that is already in that list MUST get exactly that string.\n" +
            "- Use web search when you are unsure whether two values are the same release or how a title is officially " +
            "spelled, before you give up and set lowConfidence; one search can settle several. Never invent a title.\n" +
            "\n" +
            "Set lowConfidence to true only when you CHANGED or MERGED something and are not sure it was right: you guessed " +
            "the official title, you merged spellings on a hunch, you are unsure whether values are one release, or a folder " +
            "name could not be cleaned with confidence. A value you return unchanged is never lowConfidence.\n" +
            "\n" +
            "OUTPUT\n" +
            "Return one result for EVERY numbered value (n as given), in any order. Never skip a number, never merge two " +
            "numbers into one result.";

    /// <summary>
    /// Answers for every key of raws (in its order). A value the cache knows is not sent to Claude; the others are asked
    /// about in chunks, and each chunk's final answers are put in the cache and flushed to disk, so a run that stops
    /// halfway keeps what it finished.
    /// </summary>
    /// <param name="notes">receives a sentence for anything the user should know; may be null</param>
    /// <param name="status">receives a short progress sentence before each request; may be null</param>
    /// <param name="cache">where answers are remembered; null for no memory at all</param>
    /// <exception cref="ArtistAiException">if the service cannot be used or the answer is mostly unusable</exception>
    public static OrderedDictionary<string, ParsedAlbum> Parse(AnthropicClient client, IDictionary<string, AlbumRaw> raws,
            string apiKey, string model, List<string> notes, Action<string> status, AlbumParseCache cache)
    {
        var decided = new Dictionary<string, ParsedAlbum>(StringComparer.Ordinal);
        var toAsk = new List<string>();
        int remembered = 0;
        foreach (string raw in raws.Keys)
        {
            if (JavaString.IsBlank(raw))
            {
                decided[raw] = new ParsedAlbum("", false);
                continue;
            }
            ParsedAlbum known = cache?.Get(raw);
            if (known == null)
            {
                toAsk.Add(raw);
            }
            else
            {
                decided[raw] = known;
                remembered++;
            }
        }

        // the names to reuse: the ones answered before, then the ones answered in this run
        var knownNames = new List<string>();
        var knownSet = new HashSet<string>(StringComparer.Ordinal);
        void AddName(string name)
        {
            if (!string.IsNullOrEmpty(name) && knownSet.Add(name))
            {
                knownNames.Add(name);
            }
        }
        if (cache != null)
        {
            cache.Names().ForEach(AddName);
        }
        if (status != null && toAsk.Count == 0 && remembered > 0)
        {
            status("All " + remembered + " album names were already known, nothing to ask Claude.");
        }

        for (int start = 0; start < toAsk.Count; start += ChunkSize)
        {
            int end = Math.Min(start + ChunkSize, toAsk.Count);
            List<string> chunk = toAsk.GetRange(start, end - start);
            if (status != null)
            {
                status("Asking Claude to group the album names (" + end + " of " + toAsk.Count
                        + (remembered == 0 ? "" : " new ones, " + remembered + " already known") + ")...");
            }
            JsonObject answer = client.CallTool(apiKey, model, SystemPrompt, UserMessage(chunk, raws, knownNames),
                    ToolName, ToolDescription, ClaudeJson.Parse(Schema).AsObject(), MaxTokens,
                    new AnthropicClient.Options("medium", MaxSearches));
            var repaired = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, ParsedAlbum> validated = Validate(chunk, answer, repaired);
            foreach (KeyValuePair<string, ParsedAlbum> pair in validated)
            {
                decided[pair.Key] = pair.Value;
                if (!repaired.Contains(pair.Key))
                {
                    AddName(pair.Value.Album);
                    cache?.Put(pair.Key, pair.Value); // only final answers: a value the model skipped is asked again next time
                }
            }
            cache?.Flush();
        }

        var result = new OrderedDictionary<string, ParsedAlbum>(StringComparer.Ordinal);
        foreach (string raw in raws.Keys)
        {
            result[raw] = decided[raw];
        }
        return result;
    }

    /// <summary>the numbered raw values with their context (as JSON, so quotes and escapes are exact), then the names to reuse</summary>
    private static string UserMessage(List<string> chunk, IDictionary<string, AlbumRaw> raws, List<string> knownNames)
    {
        StringBuilder message = new StringBuilder();
        message.Append("Raw album values (JSON, one per line; n is only a running number, count is how many songs use that ")
                .Append("exact value):\n");
        for (int i = 0; i < chunk.Count; i++)
        {
            AlbumRaw raw = raws[chunk[i]];
            JsonArray artists = new JsonArray();
            foreach (string artist in raw.Artists)
            {
                artists.Add(JsonValue.Create(artist));
            }
            JsonArray folders = new JsonArray();
            foreach (string folder in raw.Folders)
            {
                folders.Add(JsonValue.Create(folder));
            }
            JsonObject entry = new JsonObject();
            entry["n"] = i + 1;
            entry["raw"] = chunk[i];
            entry["count"] = raw.Count;
            entry["source"] = raw.IsFromFolder ? "folder" : "tag";
            entry["artists"] = artists;
            entry["folders"] = folders;
            JsonArray titles = new JsonArray();
            foreach (string title in raw.Titles)
            {
                titles.Add(JsonValue.Create(title));
            }
            entry["titles"] = titles;
            message.Append(ClaudeJson.Compact(entry)).Append('\n');
        }

        if (knownNames.Count > 0)
        {
            JsonArray names = new JsonArray();
            foreach (string name in knownNames.Take(MaxKnownNames))
            {
                names.Add(JsonValue.Create(name));
            }
            message.Append("\nNames already used - reuse EXACTLY these strings when the same album appears:\n")
                    .Append(ClaudeJson.Compact(names)).Append('\n');
        }
        message.Append("\nCall ").Append(ToolName).Append(" with one result for each of the ").Append(chunk.Count)
                .Append(" values above.");
        return message.ToString();
    }

    /// <summary>
    /// matches the model's results back to the chunk's values by number and repairs what is wrong with them: a value the
    /// model skipped keeps its own (tidied) text as its album and is marked "please check" (and listed in repaired)
    /// </summary>
    private static Dictionary<string, ParsedAlbum> Validate(List<string> chunk, JsonObject answer, ISet<string> repaired)
    {
        var byNumber = new Dictionary<int, JsonObject>();
        foreach (JsonNode element in AsArray(answer["results"]))
        {
            if (element is JsonObject item && item["n"] is JsonValue number && ClaudeJson.TryGetAsInt(number, out int n)
                    && n >= 1 && n <= chunk.Count && item["album"] is JsonValue)
            {
                byNumber.TryAdd(n, item);
            }
        }

        var validated = new Dictionary<string, ParsedAlbum>(StringComparer.Ordinal);
        for (int i = 0; i < chunk.Count; i++)
        {
            string raw = chunk[i];
            if (byNumber.TryGetValue(i + 1, out JsonObject item))
            {
                validated[raw] = new ParsedAlbum(CleanName(ClaudeJson.GetString(item, "album")), Flag(item, "lowConfidence"));
            }
            else
            {
                validated[raw] = new ParsedAlbum(AlbumStandardizer.CleanLocally(raw), true);
                repaired.Add(raw);
            }
        }
        if (repaired.Count * 3 > chunk.Count)
        {
            throw new ArtistAiException("The AI answer was incomplete");
        }
        return validated;
    }

    /// <summary>one canonical spelling for names: composed Unicode, single spaces, no outer whitespace</summary>
    private static string CleanName(string name)
    {
        return name == null ? "" : ArtistStandardizer.NormalizeRaw(name);
    }

    private static bool Flag(JsonObject jsonObject, string key)
    {
        if (jsonObject[key] is not JsonValue primitive)
        {
            return false;
        }
        switch (primitive.GetValueKind())
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return string.Equals(JavaString.Strip(ClaudeJson.AsString(primitive)), "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>the array itself, or one the model sent as a JSON string (a known quirk); empty if neither</summary>
    private static JsonArray AsArray(JsonNode element)
    {
        if (element is JsonArray array)
        {
            return array;
        }
        if (element is JsonValue primitive && primitive.GetValueKind() == JsonValueKind.String)
        {
            try
            {
                if (ClaudeJson.Parse(primitive.GetValue<string>()) is JsonArray parsed)
                {
                    return parsed;
                }
            }
            catch (Exception e) when (ClaudeJson.IsParseFailure(e))
            {
                // not JSON: treated as no array
            }
        }
        return new JsonArray();
    }
}
