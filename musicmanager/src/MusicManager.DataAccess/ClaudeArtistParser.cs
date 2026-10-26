using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.DataAccess;

/// <summary>
/// asks Claude to split raw artist tag values into individual artists and to name each one in
/// English. The model's answer is never trusted blindly: every input gets an entry no matter
/// what the model returned, so the standardization plan can't silently lose a tag value.
/// </summary>
public static class ClaudeArtistParser
{
    /// <summary>told after every request how many strings are done</summary>
    public delegate void ProgressListener(int doneStrings, int totalStrings);

    public const string ToolName = "record_artists";
    public const int ChunkSize = 120;
    public const int MaxTokens = 16000;
    // earlier batches' names are repeated in every later request; bounded so a huge library
    // can't make each request grow without limit (the first names are the most frequent artists)
    public const int MaxKnownNames = 1000;

    private const string ToolDescription = "Record the individual artists named by each raw artist tag value.";

    private const string Schema =
            "{\"type\":\"object\",\"properties\":{\"results\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"raw\":{\"type\":\"string\"},\"artists\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"text\":{\"type\":\"string\"},\"english\":{\"type\":\"string\"},\"lowConfidence\":{\"type\":\"boolean\"}," +
            "\"native\":{\"type\":\"boolean\"}}," +
            "\"required\":[\"text\",\"english\"]}}},\"required\":[\"raw\",\"artists\"]}}},\"required\":[\"results\"]}";

    public const string SystemPrompt =
            "You standardize the artist tags of a personal music library. Each request lists raw artist tag values " +
            "exactly as they are stored in audio files; they come from many sources, so one artist is spelled in many " +
            "ways. For EVERY raw value, find the individual artists it names and give each one's official English name. " +
            "Report by calling the record_artists tool exactly once.\n" +
            "\n" +
            "SPLITTING\n" +
            "Split each raw value into individual artists using your knowledge of real artists. Separators seen in the " +
            "wild include / & ; , 、 ， + x feat. ft. featuring with and the symbol ␟ (U+241F), with or without spaces " +
            "around them. The same characters can also be PART of one artist's name, so never split blindly. These are " +
            "single artists: K/DA, HUNTR/X, AC/DC, Tyler, The Creator, Earth, Wind & Fire, (G)I-DLE, G.E.M., S.H.E, " +
            "Lil Nas X, Ty Dolla $ign. Decide by asking whether each piece is a real artist on its own. A raw value " +
            "that names one artist stays one entry.\n" +
            "\n" +
            "For each artist return:\n" +
            "- text: the exact substring of the raw value that names this artist, copied character for character " +
            "(same case, spacing, punctuation and script), INCLUDING any parenthesised translation, group or marker " +
            "attached to it, e.g. \"에스파 (aespa)\" or \"민니 ((여자)아이들)\". Never include the separators between " +
            "artists. Do not tidy it or fix typos in it: it must be found in the raw value by a plain substring search.\n" +
            "- english: the artist's standard name: the official English / Latin-script name, except for Chinese-language " +
            "artists, who keep their Chinese name (rules below).\n" +
            "- lowConfidence: true when you are unsure (rules below), otherwise false.\n" +
            "- native: true only for a Chinese-language artist whose name stays Chinese (rules below), otherwise false.\n" +
            "\n" +
            "NAMES\n" +
            "- Use the artist's official stylization: aespa, (G)I-DLE, IU, ROSÉ, NewJeans, BABYMONSTER, K/DA.\n" +
            "- Korean and Japanese artists: the official English / Latin-script name. A name written only in Korean or " +
            "Japanese: use the artist's official English name if one exists (에스파 => aespa), otherwise romanize it " +
            "with normal capitalization (Revised Romanization for Korean, Hepburn for Japanese).\n" +
            "- Chinese-language artists (Mandarin or Cantonese, from mainland China, Hong Kong, Taiwan, Singapore or " +
            "Malaysia) KEEP THEIR CHINESE NAME. english is the name they are officially credited under in the Chinese " +
            "market, written in Chinese characters, and native is true: 周杰伦 stays 周杰伦, 薛之谦 stays 薛之谦, 周兴哲 " +
            "stays 周兴哲, and 邓紫棋 is credited \"G.E.M. 邓紫棋\" (that whole string is her name). Never turn such a " +
            "name into pinyin or an English name. Traditional and Simplified spellings of one artist get the same " +
            "result, written in the script the raw values use most (周杰倫 and 周杰伦 => the same one).\n" +
            "- A native-script name followed by a parenthesised Latin name is one artist written twice: " +
            "\"에스파 (aespa)\" => aespa, \"리사 (LISA)\" => LISA. (For a Chinese-language artist the Chinese credit wins: " +
            "\"G.E.M. 邓紫棋\" and \"G.E.M.\" are both \"G.E.M. 邓紫棋\".)\n" +
            "- A parenthesised group name after a member's name is only a descriptor, not part of the name: " +
            "\"민니 ((여자)아이들)\" => MINNIE, \"나연 (TWICE)\" => NAYEON.\n" +
            "- A group and its members are DIFFERENT artists. Never merge them (MINNIE and (G)I-DLE are two artists).\n" +
            "- The SAME artist must get byte-identical english everywhere, whatever script, capitalization, spacing, " +
            "punctuation or typo the raw value used, and in every request: \"aespa\", \"Aespa\" and \"에스파 (aespa)\" => " +
            "aespa; \"K-DA\" and \"K/DA\" => K/DA; \"i-dle\", \"(G)I-DLE\" and \"i-dle (아이들)\" => (G)I-DLE. When the library " +
            "spells one artist several ways, use the official spelling, otherwise the spelling with the highest count.\n" +
            "- If the request lists \"Names already used\", an artist who is already in that list MUST get exactly that " +
            "string.\n" +
            "- Fix an obvious typo only when you are confident (Celing Dion => Celine Dion).\n" +
            "- A Latin-script name you do not recognize: keep it as written (only tidy the whitespace). Do not invent " +
            "a correction.\n" +
            "- Placeholders for \"various artists\" (V.A., VA, Various, 群星, 华语群星, 各种艺人) => english exactly " +
            "\"Various Artists\".\n" +
            "- Drop junk markers such as [重复], (重复), [duplicate] or (dup): they are not part of the name, so they " +
            "never appear in english (they stay in text, which copies what the raw value wrote).\n" +
            "- english is only the name: no roles, no descriptions, no \"feat.\".\n" +
            "\n" +
            "Set lowConfidence to true when you are guessing a romanization or an official name (you do not know it), " +
            "when you merged spellings because you suspect a typo, when you are unsure whether a separator splits one " +
            "name, when you do not know who the artist is, or whenever you are otherwise unsure. Leave it false for " +
            "artists you know well.\n" +
            "\n" +
            "OUTPUT\n" +
            "Each input has a count: how many tags use that exact string. It is only a hint (the more frequent spelling " +
            "is usually the established one); every string still needs its own entry. Return one entry in results for " +
            "EVERY input string, in any order, with raw copied exactly (character for character, including leading " +
            "and trailing spaces) so it can be matched back. Never skip a string, never merge two strings into one " +
            "entry, and never return an empty artists list: for a value that is not a name at all, return it as a " +
            "single artist whose text and english are the value, with lowConfidence true. List the artists in the " +
            "order they appear in the raw value.";

