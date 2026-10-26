using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using static MusicManager.Tests.DataAccess.ClaudeTestSupport;

namespace MusicManager.Tests.DataAccess;

public class ClaudeRomanizerTest : IDisposable
{
    private const string Key = "sk-ant-api03-TEST-KEY-abcdefghijklmnop";
    private const string Model = "claude-test-model";

    private readonly string _temp;

    public ClaudeRomanizerTest()
    {
        _temp = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, true);
        }
        catch (IOException)
        {
            // a temp folder left behind is harmless
        }
    }

    /// <summary>plays the API: each request gets the answer the brain makes of it (or a scripted failure)</summary>
    private sealed class FakeApi
    {
        public readonly List<JsonObject> Requests = new List<JsonObject>();
        private readonly Func<JsonObject, JsonObject> _brain;
        private readonly Queue<AnthropicClient.Response> _failures = new Queue<AnthropicClient.Response>();

        public FakeApi(Func<JsonObject, JsonObject> brain)
        {
            _brain = brain;
        }

        public FakeApi FailFirst(int status, string body)
        {
            _failures.Enqueue(new AnthropicClient.Response(status, body));
            return this;
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
            if (_failures.Count > 0)
            {
                return _failures.Dequeue();
            }
            return ToolResponse(ClaudeRomanizer.ToolName, _brain(request));
        }
    }

    // ---- reading requests, writing answers ----

    private static List<JsonObject> LinesOf(JsonObject request)
    {
        string user = request.Arr("messages")[0].Str("content");
        List<JsonObject> lines = new List<JsonObject>();
        foreach (string row in user.Split('\n'))
        {
            if (row.StartsWith("{\"n\":", StringComparison.Ordinal))
            {
                lines.Add(Obj(row));
            }
        }
        return lines;
    }

    /// <summary>the answer the tool takes: one text, a line "n: piece|piece" for each numbered line</summary>
    private static JsonObject Answer(IEnumerable<KeyValuePair<int, string>> romajiByNumber)
    {
        JsonObject answer = new JsonObject();
        answer["romaji"] = string.Join("\n", romajiByNumber.Select(pair => pair.Key + ": " + pair.Value));
        return answer;
    }

    /// <summary>every non-ASCII character becomes "x", words joined with "|": a valid romanization of any line</summary>
    private static JsonObject Xify(JsonObject request)
    {
        OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
        foreach (JsonObject line in LinesOf(request))
        {
            List<string> pieces = new List<string>();
            foreach (JsonNode word in line.Arr("words"))
            {
                pieces.Add(Regex.Replace(word.GetValue<string>(), "[^\\x00-\\x7F]", "x"));
            }
            romaji[line.Int("n")] = string.Join("|", pieces);
        }
        return Answer(romaji);
    }

    /// <summary>
    /// answers each line by its words (stripped, joined with "|"), whatever number it has in the request, so a line that is
    /// asked again on its own gets the same answer; a line that is not in the table gets none
    /// </summary>
    private static Func<JsonObject, JsonObject> ByWords(Dictionary<string, string> answers)
    {
        return request =>
        {
            OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
            foreach (JsonObject line in LinesOf(request))
            {
                string key = string.Join("|", line.Arr("words").Select(word => word.GetValue<string>()));
                if (answers.TryGetValue(key, out string text))
                {
                    romaji[line.Int("n")] = text;
                }
            }
            return Answer(romaji);
        };
    }

    private static RomajiLine Line(params string[] words)
    {
        return new RomajiLine(new List<string>(words));
    }

    private static List<RomajiLine> Lines(int count)
    {
        List<RomajiLine> lines = new List<RomajiLine>();
        for (int i = 0; i < count; i++)
        {
            lines.Add(Line("가" + i + " ", "나"));
        }
        return lines;
    }

    private RomajiCache Cache()
    {
        return new RomajiCache(Path.Combine(_temp, "cache.json"));
    }

    private static RomajiResult Run(FakeApi api, RomajiCache cache, List<RomajiLine> lines)
    {
        return ClaudeRomanizer.Run(api.Client(), cache, lines, Key, Model, null, millis =>
        {
        });
    }

    private static Dictionary<int, string> Numbered(params (int Number, string Romaji)[] answers)
    {
        Dictionary<int, string> map = new Dictionary<int, string>();
        foreach ((int number, string romaji) in answers)
        {
            map[number] = romaji;
        }
        return map;
    }

    // ---- tests ----

    [Fact]
    public void ManyLinesGoInOneRequestAndComeBackWordForWord()
    {
        FakeApi api = new FakeApi(Xify);
        List<RomajiLine> lines = new List<RomajiLine> { Line("문득 ", "달이"), Line("私は") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Single(api.Requests);
        Assert.Equal(new List<string> { "xx", "xx" }, result.Get(lines[0].Key));
        Assert.Equal(new List<string> { "xx" }, result.Get(lines[1].Key));
        Assert.Null(result.StoppedBecause);
        Assert.Equal(0, result.CachedLines);
    }

    [Fact]
    public void TheRequestOffersTheToolWithMediumEffortAndNeverForcesIt()
    {
        FakeApi api = new FakeApi(Xify);

        Run(api, Cache(), Lines(1));

        JsonObject request = api.Requests[0];
        Assert.Equal("auto", request.Child("tool_choice").Str("type"));
        Assert.Equal("medium", request.Child("output_config").Str("effort"));
        Assert.Equal(ClaudeRomanizer.SystemPrompt, request.SystemText());
    }

    [Fact]
    public void ARepeatedChorusIsAskedAboutOnce()
    {
        FakeApi api = new FakeApi(Xify);
        List<RomajiLine> lines = new List<RomajiLine> { Line("가 ", "나"), Line("가 ", "나"), Line("다 ", "라"), Line("가 ", "나") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(2, LinesOf(api.Requests[0]).Count);
        Assert.NotNull(result.Get(lines[0].Key));
    }

    [Fact]
    public void LinesAreSplitIntoRequestsOfAtMostTwoHundredAndEveryOneIsAnswered()
    {
        FakeApi api = new FakeApi(Xify);
        List<RomajiLine> lines = Lines(500);

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(3, api.Requests.Count);
        Assert.Equal(200, LinesOf(api.Requests[0]).Count);
        Assert.Equal(100, LinesOf(api.Requests[2]).Count);
        Assert.Equal(3, result.Requests);
        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
    }

    [Fact]
    public void ProgressIsReportedAfterEveryRequest()
    {
        FakeApi api = new FakeApi(Xify);
        List<int[]> seen = new List<int[]>();

        ClaudeRomanizer.Run(api.Client(), Cache(), Lines(450), Key, Model,
                (requestsDone, requestsTotal, linesDone, linesTotal) => seen
                        .Add(new int[] { requestsDone, requestsTotal, linesDone, linesTotal }),
                millis =>
                {
                });

        Assert.Equal(3, seen.Count);
        Assert.Equal(new List<int> { 1, 3, 200, 450 }, seen[0].ToList());
        Assert.Equal(new List<int> { 2, 3, 400, 450 }, seen[1].ToList());
        Assert.Equal(new List<int> { 3, 3, 450, 450 }, seen[2].ToList());
    }

    [Fact]
    public void WhatWasRomanizedIsSavedSoTheNextRunAsksOnlyAboutTheRest()
    {
        RomajiCache cache = Cache();
        List<RomajiLine> firstBatch = Lines(5);
        Run(new FakeApi(Xify), cache, firstBatch);

        // a new program run reads the cache back from disk
        RomajiCache reloaded = Cache();
        List<RomajiLine> all = new List<RomajiLine>(firstBatch);
        all.AddRange(Lines(8).GetRange(5, 3));
        FakeApi api = new FakeApi(Xify);

        RomajiEstimate estimate = ClaudeRomanizer.Estimate(reloaded, all);
        RomajiResult result = Run(api, reloaded, all);

        Assert.Equal(8, estimate.UniqueLines);
        Assert.Equal(5, estimate.CachedLines);
        Assert.Equal(3, estimate.LinesToAsk);
        Assert.Equal(1, estimate.Requests);
        Assert.Single(api.Requests);
        Assert.Equal(3, LinesOf(api.Requests[0]).Count);
        Assert.Equal(5, result.CachedLines);
        Assert.NotNull(result.Get(all[0].Key));
    }

    [Fact]
    public void AFullyCachedRunMakesNoRequestAtAll()
    {
        RomajiCache cache = Cache();
        List<RomajiLine> lines = Lines(4);
        Run(new FakeApi(Xify), cache, lines);
        FakeApi api = new FakeApi(request =>
        {
            Assert.Fail("nothing should be asked");
            return null;
        });

        RomajiResult result = Run(api, cache, lines);

        Assert.Empty(api.Requests);
        Assert.Equal(4, result.CachedLines);
        Assert.Equal(0, ClaudeRomanizer.Estimate(cache, lines).Requests);
    }

    [Fact]
    public void AnAnswerWithTheWrongNumberOfWordsIsAskedAgainOnItsOwn()
    {
        int[] calls = { 0 };
        FakeApi api = new FakeApi(request =>
        {
            calls[0]++;
            if (calls[0] == 1)
            { // the first answer gets line 2 wrong
                return Answer(Numbered((1, "ga0 | na"), (2, "only one piece")));
            }
            return Xify(request);
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("가0 ", "나"), Line("가1 ", "나") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(2, api.Requests.Count);
        Assert.True(LinesOf(api.Requests[1]).Count == 1, "only the wrong line is asked again");
        Assert.Equal(new List<string> { "ga0", "na" }, result.Get(lines[0].Key));
        Assert.NotNull(result.Get(lines[1].Key));
    }

    [Fact]
    public void RomanizationStillInKoreanOrChineseIsRefusedAndTheLineIsLeftOut()
    {
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "ga | 나"), (2, "ga | na"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("가 ", "나"), Line("가 ", "다") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[0].Key)); // Hangul left in the answer is not a romanization
        Assert.Equal(new List<string> { "ga", "na" }, result.Get(lines[1].Key));
        Assert.Equal("Claude's answers could not be used for 1 line after 3 tries", result.StoppedBecause);
        Assert.Equal(1 + ClaudeRomanizer.RepairStages, api.Requests.Count); // the refused line is asked again, twice, and never given up silently
    }

    [Fact]
    public void AccentsAreFoldedAwaySoTheResultIsPlainEnglishLetters()
    {
        // a Japanese line (it has kana): its kanji are romanized
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "tōkyō | Rosé"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("東京 ", "ロゼ") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "tokyo", "Rose" }, result.Get(lines[0].Key));
    }

    // ---- Chinese characters: only Korean and Japanese are romanized ----

    [Fact]
    public void AKoreanLineKeepsItsChineseWordsWithoutAskingAboutThemAndTheRestIsRomanized()
    {
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "Rosé"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("東京 ", "로제") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "東京", "Rose" }, result.Get(lines[0].Key));
        // only the word with Hangul is sent: the Chinese word is copied by the program, so it cannot come back changed
        Assert.Equal(new List<string> { "로제" }, LinesOf(api.Requests[0])[0].Arr("words").Select(w => w.GetValue<string>()).ToList());
    }

    [Fact]
    public void WordsThatNeedNoRomanizingAreCopiedByTheProgramAndNeverSent()
    {
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "(aideul)"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("Know ", "My ", "(以我之名) ", "- ", "(아이들)") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "Know", "My", "(以我之名)", "-", "(aideul)" }, result.Get(lines[0].Key));
        Assert.Equal(new List<string> { "(아이들)" }, LinesOf(api.Requests[0])[0].Arr("words").Select(w => w.GetValue<string>()).ToList());
    }

    [Fact]
    public void ChineseCharactersTurnedIntoPinyinInsideAKoreanWordAreRefused()
    {
        FakeApi api = new FakeApi(ByWords(new Dictionary<string, string>
        {
            ["作词：소연|(아이들)"] = "zuoci：Soyeon|(aideul)", ["作词：소연|(아이돌)"] = "作词：Soyeon|(aidol)"
        }));
        List<RomajiLine> lines = new List<RomajiLine> { Line("作词：소연 ", "(아이들)"), Line("作词：소연 ", "(아이돌)") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[0].Key)); // pinyin is not what was asked: the Chinese characters have to stay
        Assert.Equal(new List<string> { "作词：Soyeon", "(aidol)" }, result.Get(lines[1].Key));
    }

    [Fact]
    public void KoreanLinesWithChineseCharactersInSeveralWordsKeepEveryOne()
    {
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "作词：Soyeon|(aideul)"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("Know ", "作词：소연 ", "田小娟 ", "(아이들)") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "Know", "作词：Soyeon", "田小娟", "(aideul)" }, result.Get(lines[0].Key));
    }

    [Fact]
    public void AChineseCharacterMovedToAnotherWordOrChangedOrDroppedIsRefused()
    {
        FakeApi api = new FakeApi(ByWords(new Dictionary<string, string>
        {
            ["以我소연|之名소연"] = "之名soyeon|以我soyeon", ["以我소연|之名소연이"] = "以我soyeon|zhimingsoyeoni",
            ["以我소연|之名소연아"] = "以我soyeon|之名soyeona", ["以我소연|之名소연저"] = "以我soyeon|之名|soyeon"
        }));
        List<RomajiLine> lines = new List<RomajiLine>
        {
            Line("以我소연 ", "之名소연"), Line("以我소연 ", "之名소연이"), Line("以我소연 ", "之名소연아"), Line("以我소연 ", "之名소연저")
        };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[0].Key)); // same characters, other order: not kept
        Assert.Null(result.Get(lines[1].Key)); // one word written as pinyin
        Assert.Equal(new List<string> { "以我soyeon", "之名soyeona" }, result.Get(lines[2].Key));
        Assert.Null(result.Get(lines[3].Key)); // the number of pieces does not match the words
    }

    [Fact]
    public void AJapaneseLineHasNoChineseCharacterLeftEvenWhenOneWordIsOnlyKanji()
    {
        // 真 | 面 | 目 | そ | う: the words that are only kanji belong to a Japanese line (it has kana), so they are romanized
        FakeApi api = new FakeApi(ByWords(new Dictionary<string, string>
        {
            ["真|面|目|そ|う"] = "ma | ji | me | so | u", ["真|面|目|で|も"] = "真 | ji | me | de | mo"
        }));
        List<RomajiLine> lines = new List<RomajiLine>
        {
            Line("真", "面", "目", "そ", "う"), Line("真", "面", "目", "で", "も")
        };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "ma", "ji", "me", "so", "u" }, result.Get(lines[0].Key));
        Assert.Null(result.Get(lines[1].Key));
    }

    [Fact]
    public void ACompatibilityIdeographAndTheCharacterItStandsForCountAsTheSame()
    {
        FakeApi api = new FakeApi(ByWords(new Dictionary<string, string>
        {
            ["\U0000F914소연|아"] = "\U0000F914soyeon|a", ["\U0000F914소연|야"] = "\U00006A02soyeon|ya"
        }));
        List<RomajiLine> lines = new List<RomajiLine>
        {
            Line("\U0000F914소연 ", "아"), Line("\U0000F914소연 ", "야")
        };

        RomajiResult result = Run(api, Cache(), lines);

        // the answer is accepted whichever of the two spellings it writes; both are the same Chinese character
        Assert.Equal(new List<string> { "\U00006A02soyeon", "a" }, result.Get(lines[0].Key));
        Assert.Equal(new List<string> { "\U00006A02soyeon", "ya" }, result.Get(lines[1].Key));
    }

    [Fact]
    public void AChineseCharacterOutsideTheBasicPlaneIsKeptWhole()
    {
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "\U00020000soyeon|a"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("\U00020000소연 ", "아") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "\U00020000soyeon", "a" }, result.Get(lines[0].Key));
    }

    [Fact]
    public void ARomanizationWithNoLetterButTheKeptChineseCharactersIsRefused()
    {
        // the Hangul word came back as punctuation: nothing was romanized
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "-"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("田小娟 ", "소연") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[0].Key));
    }

    [Fact]
    public void AnOldCachedPinyinAnswerForAKoreanLineWithChineseCharactersIsAskedAgain()
    {
        RomajiCache cache = Cache();
        RomajiLine line = Line("作词：소연 ", "(아이들)");
        cache.Put(line.Key, new List<string> { "zuoci：Soyeon", "(aideul)" }); // what the earlier version saved
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "作词：Soyeon|(aideul)"))));

        RomajiEstimate estimate = ClaudeRomanizer.Estimate(cache, new List<RomajiLine> { line });
        RomajiResult result = Run(api, cache, new List<RomajiLine> { line });

        Assert.Equal(0, estimate.CachedLines);
        Assert.Single(api.Requests);
        Assert.Equal(0, result.CachedLines);
        Assert.Equal(new List<string> { "作词：Soyeon", "(aideul)" }, result.Get(line.Key));
        Assert.Equal(new List<string> { "作词：Soyeon", "(aideul)" }, cache.Get(line.Key)); // the new answer replaced it
    }

    [Fact]
    public void AnOldCachedAnswerThatKeptTheChineseCharactersIsStillUsed()
    {
        RomajiCache cache = Cache();
        RomajiLine line = Line("(以我之名) ", "(아이들)");
        cache.Put(line.Key, new List<string> { "(以我之名)", "(aideul)" });
        FakeApi api = new FakeApi(request =>
        {
            Assert.Fail("nothing should be asked");
            return null;
        });

        RomajiResult result = Run(api, cache, new List<RomajiLine> { line });

        Assert.Empty(api.Requests);
        Assert.Equal(1, result.CachedLines);
    }

    [Fact]
    public void EveryLineIsToldWhetherItIsKoreanOrJapanese()
    {
        FakeApi api = new FakeApi(Xify);

        Run(api, Cache(), new List<RomajiLine> { Line("가 ", "나"), Line("私は"), Line("真", "そ"), Line("東京 ", "로제") });

        List<JsonObject> asked = LinesOf(api.Requests[0]);
        Assert.Equal(new List<string> { "ko", "ja", "ja", "ko" }, asked.Select(line => line.Str("lang")).ToList());
    }

    [Fact]
    public void ThePromptKeepsChineseCharactersInKoreanLinesAndNeverAsksForPinyin()
    {
        string prompt = ClaudeRomanizer.SystemPrompt;

        Assert.Contains("CHINESE CHARACTERS IN A KOREAN LINE", prompt);
        Assert.Contains("STAY CHINESE CHARACTERS", prompt);
        Assert.Contains("\"lang\"", prompt);
        Assert.Contains("NEVER Hanyu Pinyin", prompt);
        Assert.DoesNotContain("zuoci", prompt);
        Assert.DoesNotContain("bianqu", prompt);
        Assert.DoesNotContain("written in Hanyu Pinyin", prompt);
    }

    [Fact]
    public void AnEmptyPieceForAWordWithLettersIsInvalidButPunctuationMayStayAsItIs()
    {
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "|na"), (2, "na"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("가 ", "나"), Line("- ", "나") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[0].Key)); // the first word came back empty
        // the punctuation word is not sent at all: it is copied, and only the Hangul word is answered
        Assert.Equal(new List<string> { "-", "na" }, result.Get(lines[1].Key));
    }

    [Fact]
    public void ARejectedKeyStopsTheRunAtOnceWithoutRetrying()
    {
        FakeApi api = new FakeApi(Xify).FailFirst(401, "{}");

        RomajiResult result = Run(api, Cache(), Lines(200));

        Assert.Single(api.Requests);
        Assert.Equal(0, result.Requests);
        Assert.Contains("API key was rejected", result.StoppedBecause);
    }

    [Fact]
    public void ATemporaryFailureIsRetriedAfterAPauseAndTheRunCarriesOn()
    {
        // 429 four times: the client's own retries give up, the romanizer pauses and tries the same lines again
        FakeApi api = new FakeApi(Xify);
        for (int i = 0; i < 4; i++)
        {
            api.FailFirst(429, "{}");
        }
        List<long> pauses = new List<long>();

        RomajiResult result = ClaudeRomanizer.Run(api.Client(), Cache(), Lines(3), Key, Model, null, pauses.Add);

        // one pause before the retry, taken in short slices so that a run that is over does not wait it out
        Assert.Equal(ClaudeRomanizer.RetryPauseMillis, pauses.Sum());
        Assert.All(pauses, slice => Assert.True(slice <= ClaudeRomanizer.PauseSliceMillis));
        Assert.Null(result.StoppedBecause);
        Assert.Equal(1, result.Requests);
    }

    [Fact]
    public void RepeatedFailuresStopTheRunButKeepWhatWasDone()
    {
        // two good requests, then the service keeps failing
        int[] good = { 0 };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            if (good[0]++ >= 2)
            {
                return new AnthropicClient.Response(503, "{}");
            }
            return ToolResponse(ClaudeRomanizer.ToolName, Xify(request));
        }, millis =>
        {
        });
        List<RomajiLine> lines = Lines(900);
        RomajiCache cache = Cache();

        RomajiResult result = ClaudeRomanizer.Run(client, cache, lines, Key, Model, null, millis =>
        {
        });

        Assert.NotNull(result.StoppedBecause);
        Assert.True(result.StoppedBecause.Contains("run it again"), result.StoppedBecause);
        Assert.Equal(2, result.Requests);
        Assert.NotNull(result.Get(lines[0].Key));
        Assert.NotNull(result.Get(lines[399].Key));
        Assert.Null(result.Get(lines[400].Key));
        Assert.NotNull(cache.Get(lines[0].Key)); // saved for the next run
    }

    [Fact]
    public void AnAnswerThatIsCutOffIsAskedForInTwoHalves()
    {
        int[] calls = { 0 };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            if (calls[0]++ == 0)
            {
                return new AnthropicClient.Response(200, "{\"content\":[],\"stop_reason\":\"max_tokens\"}");
            }
            return ToolResponse(ClaudeRomanizer.ToolName, Xify(request));
        }, millis =>
        {
        });
        List<RomajiLine> lines = Lines(10);

        RomajiResult result = ClaudeRomanizer.Run(client, Cache(), lines, Key, Model, null, millis =>
        {
        });

        Assert.Null(result.StoppedBecause);
        Assert.Equal(2, result.Requests);
        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
    }

    [Fact]
    public void TheKeyNeverAppearsInTheRequestBody()
    {
        FakeApi api = new FakeApi(Xify);

        Run(api, Cache(), Lines(2));

        Assert.False(api.Requests[0].ToJsonString().Contains(Key));
    }

    [Fact]
    public void AnUnreadableCacheFileIsTreatedAsEmpty()
    {
        File.WriteAllText(Path.Combine(_temp, "cache.json"), "{ this is not json");
        RomajiCache cache = Cache();
        List<RomajiLine> lines = Lines(2);

        Assert.Equal(0, cache.Size());
        FakeApi api = new FakeApi(Xify);
        Run(api, cache, lines);

        Assert.Single(api.Requests);
        Assert.NotNull(Cache().Get(lines[0].Key)); // and it is replaced by a good one
    }

    [Fact]
    public void WithoutAKeySavedLinesAreServedAndTheRestAreNeitherAskedAboutNorFail()
    {
        RomajiCache cache = Cache();
        List<RomajiLine> saved = Lines(3);
        Run(new FakeApi(Xify), cache, saved);
        List<RomajiLine> all = new List<RomajiLine>(saved);
        all.AddRange(Lines(5).GetRange(3, 2));
        FakeApi api = new FakeApi(request =>
        {
            Assert.Fail("nothing may be asked without a key");
            return null;
        });
        List<int[]> progress = new List<int[]>();

        foreach (string noKey in new string[] { null, "", "   " })
        {
            RomajiResult result = ClaudeRomanizer.Run(api.Client(), cache, all, noKey, Model,
                    (requestsDone, requestsTotal, linesDone, linesTotal) => progress
                            .Add(new int[] { requestsDone, requestsTotal, linesDone, linesTotal }),
                    millis =>
                    {
                    });

            Assert.True(api.Requests.Count == 0, "the network is not contacted");
            Assert.Equal(3, result.CachedLines);
            Assert.Equal(0, result.Requests);
            Assert.Equal("no Anthropic API key", result.StoppedBecause);
            for (int i = 0; i < saved.Count; i++)
            {
                Assert.Equal(new List<string> { "x" + i, "x" }, result.Get(saved[i].Key));
            }
            Assert.Null(result.Get(all[3].Key));
            Assert.Null(result.Get(all[4].Key));
        }
        Assert.Empty(progress);
    }

    [Fact]
    public void WithoutAKeyThereIsNothingToReportWhenEverythingIsSaved()
    {
        RomajiCache cache = Cache();
        List<RomajiLine> lines = Lines(4);
        Run(new FakeApi(Xify), cache, lines);
        FakeApi api = new FakeApi(Xify);

        RomajiResult result = ClaudeRomanizer.Run(api.Client(), cache, lines, null, Model, null, millis =>
        {
        });

        Assert.Empty(api.Requests);
        Assert.True(result.StoppedBecause == null, "nothing was left to ask, so nothing went wrong");
        Assert.Equal(4, result.CachedLines);
        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
    }

    // ---- several requests at once ----

    /// <summary>a transport that several workers can use at once; it counts how many requests are in flight</summary>
    private sealed class ConcurrentApi
    {
        private readonly Func<JsonObject, JsonObject> _brain;
        private readonly object _gate = new object();
        private int _arrived;
        public readonly List<JsonObject> Requests = new List<JsonObject>();
        public int InFlight;
        public int MaxInFlight;
        public int Calls;
        public int HoldFirst; // the first requests wait (up to two seconds) until this many are in flight, so a test can see them overlap
        public int WorkMillis; // how long answering takes
        public int FailFirstCalls; // transport calls that fail with 503 before the service recovers
        public int Status; // when set, every call answers with it

        public ConcurrentApi(Func<JsonObject, JsonObject> brain)
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
            bool holds;
            lock (_gate)
            {
                Calls++;
                if (Status != 0)
                {
                    return new AnthropicClient.Response(Status, "{}");
                }
                if (Calls <= FailFirstCalls)
                {
                    return new AnthropicClient.Response(503, "{}");
                }
                Requests.Add(request);
                InFlight++;
                MaxInFlight = Math.Max(MaxInFlight, InFlight);
                holds = ++_arrived <= HoldFirst;
            }
            try
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (holds && waited.ElapsedMilliseconds < 2000)
                {
                    lock (_gate)
                    {
                        if (InFlight >= HoldFirst)
                        {
                            break;
                        }
                    }
                    Thread.Sleep(5);
                }
                if (WorkMillis > 0)
                {
                    Thread.Sleep(WorkMillis);
                }
                return ToolResponse(ClaudeRomanizer.ToolName, _brain(request));
            }
            finally
            {
                lock (_gate)
                {
                    InFlight--;
                }
            }
        }
    }

    private static RomajiResult RunWith(ConcurrentApi api, int workers, List<RomajiLine> lines, RomajiProgressListener progress = null,
            ClaudeRomanizer.Pause pause = null, RomajiCache cache = null)
    {
        return ClaudeRomanizer.Run(api.Client(), cache ?? new RomajiCache(Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid() + ".json")),
                lines, Key, Model, progress, pause ?? (millis => { }), workers);
    }

    [Fact]
    public void SeveralRequestsAreInFlightAtOnce()
    {
        ConcurrentApi api = new ConcurrentApi(Xify) { HoldFirst = 4 };
        List<RomajiLine> lines = Lines(1000); // five requests

        RomajiResult result = RunWith(api, 4, lines);

        Assert.Equal(4, api.MaxInFlight);
        Assert.Equal(5, result.Requests);
        Assert.Null(result.StoppedBecause);
        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
    }

    [Fact]
    public void NoMoreRequestsAreInFlightThanWereAskedFor()
    {
        ConcurrentApi api = new ConcurrentApi(Xify) { WorkMillis = 30 };

        RomajiResult result = RunWith(api, 3, Lines(2000)); // ten requests

        Assert.True(api.MaxInFlight <= 3, "in flight: " + api.MaxInFlight);
        Assert.True(api.MaxInFlight >= 2, "they did overlap");
        Assert.Equal(10, result.Requests);
    }

    [Fact]
    public void NeverMoreThanTheCeilingWhateverIsAskedFor()
    {
        ConcurrentApi api = new ConcurrentApi(Xify) { WorkMillis = 20 };

        RunWith(api, 500, Lines(10000)); // fifty requests

        Assert.True(api.MaxInFlight <= ClaudeRomanizer.MaxWorkers, "in flight: " + api.MaxInFlight);
    }

    [Fact]
    public void SeveralWorkersGiveTheSameAnswersAsOneAtATime()
    {
        List<RomajiLine> lines = Lines(1000);

        RomajiResult one = RunWith(new ConcurrentApi(Xify), 1, lines);
        RomajiResult many = RunWith(new ConcurrentApi(Xify), 6, lines);

        foreach (RomajiLine line in lines)
        {
            Assert.Equal(one.Get(line.Key), many.Get(line.Key));
        }
        Assert.Equal(one.Requests, many.Requests);
    }

    [Fact]
    public void ProgressWithSeveralWorkersCountsEveryRequestOnceAndNeverGoesBack()
    {
        List<int[]> seen = new List<int[]>();
        ConcurrentApi api = new ConcurrentApi(Xify) { WorkMillis = 10 };

        RunWith(api, 4, Lines(1000), (requestsDone, requestsTotal, linesDone, linesTotal) =>
        {
            lock (seen)
            {
                seen.Add(new int[] { requestsDone, requestsTotal, linesDone, linesTotal });
            }
        });

        Assert.Equal(5, seen.Count);
        for (int i = 0; i < seen.Count; i++)
        {
            Assert.Equal(i + 1, seen[i][0]);
            Assert.Equal(5, seen[i][1]);
            Assert.Equal(1000, seen[i][3]);
            Assert.True(i == 0 || seen[i][2] > seen[i - 1][2], "lines done only grow");
        }
        Assert.Equal(1000, seen[4][2]);
    }

    [Fact]
    public void TheLinesAnAnswerGotWrongAreAskedAgainTogetherOnceTheFirstAsksAreDone()
    {
        HashSet<string> seen = new HashSet<string>();
        List<RomajiLine> lines = Lines(450); // three requests: 200, 200, 50
        HashSet<string> wrong = new HashSet<string>(new[] { 0, 100, 200, 300, 400 }.Select(i => "가" + i)); // the first word of those lines
        List<int[]> progress = new List<int[]>();
        ConcurrentApi api = null;
        api = new ConcurrentApi(request =>
        {
            OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
            foreach (JsonObject asked in LinesOf(request))
            {
                string word = asked.Arr("words")[0].GetValue<string>();
                bool isWrongOne = wrong.Contains(word);
                lock (seen)
                {
                    if (isWrongOne && seen.Add(word))
                    {
                        continue; // the first time it is asked about, this line gets no answer
                    }
                }
                romaji[asked.Int("n")] = string.Join("|", asked.Arr("words").Select(w => Regex.Replace(w.GetValue<string>(), "[^\\x00-\\x7F]", "x")));
            }
            return Answer(romaji);
        });

        RomajiResult result = RunWith(api, 1, lines, (done, total, linesDone, linesTotal) => progress.Add(new int[] { done, total }));

        Assert.Equal(4, api.Requests.Count);
        Assert.Equal(5, LinesOf(api.Requests[3]).Count); // all five, in one request, after the three first asks
        Assert.Equal(4, result.Requests);
        Assert.Equal(4, progress.Count); // the three first asks, and the second ask of the wrong lines: a run is not frozen at 100%
        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
    }

    [Fact]
    public void NothingIsAskedAgainWhenNotASingleLineWasAnsweredWell()
    {
        ConcurrentApi api = new ConcurrentApi(request => Answer(new Dictionary<int, string>()));
        List<RomajiLine> lines = Lines(30);

        RomajiResult result = RunWith(api, 1, lines);

        // nothing in the answer could be read: a handful of lines is asked once more (a small request), no more
        Assert.Equal(2, api.Requests.Count);
        Assert.Null(result.Get(lines[0].Key));
    }

    [Fact]
    public void NothingIsAskedAgainWhenTheAnswersOfABigRunCannotBeReadAtAll()
    {
        ConcurrentApi api = new ConcurrentApi(request => Answer(new Dictionary<int, string>()));

        RomajiResult result = RunWith(api, 1, Lines(500)); // 200, 200, 100 lines

        Assert.Equal(2, api.Requests.Count); // two big requests without one readable line end the run
        Assert.Contains("could not be read", result.StoppedBecause);
    }

    [Fact]
    public void ABurstOfFailuresSlowsTheRunDownInsteadOfEndingIt()
    {
        // four requests in flight, and the service rejects every call until 16 have been made: each of the four requests
        // fails after its own retries, all at the same moment
        ConcurrentApi api = new ConcurrentApi(Xify) { FailFirstCalls = 16, HoldFirst = 0 };
        List<long> pauses = new List<long>();
        List<RomajiLine> lines = Lines(1000);

        RomajiResult result = RunWith(api, 4, lines, null, millis =>
        {
            lock (pauses)
            {
                pauses.Add(millis);
            }
        });

        Assert.Null(result.StoppedBecause);
        Assert.True(pauses.Count >= 1, "it paused before it asked again");
        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
        Assert.Equal(5, result.Requests);
    }

    [Fact]
    public void ARejectedKeyStopsEveryWorkerAtOnce()
    {
        ConcurrentApi api = new ConcurrentApi(Xify) { Status = 401 };

        RomajiResult result = RunWith(api, 6, Lines(2000));

        Assert.Contains("API key was rejected", result.StoppedBecause);
        Assert.Equal(0, result.Requests);
        Assert.True(api.Calls <= 6, "only the requests already on their way were made: " + api.Calls);
    }

    [Fact]
    public void ASurpriseInAWorkerStopsTheRunAndReachesTheCaller()
    {
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) => throw new InvalidOperationException("boom"), millis =>
        {
        });

        Assert.Throws<InvalidOperationException>(() => ClaudeRomanizer.Run(client, Cache(), Lines(1000), Key, Model, null, millis =>
        {
        }, 4));
    }

    [Fact]
    public void WhenEverythingIsSavedNoWorkerIsStartedAndNothingIsAsked()
    {
        RomajiCache cache = Cache();
        List<RomajiLine> lines = Lines(10);
        Run(new FakeApi(Xify), cache, lines);
        ConcurrentApi api = new ConcurrentApi(Xify);

        RomajiResult result = RunWith(api, 8, lines, null, null, cache);

        Assert.Empty(api.Requests);
        Assert.Equal(10, result.CachedLines);
    }

    [Fact]
    public void TheAnswerIsOneTextAndTheToolTakesNothingElse()
    {
        FakeApi api = new FakeApi(Xify);

        Run(api, Cache(), Lines(2));

        JsonObject schema = api.Requests[0].Arr("tools")[0].Child("input_schema");
        Assert.Equal(new List<string> { "romaji" }, schema.Child("properties").Select(p => p.Key).ToList());
        Assert.Equal("string", schema.Child("properties").Child("romaji").Str("type"));
    }

    [Fact]
    public void AnswerLinesThatAreNotNumberedOrNumberedOutOfRangeAreIgnored()
    {
        int[] asked = { 0 };
        FakeApi api = new FakeApi(request =>
        {
            JsonObject answer = new JsonObject();
            answer["romaji"] = asked[0]++ == 0
                    ? "Here is the romanization:\n\n1: ga0|na\n99: x|y\n0: x|y\nno number|here\n2：ga1|na\n"
                    : ""; // and nothing the second time
            return answer;
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("가0 ", "나"), Line("가1 ", "나"), Line("가2 ", "나") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "ga0", "na" }, result.Get(lines[0].Key));
        Assert.Equal(new List<string> { "ga1", "na" }, result.Get(lines[1].Key)); // a full-width colon is read too
        Assert.Null(result.Get(lines[2].Key)); // never answered
    }

    [Fact]
    public void ARequestHoldsAtMostTwoHundredLinesAndTheirTextStaysShort()
    {
        FakeApi api = new FakeApi(Xify);
        List<RomajiLine> lines = new List<RomajiLine>();
        for (int i = 0; i < 300; i++)
        {
            lines.Add(Line("아주 긴 가사 한 줄입니다 " + i + " ", "그리고 또 하나의 줄이 계속됩니다 ", "끝"));
        }

        Run(api, Cache(), lines);

        foreach (JsonObject request in api.Requests)
        {
            Assert.True(LinesOf(request).Count <= ClaudeRomanizer.MaxLinesPerRequest);
            Assert.True(UserMessage(request).Length <= ClaudeRomanizer.MaxCharsPerRequest * 4, "the request stays a sensible size");
        }
    }

    private static string UserMessage(JsonObject request)
    {
        return request.Arr("messages")[0].Str("content");
    }

    [Fact]
    public void ARunWithoutAKeyOnAnEmptyCacheStillNeverContactsTheNetwork()
    {
        FakeApi api = new FakeApi(Xify);

        RomajiResult result = ClaudeRomanizer.Run(api.Client(), Cache(), Lines(2), null, Model, null, millis =>
        {
        });

        Assert.Empty(api.Requests);
        Assert.Equal(0, result.CachedLines);
        Assert.Equal(0, result.Requests);
        Assert.Equal("no Anthropic API key", result.StoppedBecause);
    }

    // ---- lines the first answer got wrong ----

    private static string EffortOf(JsonObject request)
    {
        return request.Child("output_config").Str("effort");
    }

    /// <summary>answers a request whose words are numbered ("1:가"): every piece with its number, as the system prompt asks</summary>
    private static JsonObject NumberedXify(JsonObject request)
    {
        OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
        foreach (JsonObject line in LinesOf(request))
        {
            List<string> pieces = new List<string>();
            foreach (JsonNode word in line.Arr("words"))
            {
                string text = word.GetValue<string>();
                int colon = text.IndexOf('=');
                pieces.Add(text.Substring(0, colon) + "=" + Regex.Replace(text.Substring(colon + 1), "[^\\x00-\\x7F]", "x"));
            }
            romaji[line.Int("n")] = string.Join("|", pieces);
        }
        return Answer(romaji);
    }

    [Fact]
    public void EveryLineSaysHowManyWordsItHasAndTheFirstAskDoesNotNumberThem()
    {
        FakeApi api = new FakeApi(Xify);
        List<RomajiLine> lines = new List<RomajiLine> { Line("今", "日", "も"), Line("Yeah ", "가 ", "나") };

        Run(api, Cache(), lines);

        List<JsonObject> sent = LinesOf(api.Requests[0]);
        Assert.Equal(3, sent[0].Int("count"));
        Assert.Equal(2, sent[1].Int("count")); // "Yeah" is copied, not asked
        Assert.Equal("今", sent[0].Arr("words")[0].GetValue<string>());
        Assert.Equal("medium", EffortOf(api.Requests[0]));
    }

    [Fact]
    public void AReadingOfSeveralCharactersMayBeWrittenOnTheFirstOneWithEmptyPiecesAfterIt()
    {
        List<RomajiLine> lines = new List<RomajiLine> { Line("今", "日", "も"), Line("明", "日", "は"), Line("大", "人", "に") };
        FakeApi api = new FakeApi(ByWords(new Dictionary<string, string>
        {
            ["今|日|も"] = "kyou||mo", ["明|日|は"] = "ashita||wa", ["大|人|に"] = "otona||ni"
        }));

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "kyou", "", "mo" }, result.Get(lines[0].Key));
        Assert.Equal(new List<string> { "ashita", "", "wa" }, result.Get(lines[1].Key));
        Assert.Equal(new List<string> { "otona", "", "ni" }, result.Get(lines[2].Key));
    }

    [Fact]
    public void AnEmptyPieceNeedsAReadingBeforeItAndANeedlessOneIsRefused()
    {
        RomajiLine line = Line("今", "日", "も");

        Assert.Null(ClaudeRomanizer.Usable(line, new List<string> { "", "kyou", "mo" })); // nothing before it to be the rest of
        Assert.Null(ClaudeRomanizer.Usable(Line("今 ", "Yeah ", "も"), new List<string> { "", "Yeah", "mo" }));
        Assert.Equal(new List<string> { "kyou", "", "mo" }, ClaudeRomanizer.Usable(line, new List<string> { "kyou", "", "mo" }));
        Assert.Null(ClaudeRomanizer.Usable(line, new List<string> { "kyou", "mo", "" })); // も is an ordinary kana: it has its own reading
        Assert.Null(ClaudeRomanizer.Usable(Line("私", "は", "走", "る"), new List<string> { "watashi", "", "hashi", "ru" })); // a dropped particle
        Assert.Null(ClaudeRomanizer.Usable(Line("사랑 ", "해 ", "요"), new List<string> { "sarang", "haeyo", "" })); // a Korean syllable has its own
        Assert.Null(ClaudeRomanizer.Usable(Line("가", "나"), new List<string> { "ga", "" })); // a line of two needs two readings
        Assert.Null(ClaudeRomanizer.Usable(Line("じ", "ゃ", "な", "い"), new List<string> { "ji", "", "na", "i" })); // a small kana has its own piece: "ji|" would lose the sound
        Assert.Equal(new List<string> { "ji", "ya", "na", "i" }, ClaudeRomanizer.Usable(Line("じ", "ゃ", "な", "い"), new List<string> { "ji", "ya", "na", "i" }));
        Assert.Null(ClaudeRomanizer.Usable(Line("甘", "酸", "っ", "ぱ", "い", "キ", "ャ", "ン"), new List<string> { "ama", "zu", "p", "", "", "", "", "" })); // stopped short
    }

    [Fact]
    public void APieceThatStartsWithTheNumberOfItsWordIsNeverTheRomajiOfAWord()
    {
        RomajiLine line = Line("今", "日", "も");

        Assert.Null(ClaudeRomanizer.Usable(line, new List<string> { "1=kyou", "2=", "3=mo" }));
        Assert.Equal(new List<string> { "kyou", "", "mo" }, ClaudeRomanizer.Usable(line, new List<string> { "kyou", "", "mo" }));
        Assert.Equal(new List<string> { "10:30", "yo" }, ClaudeRomanizer.Usable(Line("10:30 ", "요"), new List<string> { "10:30", "yo" })); // a time that is copied
    }

    [Fact]
    public void ALineAnsweredWrongIsAskedAgainWithItsWordsNumberedAndWhatWasWrong()
    {
        int[] calls = { 0 };
        FakeApi api = new FakeApi(request =>
        {
            if (++calls[0] == 1)
            {
                return Answer(Numbered((1, "ga0|na"), (2, "only one piece")));
            }
            return NumberedXify(request);
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("가0 ", "나"), Line("가1 ", "나", "다") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(2, api.Requests.Count);
        JsonObject again = LinesOf(api.Requests[1]).Single();
        Assert.Equal(new List<string> { "1=가1", "2=나", "3=다" }, again.Arr("words").Select(w => w.GetValue<string>()).ToList());
        Assert.Equal("only one piece", again.Str("previous"));
        Assert.Contains("1 pieces, but \"count\" is 3", again.Str("problem"));
        Assert.Equal("high", EffortOf(api.Requests[1]));
        Assert.Equal(new List<string> { "x1", "x", "x" }, result.Get(lines[1].Key));
        Assert.Null(result.StoppedBecause);
    }

    [Fact]
    public void ALineTheAnswerLeftOutIsAskedAgainWithoutAPreviousAnswer()
    {
        int[] calls = { 0 };
        FakeApi api = new FakeApi(request =>
        {
            if (++calls[0] == 1)
            {
                return Answer(Numbered((1, "ga0|na")));
            }
            return NumberedXify(request);
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("가0 ", "나"), Line("가1 ", "나") };

        RomajiResult result = Run(api, Cache(), lines);

        JsonObject again = LinesOf(api.Requests[1]).Single();
        Assert.False(again.AsObject().ContainsKey("previous"));
        Assert.Contains("missing from your answer", again.Str("problem"));
        Assert.NotNull(result.Get(lines[1].Key));
    }

    [Fact]
    public void ALoneLineTheFirstAnswerGotWrongIsStillRepairedAndReportedWhenNothingHelps()
    {
        // the usual rerun: everything else is saved, only the hard line is asked, and it has to get the same tries
        FakeApi api = new FakeApi(request => Answer(Numbered((1, "kyou|mo")))); // one piece too few, every time
        List<RomajiLine> lines = new List<RomajiLine> { Line("今", "日", "も") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[0].Key));
        Assert.Equal(3, api.Requests.Count);
        Assert.Equal("Claude's answers could not be used for 1 line after 3 tries", result.StoppedBecause);
    }

    [Fact]
    public void ALoneHardLineIsRepairedByTheSecondAskWithItsWordsNumbered()
    {
        int[] calls = { 0 };
        FakeApi api = new FakeApi(request => ++calls[0] == 1 ? Answer(Numbered((1, "kyou|mo"))) : Answer(Numbered((1, "1=kyou|2=|3=mo"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("今", "日", "も") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "kyou", "", "mo" }, result.Get(lines[0].Key));
        Assert.Equal(2, api.Requests.Count);
        Assert.Null(result.StoppedBecause);
    }

    [Fact]
    public void APieceKeepsNoNumberWhenTheNumberingOfAnAnswerIsBrokenAndTheLineIsRefusedInstead()
    {
        // line 1 is right, so the others are asked again; their numbering slips in every way a numbering can
        string[] repair = { "1=kyou|1=x|3=mo", "1=kyou|2=|mo", "0=kyou|1=|2=mo", "1=kyou|2=|3=mo|4=x" };
        int[] calls = { 0 };
        FakeApi api = new FakeApi(request =>
        {
            if (++calls[0] == 1)
            {
                return Answer(Numbered((1, "ga0|na"), (2, "a|b"), (3, "a|b"), (4, "a|b"), (5, "a|b")));
            }
            OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
            foreach (JsonObject line in LinesOf(request))
            {
                romaji[line.Int("n")] = repair[(line.Int("n") - 1) % repair.Length];
            }
            return Answer(romaji);
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("가0 ", "나") };
        for (int i = 0; i < 4; i++)
        {
            lines.Add(Line("今", "日", "も"));
        }
        lines = lines.GroupBy(line => line.Key).Select(group => group.First()).ToList();
        lines.AddRange(new[] { Line("今", "日", "は"), Line("明", "日", "は"), Line("今", "日", "に"), Line("今", "日", "が") });

        RomajiResult result = Run(api, Cache(), lines);

        foreach (RomajiLine line in lines.Skip(1))
        {
            List<string> got = result.Get(line.Key);
            Assert.True(got == null || got.All(piece => !piece.Contains('=')), "a numbered piece was kept: " + string.Join("|", got ?? new List<string>()));
        }
        // the refusal says what to write instead
        Assert.Contains(api.Requests.SelectMany(LinesOf).Where(line => line.ContainsKey("problem")),
                line => line.Str("problem").Contains("number=piece"));
    }

    [Fact]
    public void ANumberedAnswerThatStopsShortIsNotAnAnswer()
    {
        // 甘 酸 っ ぱ い キ ャ ン: the answer stops after three words, and the left-out numbers would be empty pieces
        int[] calls = { 0 };
        FakeApi api = new FakeApi(request => ++calls[0] == 1
                ? Answer(Numbered((1, "ga0|na"), (2, "x|y")))
                : Answer(Numbered((1, "1=ama|2=zu|3=p"))));
        List<RomajiLine> lines = new List<RomajiLine> { Line("가0 ", "나"), Line("甘", "酸", "っ", "ぱ", "い", "キ", "ャ", "ン") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[1].Key)); // the ordinary kana ぱ い ン have no piece of their own: the line is refused
    }

    [Fact]
    public void ARepairAnswerMayLeaveOutTheNumberOfAnEmptyPieceOrIgnoreTheNumbersAltogether()
    {
        int[] calls = { 0 };
        FakeApi api = new FakeApi(request =>
        {
            calls[0]++;
            if (calls[0] == 1)
            {
                // one line is answered well, so the others are asked again; those are wrong
                return Answer(Numbered((1, "ga0|na"), (2, "kyou|mo"), (3, "ichi|ni")));
            }
            return Answer(Numbered((1, "1=kyou|3=mo"), (2, "ichi||ni")));
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("가0 ", "나"), Line("今", "日", "も"), Line("一", "日", "に") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "kyou", "", "mo" }, result.Get(lines[1].Key)); // the left-out number is an empty piece
        Assert.Equal(new List<string> { "ichi", "", "ni" }, result.Get(lines[2].Key)); // read by position
    }

    [Fact]
    public void LinesThatStayWrongAreAskedOnceMoreInSmallRequestsAndThenGivenUpOn()
    {
        Dictionary<string, int> asks = new Dictionary<string, int>();
        FakeApi api = new FakeApi(request =>
        {
            OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
            foreach (JsonObject line in LinesOf(request))
            {
                string first = line.Arr("words")[0].GetValue<string>();
                string key = first.Substring(first.IndexOf('=') + 1);
                asks[key] = asks.GetValueOrDefault(key) + 1;
                bool easy = key == "가0";
                bool right = easy || asks[key] >= 3;
                romaji[line.Int("n")] = right
                        ? string.Join("|", line.Arr("words").Select((w, k) => (asks[key] > 1 ? (k + 1) + "=" : "") + "x"))
                        : "wrong";
            }
            return Answer(romaji);
        });
        List<RomajiLine> lines = new List<RomajiLine>();
        for (int i = 0; i < 20; i++)
        {
            lines.Add(Line("가" + i + " ", "나"));
        }

        RomajiResult result = Run(api, Cache(), lines);

        // 20 lines in one request, the 19 wrong ones in one request (40 at most), then in requests of 8: 8, 8, 3
        Assert.Equal(new List<int> { 20, 19, 8, 8, 3 }, api.Requests.Select(r => LinesOf(r).Count).ToList());
        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
        Assert.Null(result.StoppedBecause);
    }

    [Fact]
    public void TheLastTryKeepsTheReadingOfAnAnswerWhoseCountStaysWrongAndPutsItOnTheFirstWords()
    {
        ConcurrentApi api = new ConcurrentApi(request =>
        {
            OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
            foreach (JsonObject line in LinesOf(request))
            {
                bool fine = line.Arr("words")[0].GetValue<string>().EndsWith("가", StringComparison.Ordinal);
                romaji[line.Int("n")] = fine
                        ? string.Join("|", line.Arr("words").Select(w => "x"))
                        : "kyou|mo"; // always one piece too few
            }
            return Answer(romaji);
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("こ", "今", "日"), Line("가", "나") };

        RomajiResult result = RunWith(api, 1, lines);

        // 2 pieces for 3 words: the reading is kept as it was written, and the last word (a kanji) shares it
        Assert.Equal(new List<string> { "kyou", "mo", "" }, result.Get(lines[0].Key));
        Assert.Null(result.StoppedBecause);
    }

    [Fact]
    public void TheLastTryNeverKeepsAnAnswerThatIsFarOffOrWouldDropAWord()
    {
        ConcurrentApi api = new ConcurrentApi(request =>
        {
            OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
            foreach (JsonObject line in LinesOf(request))
            {
                bool fine = line.Arr("words")[0].GetValue<string>().EndsWith("가", StringComparison.Ordinal);
                romaji[line.Int("n")] = fine ? string.Join("|", line.Arr("words").Select(w => "x")) : "hitori|janai"; // 2 pieces
            }
            return Answer(romaji);
        });
        // twelve words, two pieces: not a reading that can be put on the words; three words, two pieces, but the one left
        // without a piece is an ordinary kana
        List<RomajiLine> lines = new List<RomajiLine>
        {
            Line("一", "人", "じ", "ゃ", "進", "め", "な", "い", "よ", "う", "だ", "ね"), Line("あ", "い", "う"), Line("가", "나")
        };

        RomajiResult result = RunWith(api, 1, lines);

        Assert.Null(result.Get(lines[0].Key));
        Assert.Null(result.Get(lines[1].Key));
        Assert.Contains("could not be used for 2 lines", result.StoppedBecause);
    }

    [Fact]
    public void ALineNoTryGetsRightIsReportedAndLeftOut()
    {
        ConcurrentApi api = new ConcurrentApi(request =>
        {
            OrderedDictionary<int, string> romaji = new OrderedDictionary<int, string>();
            foreach (JsonObject line in LinesOf(request))
            {
                bool fine = line.Arr("words")[0].GetValue<string>().EndsWith("가", StringComparison.Ordinal);
                romaji[line.Int("n")] = fine ? string.Join("|", line.Arr("words").Select(w => "x")) : "kyoumo"; // one piece for three words
            }
            return Answer(romaji);
        });
        List<RomajiLine> lines = new List<RomajiLine> { Line("今", "日", "も"), Line("가", "나") };

        RomajiResult result = RunWith(api, 1, lines);

        Assert.Null(result.Get(lines[0].Key));
        Assert.NotNull(result.Get(lines[1].Key));
        Assert.Equal("Claude's answers could not be used for 1 line after 3 tries", result.StoppedBecause);
        Assert.Equal(3, api.Requests.Count);
    }

    // ---- what the review found ----

    [Fact]
    public void AProlongedSoundMarkInAJapaneseLineIsAskedAboutAndNeverCopied()
    {
        List<RomajiLine> lines = new List<RomajiLine> { Line("そ", "う", "ー") };
        FakeApi good = new FakeApi(ByWords(new Dictionary<string, string> { ["そ|う|ー"] = "so|u|u" }));
        FakeApi copied = new FakeApi(ByWords(new Dictionary<string, string> { ["そ|う|ー"] = "so|u|ー" }));

        RomajiResult asked = Run(good, Cache(), lines);
        RomajiResult refused = Run(copied, new RomajiCache(Path.Combine(_temp, "other.json")), lines); // (the first answer is saved in the other cache)

        Assert.Equal(new List<string> { "そ", "う", "ー" }, LinesOf(good.Requests[0])[0].Arr("words").Select(w => w.GetValue<string>()).ToList());
        Assert.Equal(new List<string> { "so", "u", "u" }, asked.Get(lines[0].Key));
        Assert.Null(refused.Get(lines[0].Key));
    }

    [Fact]
    public void AProlongedSoundMarkLeftInAJapaneseLinesRomajiIsNotUsable()
    {
        RomajiLine line = Line("そ", "ー");

        Assert.Null(ClaudeRomanizer.Usable(line, new List<string> { "so", "ー" }));
        Assert.Null(ClaudeRomanizer.Usable(line, new List<string> { "so", "\U0000FF70" }));
        Assert.Equal(new List<string> { "so", "o" }, ClaudeRomanizer.Usable(line, new List<string> { "so", "o" }));
    }

    [Fact]
    public void EveryKanjiWordOfAJapaneseLineIsAskedAbout()
    {
        FakeApi api = new FakeApi(Xify);
        RomajiLine line = Line("真", "面", "目", "そ", "う", "で", "も");

        Run(api, Cache(), new List<RomajiLine> { line });

        Assert.Equal(7, LinesOf(api.Requests[0])[0].Arr("words").Count);
    }

    [Fact]
    public void ABarInsideAWordIsSentAsASlashSoThePiecesStayCountable()
    {
        FakeApi api = new FakeApi(Xify);
        List<RomajiLine> lines = new List<RomajiLine> { Line("가|나 ", "다") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal("가/나", LinesOf(api.Requests[0])[0].Arr("words")[0].GetValue<string>());
        Assert.Equal(new List<string> { "x/x", "x" }, result.Get(lines[0].Key));
    }

    [Fact]
    public void AnInvisibleFillerWordIsNotAskedAboutAndStaysEmpty()
    {
        FakeApi api = new FakeApi(Xify);
        List<RomajiLine> lines = new List<RomajiLine> { Line("가 ", "\U00003164", "나") };

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(2, LinesOf(api.Requests[0])[0].Arr("words").Count);
        Assert.Equal(new List<string> { "x", "", "x" }, result.Get(lines[0].Key));
    }

    [Fact]
    public void ASmallTsuWithNoSoundAfterItMayBeAnApostropheOrNothing()
    {
        List<RomajiLine> lines = new List<RomajiLine> { Line("い", "っ"), Line("あ", "っ", "い") };
        FakeApi api = new FakeApi(ByWords(new Dictionary<string, string> { ["い|っ"] = "i|'", ["あ|っ|い"] = "a||i" }));

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Equal(new List<string> { "i", "'" }, result.Get(lines[0].Key));
        Assert.Equal(new List<string> { "a", "", "i" }, result.Get(lines[1].Key));
    }

    [Fact]
    public void ALineNeedsTwoWordsWithAReadingToBeReadBackAsRomaji()
    {
        // with the empty pieces dropped when it is written, one word would be left, and a romaji line has at least two
        Assert.Null(ClaudeRomanizer.Usable(Line("あ", "っ"), new List<string> { "a", "" }));
        Assert.Null(ClaudeRomanizer.Usable(Line("今", "日"), new List<string> { "kyou", "" }));
        Assert.Equal(new List<string> { "kyou", "", "mo" }, ClaudeRomanizer.Usable(Line("今", "日", "も"), new List<string> { "kyou", "", "mo" }));
    }

    [Fact]
    public void APieceThatIsOnlyASymbolIsNotAnAnswerForAWord()
    {
        List<RomajiLine> lines = new List<RomajiLine> { Line("가 ", "나") };
        FakeApi api = new FakeApi(ByWords(new Dictionary<string, string> { ["가|나"] = "ga|-" }));

        RomajiResult result = Run(api, Cache(), lines);

        Assert.Null(result.Get(lines[0].Key));
    }

    [Fact]
    public void TheAnswerMayComeAsAnArrayOfLines()
    {
        FakeApi api = new FakeApi(request =>
        {
            JsonObject answer = new JsonObject();
            JsonArray rows = new JsonArray();
            foreach (string row in Xify(request).Str("romaji").Split('\n'))
            {
                rows.Add(JsonValue.Create(row));
            }
            answer["romaji"] = rows;
            return answer;
        });
        List<RomajiLine> lines = Lines(3);

        RomajiResult result = Run(api, Cache(), lines);

        foreach (RomajiLine line in lines)
        {
            Assert.NotNull(result.Get(line.Key));
        }
    }

    [Fact]
    public void WhenTwoBigRequestsGiveNothingReadableTheRestIsNotSent()
    {
        ConcurrentApi api = new ConcurrentApi(request => Answer(new Dictionary<int, string>()));

        RomajiResult result = RunWith(api, 1, Lines(500)); // 200, 200, 100 lines

        Assert.Equal(2, api.Requests.Count);
        Assert.Contains("could not be read", result.StoppedBecause);
    }

    [Fact]
    public void ARetryWaitsOutItsPauseBeforeTheLinesAreAskedAgain()
    {
        // every attempt of the first three requests (four tries each) is turned away; the retries come after the pauses
        List<string> events = new List<string>();
        int[] posts = { 0 };
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            lock (events)
            {
                if (++posts[0] <= 12)
                {
                    events.Add("rejected");
                    return new AnthropicClient.Response(429, "{}");
                }
                events.Add("post");
            }
            return ToolResponse(ClaudeRomanizer.ToolName, Xify(Obj(body)));
        }, millis =>
        {
        });

        RomajiResult result = ClaudeRomanizer.Run(client, Cache(), Lines(500), Key, Model, null, millis =>
        {
            lock (events)
            {
                events.Add("pause");
            }
        }, 3);

        Assert.Null(result.StoppedBecause);
        int firstPost = events.IndexOf("post");
        Assert.True(firstPost >= 0);
        Assert.True(events.Take(firstPost).Count(e => e == "pause") >= (int)(ClaudeRomanizer.RetryPauseMillis / ClaudeRomanizer.PauseSliceMillis),
                "the first retry came after a whole pause");
    }

    [Fact]
    public void ARunThatIsOverDoesNotWaitOutThePauseOfARetry()
    {
        // the first request keeps being turned away (it pauses before its retry), the second one's key is rejected a moment later
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) =>
        {
            JsonObject request = Obj(body);
            if (UserMessage(request).Contains("\"가0\""))
            {
                return new AnthropicClient.Response(503, "{}");
            }
            Thread.Sleep(150);
            return new AnthropicClient.Response(401, "{}");
        }, millis =>
        {
        });
        int[] slices = { 0 };
        Stopwatch watch = Stopwatch.StartNew();

        RomajiResult result = ClaudeRomanizer.Run(client, Cache(), Lines(400), Key, Model, null, millis =>
        {
            Interlocked.Increment(ref slices[0]);
            Thread.Sleep((int)(millis / 10)); // a tenth of the real pause: the whole of it would take two seconds
        }, 2);

        Assert.Contains("API key was rejected", result.StoppedBecause);
        Assert.True(slices[0] < 15, "the pause was cut short: " + slices[0] + " slices");
        Assert.True(watch.ElapsedMilliseconds < 1500, "the run ended at once: " + watch.ElapsedMilliseconds + " ms");
    }
}
