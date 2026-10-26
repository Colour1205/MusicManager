using System.Text;
using System.Text.Json.Nodes;
using MusicManager.Common;
using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;
using static MusicManager.Tests.DataAccess.ClaudeTestSupport;

namespace MusicManager.Tests.DataAccess;

public class ClaudeArtistParserTest : IDisposable
{
    private const string Key = "sk-ant-api03-TEST-KEY-abcdefghijklmnop";
    private const string Model = "claude-test-model";

    private readonly string _dir;

    public ClaudeArtistParserTest()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
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

        public AnthropicClient.Response Post(string url, IDictionary<string, string> headers, string body, TimeSpan timeout)
        {
            JsonObject request = Obj(body);
            Requests.Add(request);
            return ToolResponse(ClaudeArtistParser.ToolName, _brain(request));
        }
    }

    // ---- reading the requests the parser sent ----

    private static string UserMessage(JsonObject request)
    {
        return request.Arr("messages")[0].Str("content");
    }

    private static List<JsonObject> EntriesOf(JsonObject request)
    {
        List<JsonObject> entries = new List<JsonObject>();
        foreach (string line in UserMessage(request).Split('\n'))
        {
            if (line.StartsWith("{\"n\":", StringComparison.Ordinal))
            {
                entries.Add(Obj(line.EndsWith(",", StringComparison.Ordinal) ? line.Substring(0, line.Length - 1) : line));
            }
        }
        return entries;
    }

    private static List<string> RawsOf(JsonObject request)
    {
        return EntriesOf(request).Select(e => e.Str("raw")).ToList();
    }

    private static List<string> KnownNamesOf(JsonObject request)
    {
        string[] lines = UserMessage(request).Split('\n');
        List<string> names = new List<string>();
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].StartsWith("Names already used - reuse EXACTLY these strings when the same artist appears", StringComparison.Ordinal))
            {
                foreach (JsonNode name in JsonNode.Parse(lines[i + 1]).AsArray())
                {
                    names.Add(name.GetValue<string>());
                }
            }
        }
        return names;
    }

    // ---- playing the model ----

    private static JsonObject Artist(string text, string english)
    {
        JsonObject artist = new JsonObject();
        artist["text"] = text;
        artist["english"] = english;
        return artist;
    }

    private static JsonObject Artist(string text, string english, bool lowConfidence)
    {
        JsonObject artist = Artist(text, english);
        artist["lowConfidence"] = lowConfidence;
        return artist;
    }

    private static JsonObject Result(string raw, params JsonObject[] artists)
    {
        JsonObject result = new JsonObject();
        result["raw"] = raw;
        JsonArray array = new JsonArray();
        foreach (JsonObject artist in artists)
        {
            array.Add(artist);
        }
        result["artists"] = array;
        return result;
    }

    private static JsonObject Answer(params JsonObject[] results)
    {
        JsonArray array = new JsonArray();
        foreach (JsonObject result in results)
        {
            array.Add(result);
        }
        JsonObject answer = new JsonObject();
        answer["results"] = array;
        return answer;
    }

    /// <summary>a model that splits every raw value on "/" and names each piece "E-piece"</summary>
    private static JsonObject SplitOnSlash(JsonObject request)
    {
        List<JsonObject> results = new List<JsonObject>();
        foreach (string raw in RawsOf(request))
        {
            List<JsonObject> artists = new List<JsonObject>();
            foreach (string piece in JavaString.Split(raw, "/"))
            {
                artists.Add(Artist(piece, "E-" + JavaString.Strip(piece)));
            }
            results.Add(Result(raw, artists.ToArray()));
        }
        return Answer(results.ToArray());
    }

    private static OrderedDictionary<string, int> Counts(params string[] raws)
    {
        OrderedDictionary<string, int> counts = new OrderedDictionary<string, int>();
        for (int i = 0; i < raws.Length; i++)
        {
            counts[raws[i]] = 1;
        }
        return counts;
    }

    private static OrderedDictionary<string, int> Numbered(int n)
    {
        OrderedDictionary<string, int> counts = new OrderedDictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            counts["artist " + i] = 1;
        }
        return counts;
    }

    private static OrderedDictionary<string, List<ParsedArtist>> Parse(FakeModel model,
            IEnumerable<KeyValuePair<string, int>> rawCounts)
    {
        return ClaudeArtistParser.Parse(model.Client(), rawCounts, Key, Model, null);
    }

    private static void AssertArtist(string text, string english, bool low, ParsedArtist actual)
    {
        Assert.Equal(text, actual.Text);
        Assert.Equal(english, actual.English);
        Assert.Equal(low, actual.IsLowConfidence);
    }

    // ---- the request ----

    [Fact]
    public void EmptyInputMakesNoRequest()
    {
        FakeModel model = new FakeModel(SplitOnSlash);
        List<int[]> progress = new List<int[]>();

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(model.Client(),
                new Dictionary<string, int>(), Key, Model, (done, total) => progress.Add(new int[] { done, total }));

        Assert.Empty(result);
        Assert.Empty(model.Requests);
        Assert.Empty(progress);
    }

    [Fact]
    public void NullInputMakesNoRequestEither()
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        Assert.Empty(ClaudeArtistParser.Parse(model.Client(), null, Key, Model, null));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void RequestCarriesTheToolDefinitionAndTheNumberedStringsWithCounts()
    {
        FakeModel model = new FakeModel(SplitOnSlash);
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts["IU"] = 214;
        rawCounts["HUNTR/X; EJAE"] = 15;
        rawCounts["NewJeans  "] = 1;

        Parse(model, rawCounts);

        Assert.Single(model.Requests);
        JsonObject request = model.Requests[0];
        Assert.Equal(Model, request.Str("model"));
        Assert.Equal(16000, request.Int("max_tokens"));
        Assert.Equal(ClaudeArtistParser.SystemPrompt, request.SystemText());
        Assert.False(request.ContainsKey("temperature"));

        JsonObject tool = request.Arr("tools")[0].AsObject();
        Assert.Equal("record_artists", tool.Str("name"));
        Assert.Equal("auto", request.Child("tool_choice").Str("type"));

        JsonObject expectedSchema = Obj("""
                {"type":"object","properties":{"results":{"type":"array","items":{"type":"object","properties":{
                  "raw":{"type":"string"},
                  "artists":{"type":"array","items":{"type":"object","properties":{
                    "text":{"type":"string"},"english":{"type":"string"},"lowConfidence":{"type":"boolean"},
                    "native":{"type":"boolean"}},
                    "required":["text","english"]}}},
                  "required":["raw","artists"]}}},"required":["results"]}
                """);
        AssertJsonEqual(expectedSchema, tool["input_schema"]);

        List<JsonObject> entries = EntriesOf(request);
        Assert.Equal(3, entries.Count);
        // most used first, exact strings (trailing spaces included), running numbers, counts
        AssertJsonEqual(Obj("{\"n\":1,\"raw\":\"IU\",\"count\":214}"), entries[0]);
        AssertJsonEqual(Obj("{\"n\":2,\"raw\":\"HUNTR/X; EJAE\",\"count\":15}"), entries[1]);
        AssertJsonEqual(Obj("{\"n\":3,\"raw\":\"NewJeans  \",\"count\":1}"), entries[2]);
        Assert.Empty(KnownNamesOf(request));
        Assert.False(UserMessage(request).Contains("Names already used"));
    }

    [Fact]
    public void SystemPromptKeepsEveryRuleTheFeatureDependsOn()
    {
        string prompt = ClaudeArtistParser.SystemPrompt;

        // separators and names that merely contain separators
        foreach (string needle in new string[] { "K/DA", "HUNTR/X", "AC/DC", "Tyler, The Creator", "Earth, Wind & Fire",
                "(G)I-DLE", "G.E.M.", "S.H.E", "feat.", "ft.", "、", "，", "␟" })
        {
            Assert.True(prompt.Contains(needle), "missing: " + needle);
        }
        // the shape of the answer
        foreach (string needle in new string[] { "text", "english", "lowConfidence", "record_artists", "EVERY",
                "exact substring", "character for character", "parenthesised" })
        {
            Assert.True(prompt.Contains(needle), "missing: " + needle);
        }
        // naming rules
        foreach (string needle in new string[] { "aespa", "IU", "ROSÉ", "NewJeans",
                "BABYMONSTER", "Revised Romanization", "Hepburn", "MINNIE", "DIFFERENT artists",
                "byte-identical", "Celing Dion", "Celine Dion", "Various Artists", "V.A.", "群星", "华语群星", "[重复]",
                "Names already used" })
        {
            Assert.True(prompt.Contains(needle), "missing: " + needle);
        }
        // Chinese-language artists keep their Chinese name, and say so with the native flag
        foreach (string needle in new string[] { "native", "KEEP THEIR CHINESE NAME", "G.E.M. 邓紫棋", "周杰伦",
                "Never turn such a name into pinyin" })
        {
            Assert.True(prompt.Contains(needle), "missing: " + needle);
        }
        Assert.True(prompt.Length < 9000, "the prompt is sent with every chunk, keep it compact");
    }

    // ---- the Chinese-name rule and the web check ----

    /// <summary>answers the grouping request with fixed artists, and a web check with whatever the brain says</summary>
    private sealed class TwoStepModel
    {
        public readonly List<JsonObject> Requests = new List<JsonObject>();
        private readonly Func<JsonObject, JsonObject> _grouping;
        private readonly Func<JsonObject, JsonObject> _checking;

        public TwoStepModel(Func<JsonObject, JsonObject> grouping, Func<JsonObject, JsonObject> checking)
        {
            _grouping = grouping;
            _checking = checking;
        }

        public AnthropicClient Client()
        {
            return new AnthropicClient(Post, millis =>
            {
            });
        }

        public AnthropicClient.Response Post(string url, IDictionary<string, string> headers, string body, TimeSpan timeout)
        {
            JsonObject request = Obj(body);
            Requests.Add(request);
            string tool = request.Arr("tools")[0].Str("name");
            bool checkingNames = tool == ClaudeArtistParser.VerifyToolName;
            return ToolResponse(tool, (checkingNames ? _checking : _grouping)(request));
        }
    }

    private static JsonObject NativeArtist(string text, string english)
    {
        JsonObject artist = Artist(text, english, false);
        artist["native"] = true;
        return artist;
    }

    /// <summary>the numbered artists of a web check request</summary>
    private static List<JsonObject> CheckedArtists(JsonObject request)
    {
        List<JsonObject> artists = new List<JsonObject>();
        foreach (string line in UserMessage(request).Split('\n'))
        {
            if (line.StartsWith("{\"n\":", StringComparison.Ordinal))
            {
                artists.Add(Obj(line));
            }
        }
        return artists;
    }

    private static JsonObject Verdicts(JsonObject request, string official, bool nativeName, bool sure)
    {
        JsonArray results = new JsonArray();
        foreach (JsonObject artist in CheckedArtists(request))
        {
            JsonObject item = new JsonObject();
            item["n"] = artist.Int("n");
            item["official"] = official;
            item["native"] = nativeName;
            item["sure"] = sure;
            results.Add(item);
        }
        JsonObject answer = new JsonObject();
        answer["results"] = results;
        return answer;
    }

    [Fact]
    public void TheNativeFlagOfAChineseArtistIsKept()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("G.E.M. 邓紫棋", NativeArtist("G.E.M. 邓紫棋", "G.E.M. 邓紫棋")),
                Result("Dua Lipa", Artist("Dua Lipa", "Dua Lipa", false))));

        OrderedDictionary<string, List<ParsedArtist>> parsed = Parse(model, Counts("G.E.M. 邓紫棋", "Dua Lipa"));

        Assert.True(parsed["G.E.M. 邓紫棋"][0].IsKeepNative);
        Assert.False(parsed["Dua Lipa"][0].IsKeepNative);
    }

    [Fact]
    public void TheWebCheckGivesEveryProposedNameOfOneChineseArtistTheOfficialCredit()
    {
        TwoStepModel model = new TwoStepModel(
                request => Answer(Result("G.E.M.", Artist("G.E.M.", "G.E.M.", false)),
                        Result("G.E.M. 邓紫棋", Artist("G.E.M. 邓紫棋", "G.E.M. 邓紫棋", false))),
                request => Verdicts(request, "G.E.M. 邓紫棋", true, true));
        List<string> notes = new List<string>();

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(),
                Counts("G.E.M.", "G.E.M. 邓紫棋"), Key, Model, null, true, notes, null);

        foreach (string raw in new List<string> { "G.E.M.", "G.E.M. 邓紫棋" })
        {
            ParsedArtist artist = parsed[raw][0];
            Assert.Equal("G.E.M. 邓紫棋", artist.English);
            Assert.True(artist.IsKeepNative);
            Assert.False(artist.IsLowConfidence, "the web check was sure");
        }
        Assert.True(parsed["G.E.M."][0].Text == "G.E.M.", "what the tag says is not touched");
        Assert.Empty(notes);

        // the check is a separate request that may search the web, and it was told what the library calls the artist
        JsonObject check = model.Requests[1];
        JsonArray tools = check.Arr("tools");
        Assert.Equal(2, tools.Count);
        Assert.Equal("web_search", tools[1].Str("name"));
        Assert.Equal(ClaudeArtistParser.VerifyMaxSearches, tools[1].Int("max_uses"));
        Assert.Equal(ClaudeArtistParser.VerifySystemPrompt, check.SystemText());
        Assert.True(UserMessage(check).Contains("G.E.M. 邓紫棋"));
    }

    [Fact]
    public void TheWebCheckSkipsNamesThatAreSureAndPlainLatin()
    {
        TwoStepModel model = new TwoStepModel(
                request => Answer(Result("Dua Lipa", Artist("Dua Lipa", "Dua Lipa", false)),
                        Result("에스파 (aespa)", Artist("에스파 (aespa)", "aespa", false))),
                request => Verdicts(request, "aespa", false, true));

        ClaudeArtistParser.Parse(model.Client(), Counts("Dua Lipa", "에스파 (aespa)"), Key, Model, null, true,
                new List<string>(), null);

        Assert.Equal(2, model.Requests.Count);
        string asked = UserMessage(model.Requests[1]);
        Assert.True(asked.Contains("aespa"), "a name written with Korean text is checked");
        Assert.False(asked.Contains("Dua Lipa"), "a sure, Latin-only name is not worth a search");
    }

    [Fact]
    public void AKoreanOrJapaneseArtistCannotBeGivenANonLatinNameByTheCheck()
    {
        TwoStepModel model = new TwoStepModel(
                request => Answer(Result("에스파 (aespa)", Artist("에스파 (aespa)", "aespa", true))),
                request => Verdicts(request, "에스파", false, true));

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("에스파 (aespa)"),
                Key, Model, null, true, new List<string>(), null);

        Assert.Equal("aespa", parsed["에스파 (aespa)"][0].English); // the proposed name is kept
    }

    [Fact]
    public void AnUnsureCheckKeepsTheArtistFlaggedForTheUser()
    {
        TwoStepModel model = new TwoStepModel(
                request => Answer(Result("张一铎", NativeArtist("张一铎", "张一铎"))),
                request => Verdicts(request, "张一铎", true, false));

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("张一铎"), Key, Model,
                null, true, new List<string>(), null);

        Assert.True(parsed["张一铎"][0].IsLowConfidence);
    }

    [Fact]
    public void AFailedWebCheckKeepsTheProposedNamesAndSaysSo()
    {
        int[] calls = { 0 };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            if (calls[0]++ == 0)
            {
                return ToolResponse(ClaudeArtistParser.ToolName, Answer(Result("张一铎", NativeArtist("张一铎", "张一铎"))));
            }
            return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"web search is not enabled\"}}");
        }, millis =>
        {
        });
        List<string> notes = new List<string>();

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(client, Counts("张一铎"), Key, Model, null, true,
                notes, null);

        Assert.Equal("张一铎", parsed["张一铎"][0].English);
        Assert.Single(notes);
        Assert.True(notes[0].Contains("could not be checked on the web"), notes[0]);
        Assert.True(notes[0].Contains("web search is not enabled"), notes[0]);
    }

    [Fact]
    public void TheWebCheckIsOffUnlessAsked()
    {
        TwoStepModel model = new TwoStepModel(
                request => Answer(Result("张一铎", NativeArtist("张一铎", "张一铎"))), request =>
                {
                    Assert.Fail("no web check expected");
                    return null;
                });

        ClaudeArtistParser.Parse(model.Client(), Counts("张一铎"), Key, Model, null);

        Assert.Single(model.Requests);
    }

    [Fact]
    public void UserDataNeverReachesTheSystemPrompt()
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        Parse(model, Counts("Secret Band 4711"));

        Assert.False(model.Requests[0].SystemText().Contains("Secret Band 4711"));
    }

    // ---- the answer ----

    [Fact]
    public void HappyPath()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("에스파 (aespa)", Artist("에스파 (aespa)", "aespa")),
                Result("민니 ((여자)아이들)/YUQI", Artist("민니 ((여자)아이들)", "MINNIE"), Artist("YUQI", "YUQI")),
                Result("Celing Dion", Artist("Celing Dion", "Celine Dion", true))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("에스파 (aespa)", "민니 ((여자)아이들)/YUQI", "Celing Dion"));

        Assert.Equal(3, result.Count);
        Assert.Single(result["에스파 (aespa)"]);
        AssertArtist("에스파 (aespa)", "aespa", false, result["에스파 (aespa)"][0]);
        Assert.Equal(2, result["민니 ((여자)아이들)/YUQI"].Count);
        AssertArtist("민니 ((여자)아이들)", "MINNIE", false, result["민니 ((여자)아이들)/YUQI"][0]);
        AssertArtist("YUQI", "YUQI", false, result["민니 ((여자)아이들)/YUQI"][1]);
        AssertArtist("Celing Dion", "Celine Dion", true, result["Celing Dion"][0]);
    }

    [Fact]
    public void ResultFollowsTheOrderOfTheInputMap()
    {
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts["rare"] = 1;
        rawCounts["common"] = 99;
        rawCounts["middle"] = 5;
        FakeModel model = new FakeModel(SplitOnSlash);

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, rawCounts);

        Assert.Equal(new List<string> { "rare", "common", "middle" }, result.Keys.ToList());
        // while the model is asked about the common ones first
        Assert.Equal(new List<string> { "common", "middle", "rare" }, RawsOf(model.Requests[0]));
    }

    [Fact]
    public void LowConfidenceIsReadLeniently()
    {
        FakeModel model = new FakeModel(request => Answer(Result("a",
                Artist("a", "A1", true),
                Artist("a", "A2", false),
                Artist("a", "A3"),
                Obj("{\"text\":\"a\",\"english\":\"A4\",\"lowConfidence\":\"true\"}"),
                Obj("{\"text\":\"a\",\"english\":\"A5\",\"lowConfidence\":\"no\"}"),
                Obj("{\"text\":\"a\",\"english\":\"A6\",\"lowConfidence\":1}"),
                Obj("{\"text\":\"a\",\"english\":\"A7\",\"lowConfidence\":null}"),
                Obj("{\"text\":\"a\",\"english\":\"A8\",\"lowConfidence\":{\"x\":1}}"))));

        List<ParsedArtist> artists = Parse(model, Counts("a"))["a"];

        Assert.Equal(new List<bool> { true, false, false, true, false, false, false, false },
                artists.Select(a => a.IsLowConfidence).ToList());
    }

    [Fact]
    public void EnglishNamesAreTidied()
    {
        // "Rose" + a combining acute accent is the decomposed spelling of Rosé
        FakeModel model = new FakeModel(request => Answer(Result("a/b/c",
                Artist("a", "  Rose\x0301  "),
                Artist("b", "Red\t \x00A0 Velvet"),
                Artist("c", "IU\r\n"))));

        List<ParsedArtist> artists = Parse(model, Counts("a/b/c"))["a/b/c"];

        Assert.True(artists[0].English == "Rosé", "trimmed and composed (NFC)");
        Assert.True(artists[1].English == "Red Velvet", "whitespace collapsed");
        Assert.Equal("IU", artists[2].English);
    }

    [Fact]
    public void TextIsKeptExactlyAsTheModelCopiedIt()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("G.E.M. 邓紫棋 / 王嘉尔", Artist("G.E.M. 邓紫棋", "G.E.M."), Artist("王嘉尔", "Jackson Wang"))));

        List<ParsedArtist> artists = Parse(model, Counts("G.E.M. 邓紫棋 / 王嘉尔"))["G.E.M. 邓紫棋 / 王嘉尔"];

        AssertArtist("G.E.M. 邓紫棋", "G.E.M.", false, artists[0]);
        AssertArtist("王嘉尔", "Jackson Wang", false, artists[1]);
    }

    // ---- matching answers back to the input ----

    [Fact]
    public void UnknownExtraResultsAreIgnored()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("IU", Artist("IU", "IU")),
                Result("Not In The Input", Artist("Not In The Input", "Nobody")),
                Result("", Artist("x", "X"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("IU"));

        Assert.Equal(new List<string> { "IU" }, result.Keys.ToList());
    }

    [Theory]
    [InlineData("newjeans")]
    [InlineData("NEWJEANS")]
    [InlineData("  NewJeans")]
    [InlineData("NewJeans\t")]
    [InlineData(" newjeans ")]
    [InlineData("NewJeans\n")]
    public void RawIsMatchedIgnoringCaseAndOuterWhitespace(string echoed)
    {
        FakeModel model = new FakeModel(request => Answer(Result(echoed, Artist("NewJeans", "NewJeans"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("NewJeans  "));

        AssertArtist("NewJeans", "NewJeans", false, result["NewJeans  "][0]);
    }

    [Fact]
    public void RawIsMatchedIgnoringInnerWhitespaceAndUnicodeForm()
    {
        // ROSE + a combining acute accent is the decomposed spelling of the raw ROSÉ; the raw LE SSERAFIM has a no-break space
        FakeModel model = new FakeModel(request => Answer(
                Result("alan walker", Artist("Alan  Walker", "Alan Walker")),
                Result("ROSE\x0301", Artist("ROSÉ", "ROSÉ")),
                Result("LE SSERAFIM", Artist("LE\x3000SSERAFIM", "LE SSERAFIM"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("Alan  Walker", "ROSÉ", "LE\x00A0SSERAFIM"));

        Assert.Equal(3, result.Count);
        // Alan  Walker matched loosely and its text really is in the raw value
        AssertArtist("Alan  Walker", "Alan Walker", false, result["Alan  Walker"][0]);
        AssertArtist("ROSÉ", "ROSÉ", false, result["ROSÉ"][0]);
        // the model's text uses an ideographic space, the raw has a no-break space: not a substring
        AssertArtist("LE\x00A0SSERAFIM", "LE SSERAFIM", true, result["LE\x00A0SSERAFIM"][0]);
    }

    [Fact]
    public void ExactMatchBeatsLooseMatch()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("Itzy", Artist("Itzy", "ITZY-lower")),
                Result("ITZY", Artist("ITZY", "ITZY-upper"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("ITZY", "Itzy"));

        Assert.Equal("ITZY-upper", result["ITZY"][0].English);
        Assert.Equal("ITZY-lower", result["Itzy"][0].English);
    }

    [Fact]
    public void ASpellingTheModelOnlyAnsweredOnceShareItsAnswer()
    {
        FakeModel model = new FakeModel(request => Answer(Result("NewJeans", Artist("NewJeans", "NewJeans"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("NewJeans", "NewJeans  ", "newjeans"));

        // no repair needed: nothing was skipped, so 3 inputs with 1 answer still pass
        AssertArtist("NewJeans", "NewJeans", false, result["NewJeans"][0]);
        AssertArtist("NewJeans", "NewJeans", false, result["NewJeans  "][0]);
        // "NewJeans" is not in "newjeans" (case differs): the lone-artist rule uses the whole raw value
        AssertArtist("newjeans", "NewJeans", true, result["newjeans"][0]);
    }

    [Fact]
    public void DuplicateResultsForOneRawKeepTheFirst()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("IU", Artist("IU", "First")),
                Result("IU", Artist("IU", "Second"))));

        Assert.Equal("First", Parse(model, Counts("IU"))["IU"][0].English);
    }

    // ---- repairs ----

    [Fact]
    public void SkippedInputFallsBackToItself()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("a", Artist("a", "A")),
                Result("b", Artist("b", "B")),
                Result("d", Artist("d", "D"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("a", "b", "c  ", "d"));

        Assert.Equal(4, result.Count);
        Assert.Single(result["c  "]);
        // text is the raw value untouched; english is tidied like every other name
        AssertArtist("c  ", "c", true, result["c  "][0]);
        AssertArtist("a", "A", false, result["a"][0]);
    }

    [Theory]
    [InlineData("{\"raw\":\"broken\",\"artists\":[]}")]
    [InlineData("{\"raw\":\"broken\"}")]
    [InlineData("{\"raw\":\"broken\",\"artists\":null}")]
    [InlineData("{\"raw\":\"broken\",\"artists\":\"\"}")]
    [InlineData("{\"raw\":\"broken\",\"artists\":\"not a list\"}")]
    [InlineData("{\"raw\":\"broken\",\"artists\":{\"text\":\"broken\",\"english\":\"Obj\"}}")]
    [InlineData("{\"raw\":\"broken\",\"artists\":[1,\"x\",null,[],{}]}")]
    [InlineData("{\"raw\":\"broken\",\"artists\":[{\"text\":\"  \",\"english\":\"\"}]}")]
    public void EmptyMissingOrUselessArtistListsFallBackToTheRawValue(string brokenEntry)
    {
        FakeModel model = new FakeModel(request =>
        {
            JsonObject answer = Answer(Result("ok1", Artist("ok1", "Ok1")), Result("ok2", Artist("ok2", "Ok2")),
                    Result("ok3", Artist("ok3", "Ok3")), Result("ok4", Artist("ok4", "Ok4")),
                    Result("ok5", Artist("ok5", "Ok5")));
            answer.Arr("results").Add(Obj(brokenEntry));
            return answer;
        });

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("ok1", "ok2", "ok3", "ok4", "ok5", "broken"));

        Assert.Single(result["broken"]);
        AssertArtist("broken", "broken", true, result["broken"][0]);
        AssertArtist("ok3", "Ok3", false, result["ok3"][0]);
    }

    [Fact]
    public void AnAnswerThatIsMostlyUnusableIsRejected()
    {
        // 6 broken of 7 is far over a third
        FakeModel model = new FakeModel(request => Answer(
                Result("empty"),
                Obj("{\"raw\":\"missing\"}"),
                Obj("{\"raw\":\"null\",\"artists\":null}"),
                Obj("{\"raw\":\"object\",\"artists\":{\"text\":\"object\",\"english\":\"Obj\"}}"),
                Obj("{\"raw\":\"junk\",\"artists\":[1,\"x\",null,[],{}]}"),
                Obj("{\"raw\":\"blank\",\"artists\":[{\"text\":\"  \",\"english\":\"\"}]}"),
                Result("fine", Artist("fine", "Fine"))));

        ArtistAiException e = Assert.Throws<ArtistAiException>(
                () => Parse(model, Counts("empty", "missing", "null", "object", "junk", "blank", "fine")));

        Assert.Equal("The AI answer was incomplete", e.Message);
    }

    [Fact]
    public void ExactlyAThirdRepairedIsStillAccepted()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("a", Artist("a", "A")),
                Result("b", Artist("b", "B")),
                Result("c")));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("a", "b", "c"));

        AssertArtist("c", "c", true, result["c"][0]);
    }

    [Fact]
    public void MoreThanAThirdRepairedIsRejected()
    {
        FakeModel model = new FakeModel(request => Answer(Result("a", Artist("a", "A"))));

        ArtistAiException e = Assert.Throws<ArtistAiException>(() => Parse(model, Counts("a", "b", "c")));

        Assert.Equal("The AI answer was incomplete", e.Message);
    }

    [Fact]
    public void ASingleStringTheModelSkippedIsRejected()
    {
        FakeModel model = new FakeModel(request => Answer());

        Assert.Throws<ArtistAiException>(() => Parse(model, Counts("only one")));
    }

    [Fact]
    public void TheThirdIsMeasuredPerChunkOf120()
    {
        // 40 of 120 skipped is exactly a third: fine; 41 is not
        foreach (int skipped in new int[] { 40, 41 })
        {
            OrderedDictionary<string, int> rawCounts = Numbered(120);
            FakeModel model = new FakeModel(request =>
            {
                JsonObject full = SplitOnSlash(request);
                JsonArray results = full.Arr("results");
                for (int i = 0; i < skipped; i++)
                {
                    results.RemoveAt(0);
                }
                return full;
            });

            if (skipped == 40)
            {
                Assert.Equal(120, Parse(model, rawCounts).Count);
            }
            else
            {
                Assert.Throws<ArtistAiException>(() => Parse(model, rawCounts));
            }
        }
    }

    [Fact]
    public void ABadChunkLateInTheRunStillFailsTheWholeRun()
    {
        int[] request = { 0 };
        FakeModel model = new FakeModel(r => ++request[0] == 2 ? Answer() : SplitOnSlash(r));
        List<int[]> progress = new List<int[]>();

        Assert.Throws<ArtistAiException>(() => ClaudeArtistParser.Parse(model.Client(), Numbered(130), Key, Model,
                (done, total) => progress.Add(new int[] { done, total })));

        Assert.True(progress.Count == 1, "only the good first chunk was reported");
    }

    [Fact]
    public void ResultsGivenAsAJsonStringAreAccepted()
    {
        // models sometimes send an array argument as a string holding the array
        FakeModel model = new FakeModel(request =>
        {
            JsonObject answer = new JsonObject();
            answer["results"] = "[{\"raw\":\"IU\",\"artists\":\"[{\\\"text\\\":\\\"IU\\\",\\\"english\\\":\\\"IU\\\"}]\"}]";
            return answer;
        });

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("IU"));

        AssertArtist("IU", "IU", false, result["IU"][0]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"results\":null}")]
    [InlineData("{\"results\":\"not json\"}")]
    [InlineData("{\"results\":{}}")]
    [InlineData("{\"results\":[]}")]
    [InlineData("{\"results\":[1,2,\"x\"]}")]
    [InlineData("{\"results\":[{\"artists\":[]}]}")]
    [InlineData("{\"results\":[{\"raw\":null,\"artists\":[]}]}")]
    [InlineData("{\"other\":true}")]
    public void AnswersWithoutUsableResultsAreRejected(string input)
    {
        FakeModel model = new FakeModel(request => Obj(input));

        ArtistAiException e = Assert.Throws<ArtistAiException>(() => Parse(model, Counts("a", "b")));

        Assert.Equal("The AI answer was incomplete", e.Message);
    }

    // ---- text that is not in the raw value ----

    [Fact]
    public void TextMissingFromTheRawValueOfALoneArtistBecomesTheWholeRaw()
    {
        FakeModel model = new FakeModel(request => Answer(Result("Celing Dion", Artist("Celine Dion", "Celine Dion"))));

        AssertArtist("Celing Dion", "Celine Dion", true, Parse(model, Counts("Celing Dion"))["Celing Dion"][0]);
    }

    [Fact]
    public void TextMissingFromTheRawValueOfSeveralArtistsBecomesTheEnglishName()
    {
        FakeModel model = new FakeModel(request => Answer(Result("IU/Wonstein",
                Artist("IU", "IU"),
                Artist("Won Stein", "Wonstein"))));

        List<ParsedArtist> artists = Parse(model, Counts("IU/Wonstein"))["IU/Wonstein"];

        AssertArtist("IU", "IU", false, artists[0]);
        AssertArtist("Wonstein", "Wonstein", true, artists[1]);
    }

    [Fact]
    public void TextWithStrayOuterWhitespaceIsFoundAnyway()
    {
        FakeModel model = new FakeModel(request => Answer(Result("IU/Wonstein",
                Artist(" IU ", "IU"),
                Artist("\tWonstein\n", "Wonstein"))));

        List<ParsedArtist> artists = Parse(model, Counts("IU/Wonstein"))["IU/Wonstein"];

        AssertArtist("IU", "IU", false, artists[0]);
        AssertArtist("Wonstein", "Wonstein", false, artists[1]);
    }

    [Fact]
    public void BlankTextIsTreatedAsMissing()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("IU", Artist("", "IU")),
                Result("a/b", Artist("   ", "A"), Artist("b", "B"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("IU", "a/b"));

        AssertArtist("IU", "IU", true, result["IU"][0]);
        AssertArtist("A", "A", true, result["a/b"][0]);
        AssertArtist("b", "B", false, result["a/b"][1]);
    }

    [Fact]
    public void MissingTextFieldIsTreatedAsMissingToo()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("IU", Obj("{\"english\":\"IU\"}")),
                Result("a/b", Obj("{\"english\":\"A\"}"), Artist("b", "B"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("IU", "a/b"));

        AssertArtist("IU", "IU", true, result["IU"][0]);
        AssertArtist("A", "A", true, result["a/b"][0]);
    }

    [Fact]
    public void BlankEnglishFallsBackToTheText()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("에스파", Artist("에스파", "")),
                Result("a/b", Artist("a", "  "), Artist("b", "B"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("에스파", "a/b"));

        AssertArtist("에스파", "에스파", true, result["에스파"][0]);
        AssertArtist("a", "a", true, result["a/b"][0]);
        AssertArtist("b", "B", false, result["a/b"][1]);
    }

    [Fact]
    public void MissingEnglishFieldFallsBackToTheText()
    {
        FakeModel model = new FakeModel(request => Answer(Result("a /b", Obj("{\"text\":\"a \"}"), Artist("b", "B"))));

        List<ParsedArtist> artists = Parse(model, Counts("a /b"))["a /b"];

        // text keeps its exact form, english is the tidied text
        AssertArtist("a ", "a", true, artists[0]);
    }

    [Fact]
    public void TextAndEnglishBothBlankDropsThatArtist()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("a/b", Artist("", ""), Artist("a", "A"), Artist("b", "B"), Obj("{}"))));

        List<ParsedArtist> artists = Parse(model, Counts("a/b"))["a/b"];

        Assert.Equal(2, artists.Count);
        AssertArtist("a", "A", false, artists[0]);
    }

    [Fact]
    public void JunkInsideTheArtistListIsDropped()
    {
        FakeModel model = new FakeModel(request =>
        {
            JsonObject answer = Answer(Result("a/b", Artist("a", "A")));
            JsonArray artists = answer.Arr("results")[0].Arr("artists");
            artists.Add("stray string");
            artists.Add(5);
            artists.Add(null);
            artists.Add(Artist("b", "B"));
            return answer;
        });

        List<ParsedArtist> artists = Parse(model, Counts("a/b"))["a/b"];

        Assert.Equal(new List<string> { "A", "B" }, artists.Select(a => a.English).ToList());
    }

    [Fact]
    public void IfJunkLeavesExactlyOneArtistItOwnsTheWholeRawValue()
    {
        FakeModel model = new FakeModel(request =>
        {
            JsonObject answer = Answer(Result("Celing Dion"));
            answer.Arr("results")[0].Arr("artists").Add(Artist("Celine Dion", "Celine Dion"));
            answer.Arr("results")[0].Arr("artists").Add("junk");
            return answer;
        });

        AssertArtist("Celing Dion", "Celine Dion", true, Parse(model, Counts("Celing Dion"))["Celing Dion"][0]);
    }

    // ---- chunking and shared names ----

    [Fact]
    public void ChunksOf120AreSentOneAfterTheOtherAndProgressIsReported()
    {
        FakeModel model = new FakeModel(SplitOnSlash);
        List<int[]> progress = new List<int[]>();
        OrderedDictionary<string, int> rawCounts = Numbered(250);

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(model.Client(), rawCounts, Key, Model,
                (done, total) => progress.Add(new int[] { done, total }));

        Assert.Equal(3, model.Requests.Count);
        Assert.Equal(120, RawsOf(model.Requests[0]).Count);
        Assert.Equal(120, RawsOf(model.Requests[1]).Count);
        Assert.Equal(10, RawsOf(model.Requests[2]).Count);
        Assert.Equal(250, result.Count);
        Assert.Equal(3, progress.Count);
        AssertArrayEquals3(new int[] { 120, 250 }, progress[0]);
        AssertArrayEquals3(new int[] { 240, 250 }, progress[1]);
        AssertArrayEquals3(new int[] { 250, 250 }, progress[2]);

        // every string is asked exactly once, and the numbering restarts in each request
        List<string> asked = new List<string>();
        foreach (JsonObject request in model.Requests)
        {
            asked.AddRange(RawsOf(request));
            Assert.Equal(1, EntriesOf(request)[0].Int("n"));
        }
        Assert.Equal(rawCounts.Keys.ToList(), asked);
    }

    private static void AssertArrayEquals3(int[] expected, int[] actual)
    {
        Assert.Equal(expected[0], actual[0]); // done
        Assert.Equal(expected[1], actual[1]); // total
    }

    [Fact]
    public void LaterChunksAreToldTheNamesAlreadyDecided()
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        Parse(model, Numbered(250));

        Assert.Empty(KnownNamesOf(model.Requests[0]));

        List<string> second = KnownNamesOf(model.Requests[1]);
        Assert.Equal(120, second.Count);
        Assert.Equal("E-artist 0", second[0]);
        Assert.Contains("E-artist 119", second);

        List<string> third = KnownNamesOf(model.Requests[2]);
        Assert.Equal(240, third.Count);
        Assert.True(second.All(third.Contains), "names from both earlier chunks");
        Assert.True(UserMessage(model.Requests[2])
                .Contains("Names already used - reuse EXACTLY these strings when the same artist appears"));
    }

    [Fact]
    public void KnownNamesAreDistinctAndOnlyContainWhatTheModelNamed()
    {
        int[] request = { 0 };
        FakeModel model = new FakeModel(r =>
        {
            request[0]++;
            if (request[0] > 1)
            {
                return SplitOnSlash(r);
            }
            // chunk 1: "a" and "a2" are the same artist; "skipped" is never answered; "IU" appears in two raws
            List<JsonObject> results = new List<JsonObject>();
            foreach (string raw in RawsOf(r))
            {
                switch (raw)
                {
                    case "a":
                    case "a2":
                        results.Add(Result(raw, Artist(raw, "Shared Name")));
                        break;
                    case "skipped":
                        break;
                    case "no-english":
                        results.Add(Result(raw, Artist(raw, "")));
                        break;
                    default:
                        results.Add(Result(raw, Artist(raw, "Name of " + raw)));
                        break;
                }
            }
            return Answer(results.ToArray());
        });
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts["a"] = 200;
        rawCounts["a2"] = 199;
        rawCounts["skipped"] = 198;
        rawCounts["no-english"] = 197;
        for (int i = 0; i < 130; i++)
        {
            rawCounts["filler " + i] = 1;
        }

        Parse(model, rawCounts);

        List<string> known = KnownNamesOf(model.Requests[1]);
        Assert.True(known.Count(name => name == "Shared Name") == 1, "no duplicates");
        Assert.False(known.Contains("skipped"), "a repaired fallback is not a decision of the model");
        Assert.False(known.Contains("no-english"));
        Assert.Contains("Name of filler 0", known);
    }

    [Fact]
    public void KnownNamesAreCappedSoRequestsStayBounded()
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        Parse(model, Numbered(1100)); // 9 chunks of 120 + one of 20

        Assert.Equal(10, model.Requests.Count);
        Assert.Equal(960, KnownNamesOf(model.Requests[8]).Count);
        List<string> last = KnownNamesOf(model.Requests[9]);
        Assert.Equal(ClaudeArtistParser.MaxKnownNames, last.Count);
        Assert.True(last[0] == "E-artist 0", "the earliest (most used) names are the ones kept");
    }

    [Fact]
    public void MostUsedStringsGoFirstAndTiesKeepTheirOrder()
    {
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        for (int i = 0; i < 300; i++)
        {
            rawCounts["artist " + i] = (i % 3) + 1; // counts 1,2,3,1,2,3,...
        }
        FakeModel model = new FakeModel(SplitOnSlash);

        Parse(model, rawCounts);

        List<string> asked = new List<string>();
        model.Requests.ForEach(r => asked.AddRange(RawsOf(r)));
        List<string> expected = new List<string>(rawCounts.Keys);
        expected = expected.OrderByDescending(x => rawCounts[x]).ToList(); // a stable sort
        Assert.Equal(expected, asked);
        Assert.Equal("artist 2", asked[0]);
        Assert.Equal(3, EntriesOf(model.Requests[0])[0].Int("count"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(119)]
    [InlineData(120)]
    public void UpToOneChunkIsOneRequest(int n)
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        Assert.Equal(n, Parse(model, Numbered(n)).Count);
        Assert.Single(model.Requests);
    }

    [Theory]
    [InlineData(121)]
    [InlineData(239)]
    [InlineData(240)]
    public void UpToTwoChunksIsTwoRequests(int n)
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        Assert.Equal(n, Parse(model, Numbered(n)).Count);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(n - 120, RawsOf(model.Requests[1]).Count);
    }

    // ---- odd input ----

    [Fact]
    public void BlankAndNullValuesNameNoArtistAndAreNeverSent()
    {
        // a .NET dictionary cannot hold a null key, so the null raw value of the Java test cannot be listed here
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts[""] = 634;
        rawCounts["   "] = 2;
        rawCounts["\t\n"] = 1;
        rawCounts["IU"] = 5;
        FakeModel model = new FakeModel(SplitOnSlash);

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, rawCounts);

        Assert.Equal(4, result.Count);
        Assert.Empty(result[""]);
        Assert.Empty(result["   "]);
        Assert.Empty(result["\t\n"]);
        Assert.Single(result["IU"]);
        Assert.Equal(new List<string> { "IU" }, RawsOf(model.Requests[0]));
    }

    [Fact]
    public void OnlyBlankValuesMakeNoRequestAndNeedNoKey()
    {
        FakeModel model = new FakeModel(SplitOnSlash);
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts[""] = 3;

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(model.Client(), rawCounts, null, Model, null);

        Assert.Empty(result[""]);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void NullAndOddCountsAreTolerated()
    {
        // an int cannot be null: the null count of the Java test is a count of 0 here, which is what the parser reads it as
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts["none"] = 0;
        rawCounts["negative"] = -5;
        rawCounts["huge"] = int.MaxValue;
        rawCounts["zero"] = 0;
        FakeModel model = new FakeModel(SplitOnSlash);

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, rawCounts);

        Assert.Equal(4, result.Count);
        Assert.Equal(new List<string> { "huge", "none", "zero", "negative" }, RawsOf(model.Requests[0]));
        Assert.Equal(0, EntriesOf(model.Requests[0])[1].Int("count"));
    }

    [Fact]
    public void AwkwardRawValuesRoundTripExactly()
    {
        // a LINE SEPARATOR and a PARAGRAPH SEPARATOR, a control character, e + a combining acute accent, a zero-width space
        List<string> raws = new List<string>
        {
            "HUNTR/X␟EJAE␟AUDREY NUNA␟REI AMI␟KPop Demon Hunters Cast",
            "NewJeans  ",
            "  leading",
            "tab\there",
            "line\nbreak",
            "crlf\r\nbreak",
            "quote\"s and 'apostrophes' and \\backslash",
            "Ty Dolla $ign",
            "emoji 🎵 note",
            "周殷廷[重复]",
            "국제 あいう",
            "ROSE\x0301 NFD",
            "a\x2028" + "b\x2029" + "c",
            "<tag>&amp;</tag>",
            "\x0001" + "ctrl",
            "G.E.M.那紫棋 / 王嘉尔",
            "\x200B" + "zero-width",
            "very long " + new string('x', 3000)
        };
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        raws.ForEach(raw => rawCounts[raw] = 1);
        FakeModel model = new FakeModel(SplitOnSlash);

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, rawCounts);

        Assert.Equal(raws, result.Keys.ToList());
        Assert.Equal(raws.OrderBy(raw => raw, StringComparer.Ordinal).ToList(),
                RawsOf(model.Requests[0]).OrderBy(raw => raw, StringComparer.Ordinal).ToList());
        foreach (string raw in raws)
        {
            foreach (ParsedArtist artist in result[raw])
            {
                Assert.True(raw.Contains(artist.Text, StringComparison.Ordinal), "text must be inside " + raw);
                Assert.NotEmpty(artist.English);
            }
        }
        Assert.Equal("G.E.M.那紫棋 ", result["G.E.M.那紫棋 / 王嘉尔"][0].Text);
    }

    [Fact]
    public void CjkNamesPassThroughUntouched()
    {
        FakeModel model = new FakeModel(request => Answer(
                Result("陈奕迅", Artist("陈奕迅", "Eason Chan")),
                Result("周杰倫", Artist("周杰倫", "Jay Chou")),
                Result("백지영", Artist("백지영", "Baek Ji-young", true)),
                Result("セレーナ・ゴメス", Artist("セレーナ・ゴメス", "Selena Gomez"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("陈奕迅", "周杰倫", "백지영", "セレーナ・ゴメス"));

        AssertArtist("陈奕迅", "Eason Chan", false, result["陈奕迅"][0]);
        AssertArtist("백지영", "Baek Ji-young", true, result["백지영"][0]);
        AssertArtist("セレーナ・ゴメス", "Selena Gomez", false, result["セレーナ・ゴメス"][0]);
    }

    [Fact]
    public void ResultListsCanBeModifiedByTheCaller()
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model, Counts("a/b", ""));

        result["a/b"].Add(new ParsedArtist("c", "C", false));
        result[""].Add(new ParsedArtist("c", "C", false));
        Assert.Equal(3, result["a/b"].Count);
    }

    // ---- failures from the service ----

    [Fact]
    public void ServiceErrorsPropagateWithoutLeakingTheKey()
    {
        AnthropicClient client = new AnthropicClient(
                (url, headers, body, timeout) => new AnthropicClient.Response(401, "{\"error\":{\"message\":\"" + Key + "\"}}"),
                millis =>
                {
                });

        ArtistAiException e = Assert.Throws<ArtistAiException>(
                () => ClaudeArtistParser.Parse(client, Counts("IU"), Key, Model, null));

        Assert.Equal("The API key was rejected (check ANTHROPIC_API_KEY)", e.Message);
        Assert.False(e.Message.Contains(Key));
    }

    [Fact]
    public void MissingKeyIsReportedBeforeAnyRequest()
    {
        FakeModel model = new FakeModel(SplitOnSlash);

        ArtistAiException e = Assert.Throws<ArtistAiException>(
                () => ClaudeArtistParser.Parse(model.Client(), Counts("IU"), "  ", Model, null));

        Assert.Equal("No API key", e.Message);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void AFailureInALaterChunkLosesNothingAlreadyReported()
    {
        int[] request = { 0 };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            request[0]++;
            if (request[0] == 2)
            {
                return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"credit balance is too low\"}}");
            }
            JsonObject parsed = Obj(body);
            JsonObject block = new JsonObject();
            block["type"] = "tool_use";
            block["name"] = "record_artists";
            block["input"] = SplitOnSlash(parsed);
            JsonArray content = new JsonArray();
            content.Add(block);
            JsonObject message = new JsonObject();
            message["content"] = content;
            return new AnthropicClient.Response(200, message.ToJsonString());
        }, millis =>
        {
        });
        List<int[]> progress = new List<int[]>();

        ArtistAiException e = Assert.Throws<ArtistAiException>(() => ClaudeArtistParser.Parse(client,
                Numbered(200), Key, Model, (done, total) => progress.Add(new int[] { done, total })));

        Assert.True(e.Message.Contains("credit balance is too low"));
        Assert.Single(progress);
    }

    // ---- robustness against a misbehaving model ----

    [Theory]
    [InlineData(42L)]
    [InlineData(7L)]
    [InlineData(2024L)]
    [InlineData(31337L)]
    public void WhateverTheModelReturnsTheResultIsCompleteOrRejected(long seed)
    {
        List<string> pool = new List<string>
        {
            "IU", "aespa", "에스파 (aespa)", "G.E.M. 邓紫棋/王嘉尔", "K/DA & League of Legends",
            "NewJeans  ", "newjeans", "ITZY/彩领", "V.A.", "群星", "Celing Dion", "周殷廷[重复]", "민니 ((여자)아이들)",
            "Tyler, The Creator", "a", "b/c/d", "  spaced  out  ", "😀", new string('x', 200)
        };

        for (int round = 0; round < 40; round++)
        {
            JavaRandom random = new JavaRandom(seed * 1000 + round);
            OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
            int size = 1 + random.NextInt(30);
            for (int i = 0; i < size; i++)
            {
                string raw = pool[random.NextInt(pool.Count)] + (random.NextInt(4) == 0 ? " " + i : "");
                rawCounts[raw] = random.NextInt(50);
            }
            FakeModel model = new FakeModel(request => Chaos(RawsOf(request), random));

            OrderedDictionary<string, List<ParsedArtist>> result;
            try
            {
                result = Parse(model, rawCounts);
            }
            catch (ArtistAiException e)
            {
                Assert.Equal("The AI answer was incomplete", e.Message);
                continue;
            }

            Assert.Equal(rawCounts.Keys.ToList(), result.Keys.ToList());
            foreach (KeyValuePair<string, List<ParsedArtist>> entry in result)
            {
                string raw = entry.Key;
                List<ParsedArtist> artists = entry.Value;
                Assert.True(artists.Count > 0, "never lose a value: " + raw);
                foreach (ParsedArtist artist in artists)
                {
                    Assert.False(JavaString.IsBlank(artist.Text), raw);
                    Assert.NotEmpty(artist.English);
                    Assert.True(JavaString.Strip(artist.English) == artist.English, "english is trimmed");
                    Assert.False(artist.English.Contains("  "), "english has single spaces");
                    // either really in the raw value, or (several artists) replaced by the name and flagged
                    Assert.True(raw.Contains(artist.Text, StringComparison.Ordinal)
                            || (artists.Count > 1 && artist.IsLowConfidence && artist.Text == artist.English),
                            "text '" + artist.Text + "' vs raw '" + raw + "'");
                    if (artists.Count == 1)
                    {
                        Assert.True(raw.Contains(artist.Text, StringComparison.Ordinal), "a lone artist's text is always in the raw value");
                    }
                }
            }
        }
    }

    // ---- remembering the answers (ArtistParseCache) ----

    private string CacheFile()
    {
        return Path.Combine(_dir, "artist-cache.json");
    }

    private ArtistParseCache Cache()
    {
        return new ArtistParseCache(CacheFile());
    }

    private static OrderedDictionary<string, List<ParsedArtist>> Parse(AnthropicClient client,
            IEnumerable<KeyValuePair<string, int>> rawCounts, ArtistParseCache cache)
    {
        return ClaudeArtistParser.Parse(client, rawCounts, Key, Model, null, false, null, null, cache);
    }

    private static List<string> ToolNames(List<JsonObject> requests)
    {
        List<string> names = new List<string>();
        foreach (JsonObject request in requests)
        {
            names.Add(request.Arr("tools")[0].Str("name"));
        }
        return names;
    }

    /// <summary>a model that gives G.E.M. and 张一铎 a Chinese name (which the web check then looks at), and everyone else "E-name"</summary>
    private static JsonObject GroupWithChineseNames(JsonObject request)
    {
        List<JsonObject> results = new List<JsonObject>();
        foreach (string raw in RawsOf(request))
        {
            results.Add(Result(raw, raw == "G.E.M." || raw == "张一铎" ? NativeArtist(raw, raw)
                    : Artist(raw, "E-" + raw, false)));
        }
        return Answer(results.ToArray());
    }

    /// <summary>a web check that finds G.E.M.'s credit and confirms every other proposal</summary>
    private static JsonObject OfficialNames(JsonObject request)
    {
        JsonArray results = new JsonArray();
        foreach (JsonObject artist in CheckedArtists(request))
        {
            string proposed = artist.Str("proposed");
            JsonObject item = new JsonObject();
            item["n"] = artist.Int("n");
            item["official"] = proposed == "G.E.M." ? "G.E.M. 邓紫棋" : proposed;
            item["native"] = true;
            item["sure"] = true;
            results.Add(item);
        }
        JsonObject answer = new JsonObject();
        answer["results"] = results;
        return answer;
    }

    /// <summary>two chunks: G.E.M. and 119 plain Latin names, then 张一铎</summary>
    private static OrderedDictionary<string, int> ChineseNamesAndFillers()
    {
        OrderedDictionary<string, int> counts = new OrderedDictionary<string, int>();
        counts["G.E.M."] = 10;
        for (int i = 0; i < 119; i++)
        {
            counts["artist " + i] = 5;
        }
        counts["张一铎"] = 1;
        return counts;
    }

    [Fact]
    public void RawValuesTheCacheKnowsAreNeverSentToClaudeNorNeedAKey()
    {
        ArtistParseCache cache = Cache();
        cache.Put("IU", new List<ParsedArtist> { new ParsedArtist("IU", "IU (cached)", false, false) });
        cache.Put("에스파 (aespa)", new List<ParsedArtist> { new ParsedArtist("에스파 (aespa)", "aespa", true, false) });
        FakeModel model = new FakeModel(request =>
        {
            Assert.Fail("no request expected");
            return null;
        });
        List<string> status = new List<string>();
        List<int[]> progress = new List<int[]>();

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(model.Client(),
                Counts("IU", "에스파 (aespa)"), null, Model,
                (done, total) => progress.Add(new int[] { done, total }), true, new List<string>(), status.Add, cache);

        Assert.Empty(model.Requests);
        Assert.Empty(progress);
        Assert.Equal(new List<string> { "IU", "에스파 (aespa)" }, result.Keys.ToList());
        AssertArtist("IU", "IU (cached)", false, result["IU"][0]);
        AssertArtist("에스파 (aespa)", "aespa", true, result["에스파 (aespa)"][0]);
        Assert.Single(status);
        Assert.True(status[0].Contains("All 2 artist names were already known"), status[0]);
    }

    [Fact]
    public void ASecondRunOverTheSameLibraryAsksNothing()
    {
        FakeModel first = new FakeModel(SplitOnSlash);
        OrderedDictionary<string, List<ParsedArtist>> before = Parse(first.Client(), Counts("A/B", "C", "에스파 (aespa)"), Cache());
        Assert.Single(first.Requests);

        FakeModel second = new FakeModel(request =>
        {
            Assert.Fail("everything is known");
            return null;
        });
        OrderedDictionary<string, List<ParsedArtist>> after = Parse(second.Client(), Counts("A/B", "C", "에스파 (aespa)"), Cache());

        Assert.Empty(second.Requests);
        Assert.Equal(before.Keys.ToList(), after.Keys.ToList());
        foreach (string raw in before.Keys)
        {
            Assert.True(before[raw].Count == after[raw].Count, raw);
            for (int i = 0; i < before[raw].Count; i++)
            {
                AssertArtist(before[raw][i].Text, before[raw][i].English, before[raw][i].IsLowConfidence, after[raw][i]);
            }
        }
    }

    [Fact]
    public void KnownAndNewValuesAreMixedInTheOrderOfTheInput()
    {
        ArtistParseCache cache = Cache();
        cache.Put("IU", new List<ParsedArtist> { new ParsedArtist("IU", "IU (cached)", false, false) });
        FakeModel model = new FakeModel(SplitOnSlash);
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts["IU"] = 5;
        rawCounts["aespa"] = 3;
        rawCounts[""] = 2;
        rawCounts["NewJeans"] = 1;
        List<string> status = new List<string>();
        List<int[]> progress = new List<int[]>();

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(model.Client(), rawCounts, Key, Model,
                (done, total) => progress.Add(new int[] { done, total }), false, null, status.Add, cache);

        Assert.Single(model.Requests);
        Assert.True(new List<string> { "aespa", "NewJeans" }.SequenceEqual(RawsOf(model.Requests[0])), "only the unknown values are sent");
        Assert.Equal(new List<string> { "IU", "aespa", "", "NewJeans" }, result.Keys.ToList());
        AssertArtist("IU", "IU (cached)", false, result["IU"][0]);
        AssertArtist("aespa", "E-aespa", false, result["aespa"][0]);
        Assert.Empty(result[""]);
        Assert.Single(progress);
        AssertArrayEquals3(new int[] { 2, 2 }, progress[0]);

        // the new values are told the known names, so a new spelling of a known artist gets the known name
        Assert.Equal(new List<string> { "IU (cached)" }, KnownNamesOf(model.Requests[0]));
        Assert.Single(status);
        Assert.True(status[0].Contains("2 of 2 new ones, 1 already known"), status[0]);

        // and what was asked is remembered from now on
        Assert.Equal(3, Cache().Size());
        Assert.Equal("E-NewJeans", Cache().Get("NewJeans")[0].English);
        Assert.Null(Cache().Get(""));
    }

    [Fact]
    public void TheStatusSaysNothingAboutKnownValuesWhenThereAreNone()
    {
        FakeModel model = new FakeModel(SplitOnSlash);
        List<string> status = new List<string>();

        ClaudeArtistParser.Parse(model.Client(), Counts("IU", "aespa"), Key, Model, null, false, null, status.Add,
                Cache());

        Assert.Equal(new List<string> { "Asking Claude to group the artist names (2 of 2)..." }, status);
        Assert.Empty(KnownNamesOf(model.Requests[0]));
    }

    [Fact]
    public void TheWebCheckOnlyLooksAtTheNewValues()
    {
        ArtistParseCache cache = Cache();
        cache.Put("에스파 (aespa)", new List<ParsedArtist> { new ParsedArtist("에스파 (aespa)", "aespa", false, false) });
        TwoStepModel model = new TwoStepModel(GroupWithChineseNames, OfficialNames);

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(model.Client(),
                Counts("에스파 (aespa)", "张一铎"), Key, Model, null, true, new List<string>(), null, cache);

        Assert.Equal(new List<string> { "record_artists", "record_official_names" }, ToolNames(model.Requests));
        Assert.Equal(new List<string> { "张一铎" }, RawsOf(model.Requests[0]));
        string checkedText = UserMessage(model.Requests[1]);
        Assert.True(checkedText.Contains("张一铎"));
        Assert.False(checkedText.Contains("aespa"), "a known value is not looked up again");
        AssertArtist("에스파 (aespa)", "aespa", false, result["에스파 (aespa)"][0]);
    }

    [Fact]
    public void TheFinalAnswerAfterTheWebCheckIsWhatIsStored()
    {
        TwoStepModel model = new TwoStepModel(GroupWithChineseNames, OfficialNames);

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(model.Client(), Counts("G.E.M.", "IU"), Key,
                Model, null, true, new List<string>(), null, Cache());

        Assert.Equal("G.E.M. 邓紫棋", result["G.E.M."][0].English);
        ParsedArtist stored = Cache().Get("G.E.M.")[0];
        Assert.True(stored.English == "G.E.M. 邓紫棋", "not the 'G.E.M.' Claude first proposed");
        Assert.Equal("G.E.M.", stored.Text);
        Assert.True(stored.IsKeepNative);
        Assert.False(stored.IsLowConfidence);
        Assert.Equal("E-IU", Cache().Get("IU")[0].English);
    }

    // ---- searching the web for the names that are still unsure ----

    /// <summary>a response of the model that searched the web before it called the tool</summary>
    private static AnthropicClient.Response SearchedResponse(string tool, JsonObject input)
    {
        JsonObject search = new JsonObject();
        search["type"] = "server_tool_use";
        search["name"] = "web_search";
        JsonObject block = new JsonObject();
        block["type"] = "tool_use";
        block["name"] = tool;
        block["input"] = input;
        JsonArray content = new JsonArray();
        content.Add(search);
        content.Add(block);
        JsonObject message = new JsonObject();
        message["content"] = content;
        message["stop_reason"] = "tool_use";
        return new AnthropicClient.Response(200, message.ToJsonString());
    }

    /// <summary>
    /// grouping, the first web check and the search for unsure names are told apart by their system prompt; the search
    /// answers with a web search only when it is told to (searching = false: it answers from memory)
    /// </summary>
    private sealed class SearchModel
    {
        public readonly List<JsonObject> Requests = new List<JsonObject>();
        public bool Searching = true;
        public Func<JsonObject, JsonObject> Grouping;
        public Func<JsonObject, JsonObject> FirstCheck;
        public Func<JsonObject, JsonObject> Search;

        public AnthropicClient Client()
        {
            return new AnthropicClient(Post, millis =>
            {
            });
        }

        public List<JsonObject> Searches => Requests.Where(r => r.SystemText() == ClaudeArtistParser.ResearchSystemPrompt).ToList();

        public AnthropicClient.Response Post(string url, IDictionary<string, string> headers, string body, TimeSpan timeout)
        {
            JsonObject request = Obj(body);
            Requests.Add(request);
            string system = request.SystemText();
            string tool = request.Arr("tools")[0].Str("name");
            if (system == ClaudeArtistParser.ResearchSystemPrompt)
            {
                JsonObject answer = Search(request);
                return Searching ? SearchedResponse(tool, answer) : ToolResponse(tool, answer);
            }
            return ToolResponse(tool, (system == ClaudeArtistParser.VerifySystemPrompt ? FirstCheck : Grouping)(request));
        }
    }

    /// <summary>"K.Will" is what Claude proposes for 케이윌, and it is not sure</summary>
    private static JsonObject GroupUnsure(JsonObject request)
    {
        return Answer(Result("케이윌", Artist("케이윌", "K.Will", true)));
    }

    [Fact]
    public void AnUnsureNameIsSearchedForOnTheWebAndASureFindingTakesAwayPleaseCheck()
    {
        SearchModel model = new SearchModel
        {
            Grouping = GroupUnsure,
            FirstCheck = request => Verdicts(request, "K.Will", false, false), // the first check could not settle it
            Search = request => Verdicts(request, "K.Will", false, true), // the search did
        };
        List<string> notes = new List<string>();

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key,
                Model, null, true, notes, null);

        ParsedArtist artist = parsed["케이윌"][0];
        Assert.Equal("K.Will", artist.English);
        Assert.False(artist.IsLowConfidence);
        Assert.True(artist.IsResearched);
        Assert.Empty(notes);
        Assert.Equal(3, model.Requests.Count);
        JsonObject search = model.Searches.Single();
        Assert.Equal("web_search", search.Arr("tools")[1].Str("name"));
        Assert.Equal(ClaudeArtistParser.ResearchMaxSearches, search.Arr("tools")[1].Int("max_uses"));
        Assert.True(UserMessage(search).Contains("케이윌"), "it is told how the library spells the artist");
        Assert.True(UserMessage(search).Contains("K.Will"), "and what is proposed");
    }

    [Fact]
    public void ANameTheSearchCannotSettleStaysMarkedPleaseCheckAndIsNotSearchedForAgain()
    {
        SearchModel model = new SearchModel
        {
            Grouping = GroupUnsure,
            FirstCheck = request => Verdicts(request, "K.Will", false, false),
            Search = request => Verdicts(request, "K.Will", false, false),
        };
        ArtistParseCache cache = Cache();

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key,
                Model, null, true, new List<string>(), null, cache);

        Assert.True(parsed["케이윌"][0].IsLowConfidence, "still not sure: the user is asked to check");
        Assert.True(parsed["케이윌"][0].IsResearched);
        ParsedArtist stored = Cache().Get("케이윌")[0];
        Assert.True(stored.IsLowConfidence);
        Assert.True(stored.IsResearched, "the cache remembers that it was searched for");

        // the next run does not pay for the same search again
        model.Requests.Clear();
        ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key, Model, null, true, new List<string>(), null, Cache());
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void ASearchThatBetterNamesTheArtistReplacesTheProposedName()
    {
        SearchModel model = new SearchModel
        {
            Grouping = request => Answer(Result("백지영", Artist("백지영", "Baek Ji-young", true))),
            FirstCheck = request => Verdicts(request, "Baek Ji-young", false, false),
            Search = request => Verdicts(request, "Baek Z Young", false, true),
        };

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("백지영"), Key,
                Model, null, true, new List<string>(), null);

        Assert.Equal("Baek Z Young", parsed["백지영"][0].English);
        Assert.False(parsed["백지영"][0].IsLowConfidence);
    }

    [Fact]
    public void AnAnswerThatCameWithoutASearchIsAskedForAgainOnceAndToldToSearch()
    {
        int[] searchRequests = { 0 };
        SearchModel model = new SearchModel
        {
            Grouping = GroupUnsure,
            FirstCheck = request => Verdicts(request, "K.Will", false, false),
            Search = request => Verdicts(request, "K.Will", false, true),
        };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            if (request.SystemText() == ClaudeArtistParser.ResearchSystemPrompt)
            {
                // the first answer is from memory, the second one searched
                model.Searching = ++searchRequests[0] > 1;
            }
            return model.Post(null, null, body, timeout);
        }, millis =>
        {
        });

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(client, Counts("케이윌"), Key, Model, null,
                true, new List<string>(), null);

        Assert.Equal(2, model.Searches.Count);
        Assert.False(UserMessage(model.Searches[0]).Contains("without searching the web"));
        Assert.True(UserMessage(model.Searches[1]).Contains("without searching the web"), "the second ask says to search");
        Assert.Single(model.Searches[1].Arr("messages")); // a fresh ask, not a continuation of the answer from memory
        Assert.False(parsed["케이윌"][0].IsLowConfidence);
    }

    [Fact]
    public void AnAnswerFromMemoryIsAcceptedWhenTheSecondAskDoesNotSearchEither()
    {
        SearchModel model = new SearchModel
        {
            Searching = false,
            Grouping = GroupUnsure,
            FirstCheck = request => Verdicts(request, "K.Will", false, false),
            Search = request => Verdicts(request, "K.Will", false, false),
        };

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key,
                Model, null, true, new List<string>(), null);

        Assert.Equal(2, model.Searches.Count); // asked once more, not for ever
        Assert.True(parsed["케이윌"][0].IsLowConfidence);
    }

    [Fact]
    public void NamesThatAreSureAfterTheFirstCheckAreNotSearchedFor()
    {
        SearchModel model = new SearchModel
        {
            Grouping = GroupUnsure,
            FirstCheck = request => Verdicts(request, "K.Will", false, true),
            Search = request =>
            {
                Assert.Fail("nothing is unsure, so nothing is searched for");
                return null;
            },
        };

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key,
                Model, null, true, new List<string>(), null);

        Assert.Empty(model.Searches);
        Assert.False(parsed["케이윌"][0].IsLowConfidence);
    }

    [Fact]
    public void ARememberedUnsureNameThatWasNeverSearchedForIsSearchedForOnceWithoutGroupingAgain()
    {
        ArtistParseCache cache = Cache();
        cache.Put("케이윌", new List<ParsedArtist> { new ParsedArtist("케이윌", "K.Will", true, false) }); // an older run
        SearchModel model = new SearchModel
        {
            Grouping = request =>
            {
                Assert.Fail("the value is known, it is not grouped again");
                return null;
            },
            FirstCheck = request =>
            {
                Assert.Fail("only the search is made");
                return null;
            },
            Search = request => Verdicts(request, "K.Will", false, true),
        };

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key,
                Model, null, true, new List<string>(), null, cache);

        Assert.Single(model.Requests);
        Assert.False(parsed["케이윌"][0].IsLowConfidence);
        ParsedArtist stored = Cache().Get("케이윌")[0];
        Assert.False(stored.IsLowConfidence);
        Assert.True(stored.IsResearched);

        model.Requests.Clear();
        ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key, Model, null, true, new List<string>(), null, Cache());
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void ARememberedUnsureNameIsNotSearchedForWithoutAKeyOrWhenTheWebCheckIsOff()
    {
        ArtistParseCache cache = Cache();
        cache.Put("케이윌", new List<ParsedArtist> { new ParsedArtist("케이윌", "K.Will", true, false) });
        SearchModel model = new SearchModel
        {
            Grouping = GroupUnsure,
            FirstCheck = request => Verdicts(request, "K.Will", false, true),
            Search = request => Verdicts(request, "K.Will", false, true),
        };
        List<string> notes = new List<string>();

        // a library whose names are all known works without a key
        OrderedDictionary<string, List<ParsedArtist>> noKey = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), null, Model,
                null, true, notes, null, cache);
        // and with the web check off nothing is searched for
        OrderedDictionary<string, List<ParsedArtist>> off = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key, Model,
                null, false, notes, null, cache);

        Assert.Empty(model.Requests);
        Assert.Empty(notes);
        Assert.True(noKey["케이윌"][0].IsLowConfidence);
        Assert.True(off["케이윌"][0].IsLowConfidence);
    }

    [Fact]
    public void AFailedSearchIsReportedAndTheNamesAlreadyFoundAreKept()
    {
        int[] calls = { 0 };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            string system = request.SystemText();
            if (system == ClaudeArtistParser.ResearchSystemPrompt)
            {
                return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"too many searches\"}}");
            }
            calls[0]++;
            return system == ClaudeArtistParser.VerifySystemPrompt
                    ? ToolResponse(ClaudeArtistParser.VerifyToolName, Verdicts(request, "K.Will", false, false))
                    : ToolResponse(ClaudeArtistParser.ToolName, GroupUnsure(request));
        }, millis =>
        {
        });
        List<string> notes = new List<string>();

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(client, Counts("케이윌"), Key, Model, null,
                true, notes, null);

        Assert.Equal("K.Will", parsed["케이윌"][0].English);
        Assert.True(parsed["케이윌"][0].IsLowConfidence);
        Assert.Single(notes);
        Assert.True(notes[0].Contains("too many searches"), notes[0]);
    }

    [Fact]
    public void TheSearchFollowsTheSameNamingRulesAsTheFirstCheck()
    {
        string prompt = ClaudeArtistParser.ResearchSystemPrompt;

        Assert.Contains("MUST run at least one web search", prompt);
        foreach (string needle in new string[] { "aespa", "(G)I-DLE", "IU", "ROSÉ", "G.E.M. 邓紫棋", "NEVER turn a Chinese name into pinyin",
                "MUST get exactly the same official name", "A group and its members are different artists", "sure" })
        {
            Assert.True(prompt.Contains(needle), "missing: " + needle);
        }
        Assert.True(ClaudeArtistParser.VerifySystemPrompt.Contains("For each artist give:"), "the first check keeps its rules");
    }

    [Fact]
    public void TheCacheKeepsTheResearchedFlagAndAnOlderFileReadsAsNotResearched()
    {
        ArtistParseCache cache = Cache();
        cache.Put("a", new List<ParsedArtist> { new ParsedArtist("a", "A", true, false, true) });
        cache.Put("b", new List<ParsedArtist> { new ParsedArtist("b", "B", true, false) });
        cache.Flush();

        ArtistParseCache reloaded = Cache();

        Assert.True(reloaded.Get("a")[0].IsResearched);
        Assert.False(reloaded.Get("b")[0].IsResearched);
        File.WriteAllText(CacheFile(), "{\"c\":[{\"text\":\"c\",\"english\":\"C\",\"lowConfidence\":true,\"keepNative\":false}]}");
        Assert.False(Cache().Get("c")[0].IsResearched);
    }

    // ---- what the review found in the search ----

    /// <summary>every artist of a search request gets its proposed name plus " Official", sure</summary>
    private static JsonObject OfficialOf(JsonObject request, Func<JsonObject, bool> includes)
    {
        JsonArray results = new JsonArray();
        foreach (JsonObject artist in CheckedArtists(request))
        {
            if (!includes(artist))
            {
                continue;
            }
            JsonObject item = new JsonObject();
            item["n"] = artist.Int("n");
            item["official"] = artist.Str("proposed") + " Official";
            item["native"] = false;
            item["sure"] = true;
            results.Add(item);
        }
        JsonObject answer = new JsonObject();
        answer["results"] = results;
        return answer;
    }

    private static ArtistParseCache Unsure(ArtistParseCache cache, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            string raw = "raw" + i.ToString("00");
            cache.Put(raw, new List<ParsedArtist> { new ParsedArtist(raw, "Artist" + i.ToString("00"), true, false) });
        }
        return cache;
    }

    private static OrderedDictionary<string, int> RawCounts(int count)
    {
        return Counts(Enumerable.Range(1, count).Select(i => "raw" + i.ToString("00")).ToArray());
    }

    [Fact]
    public void ABatchThatFailsKeepsWhatTheBatchesBeforeItFoundAndTheNextRunDoesTheRest()
    {
        ArtistParseCache cache = Unsure(Cache(), 13); // three batches: 6, 6 and 1
        int[] requests = { 0 };
        bool[] failSecond = { true };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            Assert.Equal(ClaudeArtistParser.ResearchSystemPrompt, request.SystemText());
            requests[0]++;
            if (failSecond[0] && requests[0] == 2)
            {
                return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"nope\"}}");
            }
            return SearchedResponse(ClaudeArtistParser.VerifyToolName, OfficialOf(request, artist => true));
        }, millis =>
        {
        });
        List<string> notes = new List<string>();

        ClaudeArtistParser.Parse(client, RawCounts(13), Key, Model, null, true, notes, null, cache);

        Assert.Equal(2, requests[0]);
        Assert.Single(notes);
        Assert.Contains("nope", notes[0]);
        ArtistParseCache stored = Cache();
        for (int i = 1; i <= 13; i++)
        {
            string name = "Artist" + i.ToString("00");
            ParsedArtist artist = stored.Get("raw" + i.ToString("00"))[0];
            Assert.Equal(i <= 6, artist.IsResearched);
            Assert.Equal(i > 6, artist.IsLowConfidence);
            Assert.Equal(i <= 6 ? name + " Official" : name, artist.English);
        }

        // the next run pays only for the names that were not searched for
        requests[0] = 0;
        failSecond[0] = false;
        ClaudeArtistParser.Parse(client, RawCounts(13), Key, Model, null, true, new List<string>(), null, Cache());
        Assert.Equal(2, requests[0]);
        Assert.False(Cache().Get("raw13")[0].IsLowConfidence);
    }

    [Fact]
    public void ANameTheAnswerLeftOutIsNotMarkedAndIsSearchedForNextTime()
    {
        ArtistParseCache cache = Unsure(Cache(), 2);
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
                SearchedResponse(ClaudeArtistParser.VerifyToolName, OfficialOf(Obj(body), artist => artist.Int("n") == 1)), millis =>
        {
        });

        ClaudeArtistParser.Parse(client, RawCounts(2), Key, Model, null, true, new List<string>(), null, cache);

        ParsedArtist answered = Cache().Get("raw01")[0];
        ParsedArtist left = Cache().Get("raw02")[0];
        Assert.True(answered.IsResearched);
        Assert.False(answered.IsLowConfidence);
        Assert.False(left.IsResearched, "it was not answered, so it was not searched for");
        Assert.True(left.IsLowConfidence);
        Assert.Equal("Artist02", left.English);
    }

    [Fact]
    public void AnAnswerWithNoResultAtAllMarksNothing()
    {
        ArtistParseCache cache = Unsure(Cache(), 2);
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
                SearchedResponse(ClaudeArtistParser.VerifyToolName, Obj("{\"results\":[]}")), millis =>
        {
        });
        List<string> notes = new List<string>();

        ClaudeArtistParser.Parse(client, RawCounts(2), Key, Model, null, true, notes, null, cache);

        Assert.Single(notes);
        Assert.All(new[] { "raw01", "raw02" }, raw =>
        {
            Assert.False(Cache().Get(raw)[0].IsResearched);
            Assert.True(Cache().Get(raw)[0].IsLowConfidence);
        });
    }

    [Fact]
    public void AnAnswerFromMemoryNeverMakesANameSureAndDoesNotCountAsASearch()
    {
        SearchModel model = new SearchModel
        {
            Searching = false,
            Grouping = GroupUnsure,
            FirstCheck = request => Verdicts(request, "K.Will", false, false),
            Search = request => Verdicts(request, "K.Will", false, true), // claims to be sure, without a search
        };

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌"), Key,
                Model, null, true, new List<string>(), null);

        Assert.True(parsed["케이윌"][0].IsLowConfidence, "no search stands behind it");
        Assert.False(parsed["케이윌"][0].IsResearched, "so it is searched for next time");
    }

    [Fact]
    public void ASureArtistWithTheSameNameAsASearchedOneIsLeftAlone()
    {
        SearchModel model = new SearchModel
        {
            Grouping = request => Answer(Result("케이윌", Artist("케이윌", "K.Will", true)), Result("K.Will", Artist("K.Will", "K.Will", false))),
            FirstCheck = request => Obj("{\"results\":[]}"), // the first check has nothing to say
            Search = request => OfficialOf(request, artist => true),
        };

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("케이윌", "K.Will"), Key,
                Model, null, true, new List<string>(), null);

        Assert.Equal("K.Will Official", parsed["케이윌"][0].English);
        Assert.True(parsed["케이윌"][0].IsResearched);
        Assert.Equal("K.Will", parsed["K.Will"][0].English); // it was never unsure: it keeps its name and its status
        Assert.False(parsed["K.Will"][0].IsLowConfidence);
        Assert.False(parsed["K.Will"][0].IsResearched);
    }

    [Fact]
    public void AFailedSearchStillStoresTheNamesTheFirstCheckSettled()
    {
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            string system = request.SystemText();
            if (system == ClaudeArtistParser.ResearchSystemPrompt)
            {
                return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"too many searches\"}}");
            }
            return system == ClaudeArtistParser.VerifySystemPrompt
                    ? ToolResponse(ClaudeArtistParser.VerifyToolName, Verdicts(request, "K.Will", false, false))
                    : ToolResponse(ClaudeArtistParser.ToolName, GroupUnsure(request));
        }, millis =>
        {
        });
        List<string> notes = new List<string>();

        ClaudeArtistParser.Parse(client, Counts("케이윌"), Key, Model, null, true, notes, null, Cache());

        Assert.Single(notes);
        ParsedArtist stored = Cache().Get("케이윌")?[0];
        Assert.NotNull(stored); // the first check was done, so the answer is kept (and searched for next time)
        Assert.True(stored.IsLowConfidence);
        Assert.False(stored.IsResearched);
    }

    [Fact]
    public void AFailedSearchOfRememberedNamesDoesNotStopTheFirstCheckOfNewOnes()
    {
        ArtistParseCache cache = Cache();
        cache.Put("케이윌", new List<ParsedArtist> { new ParsedArtist("케이윌", "K.Will", true, false) }); // an older run
        int[] firstChecks = { 0 };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            string system = request.SystemText();
            if (system == ClaudeArtistParser.ResearchSystemPrompt)
            {
                return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"too many searches\"}}");
            }
            if (system == ClaudeArtistParser.VerifySystemPrompt)
            {
                firstChecks[0]++;
                return ToolResponse(ClaudeArtistParser.VerifyToolName, Verdicts(request, "Aespa", false, true));
            }
            return ToolResponse(ClaudeArtistParser.ToolName, Answer(Result("에스파", Artist("에스파", "aespa", false))));
        }, millis =>
        {
        });
        List<string> notes = new List<string>();

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(client, Counts("케이윌", "에스파"), Key, Model,
                null, true, notes, null, cache);

        Assert.Equal(1, firstChecks[0]);
        Assert.Equal("Aespa", parsed["에스파"][0].English);
    }

    [Fact]
    public void ValuesTheModelSkippedAndValuesWithNoLetterAreNotLookedUpOnTheWeb()
    {
        List<string> checkMessages = new List<string>();
        SearchModel model = new SearchModel
        {
            Grouping = request => Answer(
                    Result("A1", Artist("A1", "Alpha One", true)),
                    Result("???", Artist("???", "???", true)),
                    Result("B2", Artist("B2", "Bravo Two", false)),
                    Result("C3", Artist("C3", "Charlie Three", false))), // D4 is skipped: it gets a stand-in
            FirstCheck = request =>
            {
                checkMessages.Add(UserMessage(request));
                return Verdicts(request, "Alpha One", false, true);
            },
            Search = request =>
            {
                checkMessages.Add(UserMessage(request));
                return Verdicts(request, "Alpha One", false, true);
            },
        };

        OrderedDictionary<string, List<ParsedArtist>> parsed = ClaudeArtistParser.Parse(model.Client(), Counts("A1", "???", "B2", "C3", "D4"),
                Key, Model, null, true, new List<string>(), null, Cache());

        Assert.Single(checkMessages); // only Alpha One was checked, and being sure it was not searched for
        Assert.Contains("Alpha One", checkMessages[0]);
        Assert.DoesNotContain("D4", checkMessages[0]);
        Assert.DoesNotContain("???", checkMessages[0]);
        Assert.True(parsed["D4"][0].IsLowConfidence);
        Assert.True(parsed["???"][0].IsLowConfidence);
    }

    [Fact]
    public void OnlyTheSearchPromptRefusesMemoryAsAReasonToBeSure()
    {
        Assert.Contains("your own memory never makes it true", ClaudeArtistParser.ResearchSystemPrompt);
        Assert.DoesNotContain("your own firm knowledge", ClaudeArtistParser.ResearchSystemPrompt);
        Assert.Contains("or your own firm knowledge confirm the official name", ClaudeArtistParser.VerifySystemPrompt);
    }

    [Fact]
    public void TheCacheIsWrittenAfterEachChunk()
    {
        List<int> onDiskAtEachRequest = new List<int>();
        FakeModel model = new FakeModel(request =>
        {
            onDiskAtEachRequest.Add(Cache().Size());
            return SplitOnSlash(request);
        });

        Parse(model.Client(), Numbered(250), Cache());

        Assert.Equal(3, model.Requests.Count);
        Assert.True(new List<int> { 0, 120, 240 }.SequenceEqual(onDiskAtEachRequest), "each chunk is on disk before the next is asked");
        Assert.Equal(250, Cache().Size());
    }

    [Fact]
    public void WhatWasFinishedSurvivesAFailureInALaterChunkAndIsNotAskedAgain()
    {
        int[] request = { 0 };
        AnthropicClient failing = new AnthropicClient((url, headers, body, timeout) =>
        {
            if (++request[0] == 2)
            {
                return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"credit balance is too low\"}}");
            }
            return ToolResponse("record_artists", SplitOnSlash(Obj(body)));
        }, millis =>
        {
        });

        ArtistAiException e = Assert.Throws<ArtistAiException>(() => Parse(failing, Numbered(200), Cache()));

        Assert.True(e.Message.Contains("credit balance is too low"));
        Assert.True(Cache().Size() == 120, "the first chunk was paid for and is kept");

        FakeModel retry = new FakeModel(SplitOnSlash);
        OrderedDictionary<string, List<ParsedArtist>> result = Parse(retry.Client(), Numbered(200), Cache());

        Assert.Single(retry.Requests);
        Assert.Equal(80, RawsOf(retry.Requests[0]).Count);
        Assert.Equal(200, result.Count);
        Assert.Equal(200, Cache().Size());
    }

    [Fact]
    public void ADamagedCacheFileIsJustAnEmptyCache()
    {
        File.WriteAllText(CacheFile(), "{ this is not json", new UTF8Encoding(false));
        FakeModel model = new FakeModel(SplitOnSlash);

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model.Client(), Counts("IU", "aespa"), Cache());

        Assert.Equal(2, result.Count);
        Assert.Equal(new List<string> { "IU", "aespa" }, RawsOf(model.Requests[0]));
        Assert.True(Cache().Size() == 2, "the damaged file was replaced by a good one");
    }

    [Fact]
    public void AValueTheModelSkippedIsAnsweredWithAStandInButNotRemembered()
    {
        FakeModel model = new FakeModel(request => Answer(Result("IU", Artist("IU", "IU")),
                Result("aespa", Artist("aespa", "aespa"))));

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model.Client(), Counts("IU", "aespa", "NewJeans"), Cache());

        AssertArtist("NewJeans", "NewJeans", true, result["NewJeans"][0]);
        Assert.Equal(2, Cache().Size());
        Assert.True(Cache().Get("NewJeans") == null, "asked again next time, the stand-in is not an answer");
    }

    [Fact]
    public void BlankValuesAreNotRemembered()
    {
        FakeModel model = new FakeModel(SplitOnSlash);
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts[""] = 3;
        rawCounts["IU"] = 5;

        Parse(model.Client(), rawCounts, Cache());

        Assert.Equal(1, Cache().Size());
        Assert.Equal(new List<string> { "IU" }, RawsOf(model.Requests[0]));
    }

    [Fact]
    public void TheListsInTheResultCanBeChangedWithoutChangingTheCache()
    {
        ArtistParseCache cache = Cache();
        cache.Put("IU", new List<ParsedArtist> { new ParsedArtist("IU", "IU", false) });
        FakeModel model = new FakeModel(SplitOnSlash);

        OrderedDictionary<string, List<ParsedArtist>> result = Parse(model.Client(), Counts("IU"), cache);
        result["IU"].Clear();

        Assert.Single(cache.Get("IU"));
    }

    [Fact]
    public void LaterChunksAreToldTheVerifiedNamesNotTheFirstProposals()
    {
        OrderedDictionary<string, int> rawCounts = new OrderedDictionary<string, int>();
        rawCounts["G.E.M."] = 10;
        for (int i = 0; i < 130; i++)
        {
            rawCounts["artist " + i] = 1;
        }
        TwoStepModel model = new TwoStepModel(GroupWithChineseNames, OfficialNames);

        ClaudeArtistParser.Parse(model.Client(), rawCounts, Key, Model, null, true, new List<string>(), null, Cache());

        Assert.Equal(new List<string> { "record_artists", "record_official_names", "record_artists" }, ToolNames(model.Requests));
        List<string> told = KnownNamesOf(model.Requests[2]);
        Assert.True(told.Contains("G.E.M. 邓紫棋"), string.Join(", ", told));
        Assert.False(told.Contains("G.E.M."), "the proposal was replaced by the checked name");
    }

    [Fact]
    public void WithACacheTheWebCheckRunsAfterEachChunkAndWithoutOneOnceAtTheEnd()
    {
        TwoStepModel withCache = new TwoStepModel(GroupWithChineseNames, OfficialNames);
        ClaudeArtistParser.Parse(withCache.Client(), ChineseNamesAndFillers(), Key, Model, null, true,
                new List<string>(), null, Cache());
        Assert.Equal(new List<string> { "record_artists", "record_official_names", "record_artists", "record_official_names" },
                ToolNames(withCache.Requests));

        TwoStepModel withoutCache = new TwoStepModel(GroupWithChineseNames, OfficialNames);
        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(withoutCache.Client(),
                ChineseNamesAndFillers(), Key, Model, null, true, new List<string>(), null, null);
        Assert.True(new List<string> { "record_artists", "record_artists", "record_official_names" }
                .SequenceEqual(ToolNames(withoutCache.Requests)), "no cache: the old behaviour");
        Assert.Equal("G.E.M. 邓紫棋", result["G.E.M."][0].English);
    }

    [Fact]
    public void AFailedWebCheckIsReportedOnceAndItsUncheckedNamesAreNotRemembered()
    {
        List<string> tools = new List<string>();
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            string tool = ToolNames(new List<JsonObject> { request })[0];
            tools.Add(tool);
            if (tool == ClaudeArtistParser.VerifyToolName)
            {
                return new AnthropicClient.Response(400, "{\"error\":{\"message\":\"web search is not enabled\"}}");
            }
            return ToolResponse(tool, GroupWithChineseNames(request));
        }, millis =>
        {
        });
        List<string> notes = new List<string>();

        OrderedDictionary<string, List<ParsedArtist>> result = ClaudeArtistParser.Parse(client, ChineseNamesAndFillers(), Key, Model,
                null, true, notes, null, Cache());

        Assert.True(new List<string> { "record_artists", "record_official_names", "record_artists" }.SequenceEqual(tools),
                "after one failure the check is not tried again");
        Assert.Single(notes);
        Assert.True(notes[0].Contains("web search is not enabled"), notes[0]);
        Assert.True(result.Count == 121, "every value has an answer");
        Assert.True(result["G.E.M."][0].English == "G.E.M.", "the proposal is used");
        Assert.Equal("张一铎", result["张一铎"][0].English);

        ArtistParseCache stored = Cache();
        Assert.True(stored.Get("G.E.M.") == null, "it should have been checked: asked and checked again next time");
        Assert.Null(stored.Get("张一铎"));
        Assert.True(stored.Get("artist 7")[0].English == "E-artist 7", "plain Latin names need no check");
        Assert.Equal(119, stored.Size());
    }

    /// <summary>
    /// java.util.Random, so that the scenarios of the misbehaving model are the very ones the Java test plays
    /// </summary>
    private sealed class JavaRandom
    {
        private long _seed;

        public JavaRandom(long seed)
        {
            _seed = (seed ^ 0x5DEECE66DL) & ((1L << 48) - 1);
        }

        private int Next(int bits)
        {
            _seed = unchecked(_seed * 0x5DEECE66DL + 0xBL) & ((1L << 48) - 1);
            return (int)(_seed >>> (48 - bits));
        }

        public int NextInt(int bound)
        {
            int r = Next(31);
            int m = bound - 1;
            if ((bound & m) == 0)
            {
                return (int)((bound * (long)r) >> 31);
            }
            unchecked
            {
                for (int u = r; u - (r = u % bound) + m < 0; u = Next(31))
                {
                }
            }
            return r;
        }

        public int NextInt()
        {
            return Next(32);
        }

        public bool NextBoolean()
        {
            return Next(1) != 0;
        }

        /// <summary>Collections.shuffle(list, random)</summary>
        public void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count; i > 1; i--)
            {
                int j = NextInt(i);
                (list[i - 1], list[j]) = (list[j], list[i - 1]);
            }
        }
    }

    /// <summary>a model having a bad day: skips, empties, garbles, re-cases, pads, adds strangers</summary>
    private static JsonObject Chaos(List<string> raws, JavaRandom random)
    {
        List<JsonObject> results = new List<JsonObject>();
        foreach (string raw in raws)
        {
            string echoed;
            switch (random.NextInt(6))
            {
                case 0:
                    echoed = raw.ToUpperInvariant();
                    break;
                case 1:
                    echoed = "  " + raw + "\t";
                    break;
                case 2:
                    echoed = raw.ToLowerInvariant().Replace(" ", "   ");
                    break;
                default:
                    echoed = raw;
                    break;
            }
            switch (random.NextInt(9))
            {
                case 0:
                    // skipped
                    break;
                case 1:
                    results.Add(Result(echoed));
                    break;
                case 2:
                    results.Add(Result(echoed, Artist("garbled " + random.NextInt(), "G" + random.NextInt())));
                    break;
                case 3:
                    results.Add(Result(echoed, Artist(raw, "")));
                    break;
                case 4:
                    results.Add(Result(echoed, Artist("", "Just English"), Artist(raw, "  Padded   Name ")));
                    break;
                case 5:
                {
                    // the Java substring may cut a surrogate pair in two; a lone surrogate cannot be written as JSON text here
                    int half = raw.Length / 2;
                    if (half > 0 && char.IsHighSurrogate(raw[half - 1]))
                    {
                        half--;
                    }
                    results.Add(Result(echoed, Artist(raw.Substring(0, half), "Half")));
                    break;
                }
                case 6:
                    results.Add(Result(echoed, Artist(raw, "Rose\x0301", random.NextBoolean())));
                    break;
                default:
                {
                    string[] pieces = JavaString.Split(raw, "/");
                    JsonObject[] artists = new JsonObject[pieces.Length];
                    for (int i = 0; i < pieces.Length; i++)
                    {
                        artists[i] = Artist(pieces[i], "E " + JavaString.Strip(pieces[i]));
                    }
                    results.Add(Result(echoed, artists));
                    break;
                }
            }
            if (random.NextInt(5) == 0)
            {
                results.Add(Result("stranger " + random.NextInt(), Artist("s", "S")));
            }
        }
        random.Shuffle(results);
        return Answer(results.ToArray());
    }
}