    private static readonly Regex Whitespace = new Regex("\\s+", RegexOptions.CultureInvariant); // .NET's \s is Unicode white space, like Java's with UNICODE_CHARACTER_CLASS

    /// <summary>
    /// A set that keeps the order its members were added in (Java's LinkedHashSet).
    /// </summary>
    private sealed class OrderedSet : IEnumerable<string>
    {
        private readonly List<string> _items = new List<string>();
        private readonly HashSet<string> _members = new HashSet<string>(StringComparer.Ordinal);

        public int Count => _items.Count;

        public bool Add(string item)
        {
            if (!_members.Add(item))
            {
                return false;
            }
            _items.Add(item);
            return true;
        }

        public void AddAll(IEnumerable<string> items)
        {
            foreach (string item in items.ToList())
            {
                Add(item);
            }
        }

        public void RemoveAll(OrderedSet items)
        {
            foreach (string item in items)
            {
                if (_members.Remove(item))
                {
                    _items.Remove(item);
                }
            }
        }

        public bool Contains(string item) => _members.Contains(item);

        public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <param name="rawCounts">raw tag value -> how many tags use it (only a hint for the model); any dictionary will do</param>
    /// <param name="progress">told after every request how many strings are done; may be null</param>
    /// <returns>every key of rawCounts (in its iteration order) -> the artists in it. Blank values name no artist
    /// (empty list); a value the model failed to answer gets one low-confidence artist, itself.</returns>
    /// <exception cref="ArtistAiException">if the service cannot be used or the answer is mostly unusable</exception>
    public static OrderedDictionary<string, List<ParsedArtist>> Parse(AnthropicClient client,
            IEnumerable<KeyValuePair<string, int>> rawCounts, string apiKey, string model, ProgressListener progress)
    {
        return Parse(client, rawCounts, apiKey, model, progress, false, null, null);
    }

    /// <summary>
    /// Same, and then, when checkOnline is set, the proposed names that are uncertain or involve Chinese, Korean or
    /// Japanese text are looked up on the web, so each artist ends up with the name they are officially credited
    /// under. A failed lookup never fails the parse: the names already proposed are kept and notes says so.
    /// </summary>
    /// <param name="notes">receives a sentence for anything the user should know (e.g. the lookup could not be done); may be null</param>
    /// <param name="status">receives a short progress sentence before each request; may be null</param>
    public static OrderedDictionary<string, List<ParsedArtist>> Parse(AnthropicClient client,
            IEnumerable<KeyValuePair<string, int>> rawCounts, string apiKey, string model, ProgressListener progress,
            bool checkOnline, List<string> notes, Action<string> status)
    {
        return Parse(client, rawCounts, apiKey, model, progress, checkOnline, notes, status, null);
    }

