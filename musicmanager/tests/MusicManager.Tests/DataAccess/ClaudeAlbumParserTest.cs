using System.Text.Json.Nodes;
using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;
using static MusicManager.Tests.DataAccess.ClaudeTestSupport;

namespace MusicManager.Tests.DataAccess;

/// <summary>Not a port: asking Claude which album each raw value is, with a stand-in for the API that plays the model.</summary>
public class ClaudeAlbumParserTest : IDisposable
{
    private const string Key = "sk-ant-api03-TEST-KEY-abcdefghijklmnop";
    private const string Model = "claude-test-model";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public ClaudeAlbumParserTest()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // a temp folder left behind is harmless
        }
    }

    /// <summary>a stand-in for the API: records each request body and lets a function play the model</summary>
    private sealed class FakeModel
    {
        public readonly List<JsonObject> Requests = new List<JsonObject>();
        private readonly Func<JsonObject, JsonObject> _brain;

        public FakeModel(Func<JsonObject, JsonObject> brain)
        {
            _brain = brain;
        }

        public AnthropicClient Client()
        {
            return new AnthropicClient(Post, millis =>
            {
            });
        }

        private AnthropicClient.Response Post(string url, IDictionary<string, string> headers, string body, TimeSpan timeout)
        {
            JsonObject request = Obj(body);
            Requests.Add(request);
            return ToolResponse(ClaudeAlbumParser.ToolName, _brain(request));
        }
    }

    private static string UserMessage(JsonObject request)
    {
        return request.Arr("messages")[0].Str("content");
    }

    private static List<JsonObject> EntriesOf(JsonObject request)
    {
        var entries = new List<JsonObject>();
        foreach (string line in UserMessage(request).Split('\n'))
        {
            if (line.StartsWith("{\"n\":", StringComparison.Ordinal))
            {
                entries.Add(Obj(line));
            }
        }
        return entries;
    }

    private static List<string> KnownNamesOf(JsonObject request)
    {
        string[] lines = UserMessage(request).Split('\n');
        var names = new List<string>();
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].StartsWith("Names already used - reuse EXACTLY these strings when the same album appears", StringComparison.Ordinal))
            {
                foreach (JsonNode name in JsonNode.Parse(lines[i + 1]).AsArray())
                {
                    names.Add(name.GetValue<string>());
                }
            }
        }
        return names;
    }

    private static JsonObject Result(int n, string album, bool? lowConfidence = null)
    {
        var result = new JsonObject();
        result["n"] = n;
        result["album"] = album;
        if (lowConfidence != null)
        {
            result["lowConfidence"] = lowConfidence.Value;
        }
        return result;
    }

    private static JsonObject Answer(params JsonObject[] results)
    {
        var array = new JsonArray();
        foreach (JsonObject result in results)
        {
            array.Add(result);
        }
        var answer = new JsonObject();
        answer["results"] = array;
        return answer;
    }

    private static OrderedDictionary<string, AlbumRaw> Raws(params (string Raw, int Count, bool FromFolder)[] values)
    {
        var raws = new OrderedDictionary<string, AlbumRaw>(StringComparer.Ordinal);
        foreach ((string raw, int count, bool fromFolder) in values)
        {
            raws[raw] = new AlbumRaw(count, fromFolder, new List<string> { "IU" }, new List<string> { raw });
        }
        return raws;
    }

    private OrderedDictionary<string, ParsedAlbum> Parse(FakeModel model, IDictionary<string, AlbumRaw> raws, AlbumParseCache cache = null,
            List<string> notes = null, Action<string> status = null)
    {
        return ClaudeAlbumParser.Parse(model.Client(), raws, Key, Model, notes ?? new List<string>(), status, cache);
    }

    // ---- the request

    [Fact]
    public void EveryValueIsSentNumberedWithItsContext()
    {
        var model = new FakeModel(request => Answer(Result(1, "Palette"), Result(2, "Lilac")));
        var raws = new OrderedDictionary<string, AlbumRaw>(StringComparer.Ordinal);
        raws["Palette [Hi-Res 24bit-96khz]"] = new AlbumRaw(10, false, new List<string> { "IU" }, new List<string> { "Palette [Hi-Res 24bit-96khz]" });
        raws["IU - Lilac (2021)"] = new AlbumRaw(2, true, new List<string>(), new List<string> { "IU - Lilac (2021)" });

        Parse(model, raws);

        JsonObject request = Assert.Single(model.Requests);
        Assert.Equal(Model, request.Str("model"));
        Assert.Contains("record_albums", request.ToJsonString());
        List<JsonObject> entries = EntriesOf(request);
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Int("n"));
        Assert.Equal("Palette [Hi-Res 24bit-96khz]", entries[0].Str("raw"));
        Assert.Equal(10, entries[0].Int("count"));
        Assert.Equal("tag", entries[0].Str("source"));
        Assert.Equal("IU", entries[0].Arr("artists")[0].GetValue<string>());
        Assert.Equal("folder", entries[1].Str("source"));
        Assert.Empty(entries[0].Arr("titles")); // (this raw value was made without any)
        Assert.Equal("IU - Lilac (2021)", entries[1].Arr("folders")[0].GetValue<string>());
        Assert.Contains("Group by the album's NAME, never by its artist", request.SystemText());
    }

    [Fact]
    public void TheTitlesOfTheSongsAreSentSoTheModelCanTellWhatTheyAreReallyOn()
    {
        var model = new FakeModel(request => Answer(Result(1, "SOUR", true)));
        var raws = new OrderedDictionary<string, AlbumRaw>(StringComparer.Ordinal);
        raws["Bittersweet Indie (Explicit)"] = new AlbumRaw(1, false, new List<string> { "Olivia Rodrigo" }, new List<string> { "New songs" },
                new List<string> { "deja vu (Explicit)" });

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, raws);

        JsonObject entry = EntriesOf(Assert.Single(model.Requests))[0];
        Assert.Equal("deja vu (Explicit)", entry.Arr("titles")[0].GetValue<string>());
        Assert.Contains("playlist or a compilation", Assert.Single(model.Requests).SystemText());
        Assert.Equal("SOUR", parsed["Bittersweet Indie (Explicit)"].Album);
        Assert.True(parsed["Bittersweet Indie (Explicit)"].IsLowConfidence);
    }

    [Fact]
    public void TheModelMaySearchTheWebForTitlesItIsUnsureOf()
    {
        var model = new FakeModel(request => Answer(Result(1, "Palette")));

        Parse(model, Raws(("Palette", 1, false)));

        Assert.Contains("web_search", Assert.Single(model.Requests).ToJsonString());
    }

    [Fact]
    public void ValuesWithQuotesAndNonLatinTextSurviveTheTrip()
    {
        string raw = "光年之外 (電影 \"Passengers\")";
        var model = new FakeModel(request => Answer(Result(1, "光年之外")));

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, Raws((raw, 1, false)));

        Assert.Equal(raw, EntriesOf(model.Requests[0])[0].Str("raw"));
        Assert.Equal("光年之外", parsed[raw].Album);
    }

    // ---- the answers

    [Fact]
    public void TheAnswersAreMatchedBackByNumberInAnyOrder()
    {
        var model = new FakeModel(request => Answer(Result(2, "Lilac", true), Result(1, "Palette", false)));

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, Raws(("a", 5, false), ("b", 1, false)));

        Assert.Equal(new[] { "a", "b" }, parsed.Keys.ToArray());
        Assert.Equal("Palette", parsed["a"].Album);
        Assert.False(parsed["a"].IsLowConfidence);
        Assert.Equal("Lilac", parsed["b"].Album);
        Assert.True(parsed["b"].IsLowConfidence);
    }

    [Fact]
    public void ABlankAlbumMeansTheValueIsNoAlbumName()
    {
        var model = new FakeModel(request => Answer(Result(1, ""), Result(2, "Real")));

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, Raws(("Unknown", 1, false), ("Real", 1, false)));

        Assert.True(parsed["Unknown"].IsNotAnAlbum);
        Assert.False(parsed["Real"].IsNotAnAlbum);
    }

    [Fact]
    public void NamesAreTidiedAndNumbersThatAreNotInTheRequestAreIgnored()
    {
        var model = new FakeModel(request => Answer(Result(1, "  Love  poem  "), Result(2, "B"), Result(3, "C"), Result(99, "Ghost"), Result(0, "Ghost")));

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, Raws(("a", 1, false), ("b", 1, false), ("c", 1, false), ("d", 1, false)));

        Assert.Equal("Love poem", parsed["a"].Album);
        Assert.Equal("d", parsed["d"].Album); // nobody answered number 4: the value stays as it was, marked "please check"
        Assert.True(parsed["d"].IsLowConfidence);
        Assert.DoesNotContain(parsed.Values, album => album.Album == "Ghost");
    }

    [Fact]
    public void AValueTheModelSkippedKeepsItsOwnTextAndIsMarkedPleaseCheck()
    {
        var model = new FakeModel(request => Answer(Result(1, "A"), Result(2, "B"), Result(3, "C"), Result(4, "D"), Result(5, "E")));
        var raws = Raws(("a", 1, false), ("b", 1, false), ("c", 1, false), ("d", 1, false), ("e", 1, false), ("Skipped [FLAC]", 1, false));

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, raws);

        Assert.Equal("Skipped", parsed["Skipped [FLAC]"].Album);
        Assert.True(parsed["Skipped [FLAC]"].IsLowConfidence);
        Assert.False(parsed["a"].IsLowConfidence);
    }

    [Fact]
    public void AMostlyUnusableAnswerFails()
    {
        var model = new FakeModel(request => Answer(Result(1, "A")));

        ArtistAiException failure = Assert.Throws<ArtistAiException>(() => Parse(model, Raws(("a", 1, false), ("b", 1, false), ("c", 1, false))));

        Assert.Contains("incomplete", failure.Message);
    }

    [Fact]
    public void AnAnswerSentAsAJsonStringIsRead()
    {
        var model = new FakeModel(request =>
        {
            var answer = new JsonObject();
            answer["results"] = Answer(Result(1, "Palette")).Arr("results").ToJsonString();
            return answer;
        });

        Assert.Equal("Palette", Parse(model, Raws(("a", 1, false)))["a"].Album);
    }

    // ---- what is remembered and what is reused

    [Fact]
    public void AValueTheCacheKnowsIsNotAskedAgainAndTheNewOnesAreRemembered()
    {
        var cache = new AlbumParseCache(Path.Combine(_dir, "cache.json"));
        cache.Put("Palette [Hi-Res 24bit-96khz]", new ParsedAlbum("Palette", false));
        var model = new FakeModel(request => Answer(Result(1, "Lilac")));
        var told = new List<string>();

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, Raws(("Palette [Hi-Res 24bit-96khz]", 3, false), ("LILAC [24bit Hi-Res]", 2, false)), cache, status: told.Add);

        List<JsonObject> entries = EntriesOf(Assert.Single(model.Requests));
        Assert.Equal("LILAC [24bit Hi-Res]", Assert.Single(entries).Str("raw"));
        Assert.Equal("Palette", parsed["Palette [Hi-Res 24bit-96khz]"].Album);
        Assert.Equal("Lilac", parsed["LILAC [24bit Hi-Res]"].Album);
        Assert.Equal("Lilac", new AlbumParseCache(Path.Combine(_dir, "cache.json")).Get("LILAC [24bit Hi-Res]").Album); // flushed to disk
        Assert.Contains(told, s => s.Contains("1 new ones, 1 already known"));
    }

    [Fact]
    public void WhenEverythingIsKnownNoKeyAndNoRequestIsNeeded()
    {
        var cache = new AlbumParseCache(Path.Combine(_dir, "cache.json"));
        cache.Put("a", new ParsedAlbum("A", false));
        var model = new FakeModel(request => throw new InvalidOperationException("must not be asked"));
        var told = new List<string>();

        OrderedDictionary<string, ParsedAlbum> parsed = ClaudeAlbumParser.Parse(model.Client(), Raws(("a", 1, false)), null, Model,
                new List<string>(), told.Add, cache);

        Assert.Empty(model.Requests);
        Assert.Equal("A", parsed["a"].Album);
        Assert.Contains(told, s => s.StartsWith("All 1 album names were already known", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAKeyAnUnknownValueCannotBeAsked()
    {
        var model = new FakeModel(request => Answer(Result(1, "A")));

        Assert.Throws<ArtistAiException>(() => ClaudeAlbumParser.Parse(model.Client(), Raws(("a", 1, false)), null, Model,
                new List<string>(), null, null));
    }

    [Fact]
    public void TheNamesAlreadyUsedAreToldToTheModelSoASpellingGetsTheKnownName()
    {
        var cache = new AlbumParseCache(Path.Combine(_dir, "cache.json"));
        cache.Put("Palette [Hi-Res 24bit-96khz]", new ParsedAlbum("Palette", false));
        cache.Put("Chat", new ParsedAlbum("CHAT-SHIRE", false));
        var model = new FakeModel(request => Answer(Result(1, "Palette")));

        Parse(model, Raws(("PALETTE [FLAC]", 1, false)), cache);

        Assert.Equal(new[] { "Palette", "CHAT-SHIRE" }, KnownNamesOf(model.Requests[0]).ToArray());
    }

    [Fact]
    public void AValueTheModelSkippedIsNotRemembered()
    {
        var cache = new AlbumParseCache(Path.Combine(_dir, "cache.json"));
        var model = new FakeModel(request => Answer(Result(1, "A"), Result(2, "B"), Result(3, "C"), Result(4, "D"), Result(5, "E")));

        Parse(model, Raws(("a", 1, false), ("b", 1, false), ("c", 1, false), ("d", 1, false), ("e", 1, false), ("skipped", 1, false)), cache);

        Assert.Equal(5, cache.Size());
        Assert.Null(cache.Get("skipped"));
    }

    [Fact]
    public void ManyValuesAreAskedAboutInChunksAndLaterChunksAreToldTheEarlierNames()
    {
        var raws = new OrderedDictionary<string, AlbumRaw>(StringComparer.Ordinal);
        for (int i = 0; i < ClaudeAlbumParser.ChunkSize + 5; i++)
        {
            raws["album " + i] = new AlbumRaw(1, false, new List<string>(), new List<string>());
        }
        var model = new FakeModel(request =>
        {
            var results = new List<JsonObject>();
            foreach (JsonObject entry in EntriesOf(request))
            {
                results.Add(Result(entry.Int("n"), "Name of " + entry.Str("raw")));
            }
            return Answer(results.ToArray());
        });

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, raws);

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(ClaudeAlbumParser.ChunkSize, EntriesOf(model.Requests[0]).Count);
        Assert.Equal(5, EntriesOf(model.Requests[1]).Count);
        Assert.Equal(ClaudeAlbumParser.ChunkSize, KnownNamesOf(model.Requests[1]).Count);
        Assert.Equal("Name of album 3", parsed["album 3"].Album);
        Assert.Equal(raws.Count, parsed.Count);
    }

    [Fact]
    public void ABlankRawValueIsNoAlbumWithoutAsking()
    {
        var model = new FakeModel(request => Answer());

        OrderedDictionary<string, ParsedAlbum> parsed = Parse(model, Raws(("  ", 1, false)));

        Assert.Empty(model.Requests);
        Assert.True(parsed["  "].IsNotAnAlbum);
    }
}