    /// <summary>
    /// Same, remembering the answers in cache: a raw value the cache already knows is not sent to Claude and not
    /// looked up on the web (the stored artists are used as they are), and the others are asked about and checked as
    /// usual. With a cache the web check runs chunk by chunk, so that what is paid for is stored and flushed to disk
    /// after each chunk: a run that stops halfway (rate limit, no network) keeps everything it finished. Only final
    /// answers are stored: a value the model skipped, and a value whose web check could not be done, are asked again
    /// next time. The names already in the cache are passed to Claude as "names already used", so a new spelling of a
    /// known artist gets the known name.
    /// </summary>
    /// <param name="cache">where answers are remembered; null for no memory at all (then the web check runs once, at the end)</param>
    public static OrderedDictionary<string, List<ParsedArtist>> Parse(AnthropicClient client,
            IEnumerable<KeyValuePair<string, int>> rawCounts, string apiKey, string model, ProgressListener progress,
            bool checkOnline, List<string> notes, Action<string> status, ArtistParseCache cache)
    {
        OrderedDictionary<string, List<ParsedArtist>> result = ParseNames(client, rawCounts, apiKey, model, progress, status,
                cache == null ? null : new CacheRun(cache, checkOnline, notes));
        if (checkOnline && cache == null)
        {
            try
            {
                CheckOnline(client, result, apiKey, model, status);
            }
            catch (ArtistAiException e)
            {
                if (notes != null)
                {
                    notes.Add(WebCheckFailedNote(e));
                }
            }
        }
        return result;
    }

    private static string WebCheckFailedNote(ArtistAiException e)
    {
        return "The names could not be checked on the web (" + e.Message
                + "); the names Claude proposed were used instead.";
    }

    private static string ResearchFailedNote(ArtistAiException e)
    {
        return "The names that are still unsure could not all be searched for on the web (" + e.Message
                + "); the ones not done stay marked and are searched for again next time.";
    }

    /// <summary>what a run that remembers its answers needs besides the model: where they go, and whether the web check still works</summary>
    private sealed class CacheRun
    {
        public readonly ArtistParseCache Cache;
        public readonly bool CheckOnline;
        public readonly List<string> Notes;
        private bool _webCheckFailed; // after one failure the first check is not tried again in this run
        private bool _researchFailed; // nor is the search for the names that are still unsure (it has its own ways to fail)

        public CacheRun(ArtistParseCache cache, bool checkOnline, List<string> notes)
        {
            Cache = cache;
            CheckOnline = checkOnline;
            Notes = notes;
        }

        /// <summary>
        /// the remembered names that are still unsure and were never searched for (an older run could not): looks them
        /// up on the web, and remembers what was found, also when the search stops halfway (the batches that were done
        /// are kept, the others are searched for next time). Without a key, or after a failed search, nothing is done.
        /// </summary>
        public void Research(AnthropicClient client, string apiKey, string model, Action<string> status,
                OrderedDictionary<string, List<ParsedArtist>> unsure)
        {
            if (!CheckOnline || _researchFailed || apiKey == null || JavaString.IsBlank(apiKey) || unsure.Count == 0)
            {
                return;
            }
            try
            {
                ResearchOnline(client, unsure, apiKey, model, status);
            }
            catch (ArtistAiException e)
            {
                _researchFailed = true;
                if (Notes != null)
                {
                    Notes.Add(ResearchFailedNote(e));
                }
            }
            foreach (KeyValuePair<string, List<ParsedArtist>> entry in unsure)
            {
                Cache.Put(entry.Key, entry.Value);
            }
            Cache.Flush();
        }

        /// <summary>
        /// finishes a chunk: checks its names on the web, remembers the final answers on disk, and returns the
        /// names that later chunks should reuse
        /// </summary>
        public OrderedSet Settle(AnthropicClient client, string apiKey, string model, Action<string> status,
                OrderedDictionary<string, List<ParsedArtist>> grouped, OrderedSet repaired)
        {
            if (CheckOnline && !_webCheckFailed)
            {
                // the stand-ins for values the model skipped, and values that are no name at all, are not looked up (they
                // are not stored either, so the work would be thrown away and done again)
                OrderedDictionary<string, List<ParsedArtist>> toCheck = new OrderedDictionary<string, List<ParsedArtist>>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, List<ParsedArtist>> entry in grouped)
                {
                    if (!repaired.Contains(entry.Key) && entry.Key.Any(char.IsLetterOrDigit))
                    {
                        toCheck[entry.Key] = entry.Value;
                    }
                }
                try
                {
                    VerifyOnline(client, toCheck, apiKey, model, status);
                    if (!_researchFailed)
                    {
                        try
                        {
                            ResearchOnline(client, toCheck, apiKey, model, status);
                        }
                        catch (ArtistAiException e)
                        {
                            // the first check was done: the names it settled are stored, and the others are searched for next time
                            _researchFailed = true;
                            if (Notes != null)
                            {
                                Notes.Add(ResearchFailedNote(e));
                            }
                        }
                    }
                }
                catch (ArtistAiException e)
                {
                    _webCheckFailed = true;
                    if (Notes != null)
                    {
                        Notes.Add(WebCheckFailedNote(e));
                    }
                }
                foreach (KeyValuePair<string, List<ParsedArtist>> entry in toCheck)
                {
                    grouped[entry.Key] = entry.Value; // (the checks rebuild the lists)
                }
            }
            OrderedSet names = new OrderedSet();
            foreach (KeyValuePair<string, List<ParsedArtist>> entry in grouped)
            {
                if (repaired.Contains(entry.Key))
                {
                    continue; // the model skipped it: asking again may do better, and the stand-in is not a name to reuse
                }
                foreach (ParsedArtist artist in entry.Value)
                {
                    names.Add(artist.English);
                }
                bool unverified = _webCheckFailed && entry.Value.Any(NeedsCheck);
                if (!unverified)
                {
                    Cache.Put(entry.Key, entry.Value);
                }
            }
            Cache.Flush();
            return names;
        }
    }

    private static OrderedDictionary<string, List<ParsedArtist>> ParseNames(AnthropicClient client,
            IEnumerable<KeyValuePair<string, int>> rawCounts, string apiKey, string model, ProgressListener progress,
            Action<string> status, CacheRun run)
    {
        // the keys in their order, and how often each is used (a dictionary cannot hold a null key, so none is listed)
        List<string> keys = new List<string>();
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (rawCounts != null)
        {
            foreach (KeyValuePair<string, int> pair in rawCounts)
            {
                if (pair.Key != null && counts.TryAdd(pair.Key, pair.Value))
                {
                    keys.Add(pair.Key);
                }
            }
        }

        Dictionary<string, List<ParsedArtist>> decided = new Dictionary<string, List<ParsedArtist>>(StringComparer.Ordinal);
        List<string> toAsk = new List<string>();
        List<string> remembered = new List<string>();
        foreach (string raw in keys)
        {
            if (JavaString.IsBlank(raw))
            {
                decided[raw] = new List<ParsedArtist>();
                continue;
            }
            List<ParsedArtist> known = run == null ? null : run.Cache.Get(raw);
            if (known == null)
            {
                toAsk.Add(raw);
            }
            else
            {
                decided[raw] = known;
                remembered.Add(raw);
            }
        }
        // most used first: the established spellings are decided first, and the rarer variants in later
        // requests are then told which names to reuse (a stable sort, so ties keep their order)
        toAsk = toAsk.OrderByDescending(raw => CountOf(counts, raw)).ToList();
        remembered = remembered.OrderByDescending(raw => CountOf(counts, raw)).ToList();

        // the names an earlier run left unsure and never searched for: look them up now (and only once)
        if (run != null && remembered.Count > 0)
        {
            OrderedDictionary<string, List<ParsedArtist>> unsure = new OrderedDictionary<string, List<ParsedArtist>>(StringComparer.Ordinal);
            foreach (string raw in remembered)
            {
                if (decided[raw].Any(artist => artist.IsLowConfidence && !artist.IsResearched))
                {
                    unsure[raw] = decided[raw];
                }
            }
            if (unsure.Count > 0)
            {
                run.Research(client, apiKey, model, status, unsure);
                foreach (KeyValuePair<string, List<ParsedArtist>> entry in unsure)
                {
                    decided[entry.Key] = entry.Value;
                }
            }
        }

        OrderedSet knownNames = new OrderedSet();
        foreach (string raw in remembered)
        {
            foreach (ParsedArtist artist in decided[raw])
            {
                knownNames.Add(artist.English);
            }
        }
        if (status != null && toAsk.Count == 0 && remembered.Count > 0)
        {
            status("All " + remembered.Count + " artist names were already known, nothing to ask Claude.");
        }

        int done = 0;
        for (int start = 0; start < toAsk.Count; start += ChunkSize)
        {
            int end = Math.Min(start + ChunkSize, toAsk.Count);
            List<string> chunk = toAsk.GetRange(start, end - start);
            if (status != null)
            {
                status("Asking Claude to group the artist names (" + end
                        + " of " + toAsk.Count + (remembered.Count == 0 ? "" : " new ones, " + remembered.Count
                                + " already known")
                        + ")...");
            }
            JsonObject answer = client.CallTool(apiKey, model, SystemPrompt, UserMessage(chunk, counts, knownNames),
                    ToolName, ToolDescription, ClaudeJson.Parse(Schema).AsObject(), MaxTokens,
                    new AnthropicClient.Options("medium", 0));
            if (run == null)
            {
                foreach (KeyValuePair<string, List<ParsedArtist>> validated in Validate(chunk, answer, knownNames, null))
                {
                    decided[validated.Key] = validated.Value;
                }
            }
            else
            {
                OrderedSet repaired = new OrderedSet();
                OrderedDictionary<string, List<ParsedArtist>> grouped = Validate(chunk, answer, new OrderedSet(), repaired);
                knownNames.AddAll(run.Settle(client, apiKey, model, status, grouped, repaired));
                foreach (KeyValuePair<string, List<ParsedArtist>> validated in grouped)
                {
                    decided[validated.Key] = validated.Value;
                }
            }
            done += chunk.Count;
            if (progress != null)
            {
                progress(done, toAsk.Count);
            }
        }
        if (run != null)
        {
            run.Cache.Flush();
        }

        OrderedDictionary<string, List<ParsedArtist>> result = new OrderedDictionary<string, List<ParsedArtist>>(StringComparer.Ordinal);
        foreach (string raw in keys)
        {
            result[raw] = decided[raw];
        }
        return result;
    }

    // ------------------------------------------------------------------ checking the names on the web

    public const string VerifyToolName = "record_official_names";
    public const int VerifyBatchSize = 25;
    public const int VerifyMaxSearches = 15;

    private const string VerifyDescription = "Record each artist's official name.";

    private const string VerifySchema =
            "{\"type\":\"object\",\"properties\":{\"results\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"n\":{\"type\":\"integer\"},\"official\":{\"type\":\"string\"},\"native\":{\"type\":\"boolean\"}," +
            "\"sure\":{\"type\":\"boolean\"}},\"required\":[\"n\",\"official\",\"native\",\"sure\"]}}},\"required\":[\"results\"]}";

    public const string VerifySystemPrompt =
            "You check the official names of music artists for a personal library. Each request lists numbered artists: " +
            "the name currently proposed, and the spellings found in the library's tags. Use web search to find how " +
            "each artist is officially credited by their own label and on the major streaming services (Apple Music, " +
            "Spotify, QQ Music, NetEase Cloud Music). Search whenever you are not certain; one search can settle several " +
            "artists. Then report by calling the record_official_names tool exactly once, with one result for EVERY " +
            "numbered artist.\n" +
            "\n" + OfficialNameRules;

    /// <summary>the naming rules both web checks follow: how an official name is written, whatever the artist</summary>
    private const string OfficialNameRules = NamingRulesStart + SureAfterFirstCheck + NamingRulesEnd;

    /// <summary>the same rules for the search, where only the web results make a name sure</summary>
    private const string SearchNameRules = NamingRulesStart + SureAfterSearch + NamingRulesEnd;

    private const string NamingRulesStart =
            "For each artist give:\n" +
            "- official: the artist's official name.\n" +
            "  * Korean, Japanese and Western artists: the official Latin-script name with its official stylization " +
            "(aespa, (G)I-DLE, IU, ROSÉ, NewJeans, YOASOBI). Never keep a Korean or Japanese spelling next to it.\n" +
            "  * Chinese-language artists (Mandarin or Cantonese: mainland China, Hong Kong, Taiwan, Singapore, " +
            "Malaysia): the official name in Chinese characters exactly as it is credited in the Chinese market. " +
            "周杰伦 stays 周杰伦, 薛之谦 stays 薛之谦, 周兴哲 stays 周兴哲, and 邓紫棋 is credited \"G.E.M. 邓紫棋\": that " +
            "whole string is her official name, so use it. NEVER turn a Chinese name into pinyin or an English name. " +
            "Where Simplified and Traditional spellings of one name exist, use the script most of the library's " +
            "spellings use.\n" +
            "- native: true for a Chinese-language artist (whose official name stays Chinese), false for everyone else.\n";

    private const string SureAfterFirstCheck =
            "- sure: true only when the web results or your own firm knowledge confirm the official name; otherwise " +
            "false, and give your best guess (usually the proposed name).\n";

    private const string SureAfterSearch =
            "- sure: true only when the search results you read confirm the official name; your own memory never makes " +
            "it true. Otherwise false, and give your best guess (usually the proposed name).\n";

    private const string NamingRulesEnd =
            "\n" +
            "Two numbered artists that are the same artist MUST get exactly the same official name. A group and its " +
            "members are different artists. Never invent a name: if you cannot tell, return the proposed name with " +
            "sure false.";

    // ------------------------------------------------------------------ searching for the names that are still unsure

    public const int ResearchBatchSize = 6;
    public const int ResearchMaxSearches = ResearchBatchSize * 10; // up to ten for each artist of a batch

    /// <summary>
    /// the second look, for the names the first check left unsure: the same naming rules, but every artist must be
    /// searched for on the web (the first check was allowed to search and mostly answered from memory)
    /// </summary>
    public const string ResearchSystemPrompt =
            "You look up the official names of music artists on the web for a personal library. Each request lists " +
            "numbered artists whose names a first check could not settle: the name currently proposed, and the " +
            "spellings found in the library's tags. For EVERY numbered artist you MUST run at least one web search " +
            "before you answer: search the proposed name together with the spellings given (add words such as official, " +
            "Apple Music or Spotify when that helps) and read how the artist is credited by their own label and on the " +
            "major streaming services (Apple Music, Spotify, QQ Music, NetEase Cloud Music). Never answer from memory " +
            "alone. Then report by calling the record_official_names tool exactly once, with one result for EVERY " +
            "numbered artist.\n" +
            "\n" + SearchNameRules;

    /// <summary>artists whose standard name is worth checking: unsure, or involving Chinese, Korean or Japanese text</summary>
    private static bool NeedsCheck(ParsedArtist artist)
    {
        return artist.IsLowConfidence || artist.IsKeepNative || HasCjk(artist.Text) || HasCjk(artist.English);
    }

    /// <summary>Han, Hangul, Hiragana or Katakana anywhere in the text (the scripts Character.UnicodeScript reports)</summary>
    private static bool HasCjk(string text)
    {
        return RomajiScripts.HasCjk(text);
    }

    /// <summary>a name found on the web for one proposed name</summary>
    private sealed record Verdict(string Official, bool KeepNative, bool Sure);

    /// <summary>replaces, in place, the names the web check settled; lists are rebuilt, the map's keys stay</summary>
    private static void VerifyOnline(AnthropicClient client, OrderedDictionary<string, List<ParsedArtist>> parsed,
            string apiKey, string model, Action<string> status)
    {
        // the names worth checking, and then any other proposal that starts like one of them: "G.E.M." next to
        // "G.E.M. 邓紫棋" is probably the same artist, and only one answer for both keeps them together
        OrderedSet checkKeys = new OrderedSet();
        OrderedSet otherKeys = new OrderedSet();
        foreach (List<ParsedArtist> artists in parsed.Values)
        {
            foreach (ParsedArtist artist in artists)
            {
                (NeedsCheck(artist) ? checkKeys : otherKeys).Add(LooseKey(artist.English));
            }
        }
        otherKeys.RemoveAll(checkKeys);
        foreach (string other in otherKeys)
        {
            foreach (string checkedKey in checkKeys.ToList())
            {
                if (Math.Min(other.Length, checkedKey.Length) >= 3
                        && (checkedKey.StartsWith(other, StringComparison.Ordinal)
                                || other.StartsWith(checkedKey, StringComparison.Ordinal)))
                {
                    checkKeys.Add(other);
                    break;
                }
            }
        }

        // one entry per proposed name (by loose key), with every spelling the library uses for it
        OrderedDictionary<string, string> proposedByKey = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        OrderedDictionary<string, OrderedSet> seenByKey = new OrderedDictionary<string, OrderedSet>(StringComparer.Ordinal);
        foreach (List<ParsedArtist> artists in parsed.Values)
        {
            foreach (ParsedArtist artist in artists)
            {
                string key = LooseKey(artist.English);
                if (checkKeys.Contains(key))
                {
                    proposedByKey.TryAdd(key, artist.English);
                    if (!seenByKey.TryGetValue(key, out OrderedSet seen))
                    {
                        seen = new OrderedSet();
                        seenByKey[key] = seen;
                    }
                    seen.Add(artist.Text);
                }
            }
        }
        if (proposedByKey.Count == 0)
        {
            return;
        }
        OrderedSet checkedKeys = checkKeys;

        // alphabetical, so spellings of one artist ("G.E.M." and "G.E.M. 邓紫棋") tend to share a request
        List<string> orderedKeys = new List<string>(proposedByKey.Keys);
        orderedKeys.Sort(StringComparer.Ordinal);
        Dictionary<string, Verdict> verdicts = new Dictionary<string, Verdict>(StringComparer.Ordinal);
        for (int start = 0; start < orderedKeys.Count; start += VerifyBatchSize)
        {
            int end = Math.Min(start + VerifyBatchSize, orderedKeys.Count);
            List<string> batch = orderedKeys.GetRange(start, end - start);
            if (status != null)
            {
                status("Checking official artist names on the web (" + end
                        + " of " + orderedKeys.Count + ")...");
            }
            StringBuilder message = new StringBuilder("Artists to check (JSON, one per line):\n");
            for (int i = 0; i < batch.Count; i++)
            {
                JsonArray seen = new JsonArray();
                foreach (string text in seenByKey[batch[i]].Take(8))
                {
                    seen.Add(JsonValue.Create(text));
                }
                JsonObject entry = new JsonObject();
                entry["n"] = i + 1;
                entry["proposed"] = proposedByKey[batch[i]];
                entry["seen"] = seen;
                message.Append(ClaudeJson.Compact(entry)).Append('\n');
            }
            message.Append("\nCall ").Append(VerifyToolName).Append(" with one result for each of the ")
                    .Append(batch.Count).Append(" artists above.");

            JsonObject answer = client.CallTool(apiKey, model, VerifySystemPrompt, message.ToString(),
                    VerifyToolName, VerifyDescription, ClaudeJson.Parse(VerifySchema).AsObject(),
                    MaxTokens, new AnthropicClient.Options("medium", VerifyMaxSearches));
            ReadVerdicts(answer, batch, verdicts);
        }

        foreach (string raw in parsed.Keys.ToList())
        {
            List<ParsedArtist> updated = new List<ParsedArtist>();
            foreach (ParsedArtist artist in parsed[raw])
            {
                string key = LooseKey(artist.English);
                Verdict verdict = null;
                if (checkedKeys.Contains(key))
                {
                    verdicts.TryGetValue(key, out verdict);
                }
                updated.Add(verdict == null ? artist
                        : new ParsedArtist(artist.Text, verdict.Official, !verdict.Sure, verdict.KeepNative));
            }
            parsed[raw] = updated;
        }
    }

    /// <summary>the usable results of a web check answer, by the loose key of the artist each number stands for</summary>
    /// <param name="answered">when given, receives the artists the answer has an item for, usable or not</param>
    private static void ReadVerdicts(JsonObject answer, List<string> batch, Dictionary<string, Verdict> verdicts,
            ISet<string> answered = null)
    {
        foreach (JsonNode element in AsArray(answer["results"]))
        {
            if (element is not JsonObject item || item["n"] is not JsonValue number)
            {
                continue;
            }
            if (!ClaudeJson.TryGetAsInt(number, out int n) || n < 1 || n > batch.Count)
            {
                continue;
            }
            answered?.Add(batch[n - 1]);
            string official = CleanName(GetString(item, "official"));
            bool keepNative = Flag(item, "native");
            if (official.Length == 0 || (!keepNative && HasCjk(official)))
            {
                continue; // unusable: a Korean/Japanese artist must end up with a Latin-script name
            }
            verdicts.TryAdd(batch[n - 1], new Verdict(official, keepNative, Flag(item, "sure")));
        }
    }

    /// <summary>
    /// The second look at the names the first check left unsure (and that were not searched for yet): each one is looked
    /// up on the web, for real (the answer is asked for again when the model did not search), with the same naming
    /// rules. A name the search settles loses its "please check"; one it cannot settle keeps it, and is marked as
    /// researched either way so it is not searched for again. Only the names that were unsure and not yet searched for
    /// are touched (a sure artist with the same name keeps its name). What a batch found is put in place at once, so a
    /// later batch that fails costs nothing already paid for; a name the answer left out stays as it was, to be
    /// searched for next time, and an answer from memory (no search was made) is applied but never makes a name sure and
    /// does not count as a search. Lists are rebuilt in place, the map's keys stay.
    /// </summary>
    private static void ResearchOnline(AnthropicClient client, OrderedDictionary<string, List<ParsedArtist>> parsed,
            string apiKey, string model, Action<string> status)
    {
        OrderedDictionary<string, string> proposedByKey = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        OrderedDictionary<string, OrderedSet> seenByKey = new OrderedDictionary<string, OrderedSet>(StringComparer.Ordinal);
        foreach (List<ParsedArtist> artists in parsed.Values)
        {
            foreach (ParsedArtist artist in artists)
            {
                if (!artist.IsLowConfidence || artist.IsResearched)
                {
                    continue;
                }
                string key = LooseKey(artist.English);
                proposedByKey.TryAdd(key, artist.English);
                if (!seenByKey.TryGetValue(key, out OrderedSet seen))
                {
                    seen = new OrderedSet();
                    seenByKey[key] = seen;
                }
                seen.Add(artist.Text);
            }
        }
        if (proposedByKey.Count == 0)
        {
            return;
        }

        List<string> orderedKeys = new List<string>(proposedByKey.Keys);
        orderedKeys.Sort(StringComparer.Ordinal);
        for (int start = 0; start < orderedKeys.Count; start += ResearchBatchSize)
        {
            int end = Math.Min(start + ResearchBatchSize, orderedKeys.Count);
            List<string> batch = orderedKeys.GetRange(start, end - start);
            if (status != null)
            {
                status("Searching the web for the names that are still unsure (" + end + " of " + orderedKeys.Count + ")...");
            }
            StringBuilder message = new StringBuilder("Artists to look up (JSON, one per line):\n");
            for (int i = 0; i < batch.Count; i++)
            {
                JsonArray seen = new JsonArray();
                foreach (string text in seenByKey[batch[i]].Take(8))
                {
                    seen.Add(JsonValue.Create(text));
                }
                JsonObject entry = new JsonObject();
                entry["n"] = i + 1;
                entry["proposed"] = proposedByKey[batch[i]];
                entry["seen"] = seen;
                message.Append(ClaudeJson.Compact(entry)).Append('\n');
            }
            message.Append("\nCall ").Append(VerifyToolName).Append(" with one result for each of the ")
                    .Append(batch.Count).Append(" artists above.");

            JsonObject answer = client.CallTool(apiKey, model, ResearchSystemPrompt, message.ToString(),
                    VerifyToolName, VerifyDescription, ClaudeJson.Parse(VerifySchema).AsObject(),
                    MaxTokens, new AnthropicClient.Options("medium", ResearchMaxSearches, true), out bool searched);
            Dictionary<string, Verdict> verdicts = new Dictionary<string, Verdict>(StringComparer.Ordinal);
            HashSet<string> answered = new HashSet<string>(StringComparer.Ordinal);
            ReadVerdicts(answer, batch, verdicts, answered);
            if (answered.Count == 0)
            {
                // an empty or unreadable answer says nothing about these names: nothing is marked, and the run stops here
                throw new ArtistAiException("Unexpected response from the API");
            }
            ApplyResearch(parsed, batch, verdicts, answered, searched);
        }
    }

    /// <summary>puts what one batch of the search found into the names that were looked up (see <see cref="ResearchOnline"/>)</summary>
    private static void ApplyResearch(OrderedDictionary<string, List<ParsedArtist>> parsed, List<string> batch,
            Dictionary<string, Verdict> verdicts, ISet<string> answered, bool searched)
    {
        HashSet<string> keys = new HashSet<string>(batch, StringComparer.Ordinal);
        foreach (string raw in parsed.Keys.ToList())
        {
            List<ParsedArtist> updated = new List<ParsedArtist>();
            foreach (ParsedArtist artist in parsed[raw])
            {
                string key = LooseKey(artist.English);
                if (!artist.IsLowConfidence || artist.IsResearched || !keys.Contains(key))
                {
                    updated.Add(artist);
                }
                else if (verdicts.TryGetValue(key, out Verdict verdict))
                {
                    // a name no search stands behind is never sure, whatever the answer claims
                    updated.Add(new ParsedArtist(artist.Text, verdict.Official, !(verdict.Sure && searched), verdict.KeepNative, searched));
                }
                else if (answered.Contains(key))
                {
                    updated.Add(new ParsedArtist(artist.Text, artist.English, artist.IsLowConfidence, artist.IsKeepNative, searched));
                }
                else
                {
                    updated.Add(artist); // the answer left it out: it is searched for next time
                }
            }
            parsed[raw] = updated;
        }
    }

    /// <summary>the whole web check: the names worth checking, then a real search for the ones that are still unsure</summary>
    private static void CheckOnline(AnthropicClient client, OrderedDictionary<string, List<ParsedArtist>> parsed,
            string apiKey, string model, Action<string> status)
    {
        VerifyOnline(client, parsed, apiKey, model, status);
        ResearchOnline(client, parsed, apiKey, model, status);
    }

    private static int CountOf(Dictionary<string, int> counts, string raw)
    {
        return counts.TryGetValue(raw, out int count) ? count : 0;
    }

    /// <summary>the numbered raw strings with their counts (as JSON, so quotes and escapes are exact), then the names to reuse</summary>
    private static string UserMessage(List<string> chunk, Dictionary<string, int> counts, OrderedSet knownNames)
    {
        StringBuilder message = new StringBuilder();
        message.Append("Raw artist tag values (JSON array; n is only a running number, count is how many tags use ")
                .Append("that exact string):\n[\n");
        for (int i = 0; i < chunk.Count; i++)
        {
            JsonObject entry = new JsonObject();
            entry["n"] = i + 1;
            entry["raw"] = chunk[i];
            entry["count"] = CountOf(counts, chunk[i]);
            message.Append(ClaudeJson.Compact(entry)).Append(i < chunk.Count - 1 ? ",\n" : "\n");
        }
        message.Append("]\n");

        if (knownNames.Count > 0)
        {
            JsonArray names = new JsonArray();
            foreach (string name in knownNames.Take(MaxKnownNames))
            {
                names.Add(JsonValue.Create(name));
            }
            message.Append("\nNames already used - reuse EXACTLY these strings when the same artist appears:\n")
                    .Append(ClaudeJson.Compact(names)).Append('\n');
        }
        message.Append("\nCall ").Append(ToolName).Append(" with one result for each of the ").Append(chunk.Count)
                .Append(" strings above.");
        return message.ToString();
    }

    /// <summary>
    /// matches the model's results back to the chunk's strings and repairs what is wrong with them.
    /// Names the model chose are added to knownNames for the next request. The strings the model skipped (and that
    /// got a stand-in entry) are added to repairedRaws, if given.
    /// </summary>
    private static OrderedDictionary<string, List<ParsedArtist>> Validate(List<string> chunk, JsonObject answer,
            OrderedSet knownNames, OrderedSet repairedRaws)
    {
        // a model can reformat a raw value a little (trim, case), so fall back to a loose match
        Dictionary<string, JsonObject> exact = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        Dictionary<string, JsonObject> loose = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (JsonNode element in AsArray(answer["results"]))
        {
            if (element is JsonObject item && GetString(item, "raw") != null)
            {
                exact.TryAdd(GetString(item, "raw"), item);
                loose.TryAdd(LooseKey(GetString(item, "raw")), item);
            }
        }

        OrderedDictionary<string, List<ParsedArtist>> validated = new OrderedDictionary<string, List<ParsedArtist>>(StringComparer.Ordinal);
        OrderedSet names = new OrderedSet();
        int repaired = 0;
        foreach (string raw in chunk)
        {
            exact.TryGetValue(raw, out JsonObject item);
            if (item == null)
            {
                loose.TryGetValue(LooseKey(raw), out item);
            }
            List<ParsedArtist> artists = item == null ? new List<ParsedArtist>() : CleanArtists(raw, item["artists"], names);
            if (artists.Count == 0)
            {
                // keep the tag value as its own artist rather than lose it; the flag makes the user look at it
                artists.Add(new ParsedArtist(raw, CleanName(raw), true));
                repaired++;
                if (repairedRaws != null)
                {
                    repairedRaws.Add(raw);
                }
            }
            validated[raw] = artists;
        }

        if (repaired * 3 > chunk.Count)
        {
            throw new ArtistAiException("The AI answer was incomplete");
        }
        knownNames.AddAll(names);
        return validated;
    }

    private static List<ParsedArtist> CleanArtists(string raw, JsonNode artistsElement, OrderedSet names)
    {
        List<JsonObject> named = new List<JsonObject>();
        foreach (JsonNode element in AsArray(artistsElement))
        {
            if (element is JsonObject artist
                    && !(IsBlank(GetString(artist, "text")) && CleanName(GetString(artist, "english")).Length == 0))
            {
                named.Add(artist);
            }
        }

        List<ParsedArtist> artists = new List<ParsedArtist>();
        foreach (JsonObject artist in named)
        {
            string english = CleanName(GetString(artist, "english"));
            bool lowConfidence = Flag(artist, "lowConfidence");
            bool keepNative = Flag(artist, "native");

            string text = Locate(raw, GetString(artist, "text"));
            if (text == null)
            {
                // the model paraphrased instead of copying: a lone artist owns the whole raw value,
                // otherwise the best handle left is the name itself
                text = named.Count == 1 ? raw : english.Length == 0 ? CleanName(GetString(artist, "text")) : english;
                lowConfidence = true;
            }
            if (english.Length == 0)
            {
                english = CleanName(text);
                lowConfidence = true;
            }
            else
            {
                names.Add(english);
            }
            artists.Add(new ParsedArtist(text, english, lowConfidence, keepNative));
        }
        return artists;
    }

    /// <summary>text as it is written in raw (ignoring stray outer whitespace the model added), or null if raw doesn't contain it</summary>
    private static string Locate(string raw, string text)
    {
        if (IsBlank(text))
        {
            return null;
        }
        if (raw.Contains(text, StringComparison.Ordinal))
        {
            return text;
        }
        string stripped = JavaString.Strip(text);
        return raw.Contains(stripped, StringComparison.Ordinal) ? stripped : null;
    }

    /// <summary>one canonical spelling for names: composed Unicode, single spaces, no outer whitespace</summary>
    private static string CleanName(string name)
    {
        if (name == null)
        {
            return "";
        }
        return JavaString.Strip(Whitespace.Replace(Normalize(name), " "));
    }

    /// <summary>Normalizer.normalize(NFC) leaves unpaired surrogates as they are; string.Normalize throws on them</summary>
    private static string Normalize(string name)
    {
        try
        {
            return name.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return name;
        }
    }

    private static string LooseKey(string raw)
    {
        return CleanName(raw).ToLowerInvariant();
    }

    private static bool IsBlank(string text)
    {
        return text == null || JavaString.IsBlank(text);
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

    private static string GetString(JsonObject jsonObject, string key)
    {
        return ClaudeJson.GetString(jsonObject, key);
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
}
