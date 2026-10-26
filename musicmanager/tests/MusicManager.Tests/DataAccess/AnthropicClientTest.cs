using System.Net.Sockets;
using System.Text.Json.Nodes;
using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;
using static MusicManager.Tests.DataAccess.ClaudeTestSupport;

namespace MusicManager.Tests.DataAccess;

public class AnthropicClientTest
{
    private const string Key = "sk-ant-api03-TEST-KEY-abcdefghijklmnop";
    private const string Model = "claude-test-model";
    private const string SystemPrompt = "system prompt text";
    private const string UserSecret = "PRIVATE-USER-DATA-4711";
    private const string UserMessage = "please parse " + UserSecret;
    private const string Tool = "record_things";

    /// <summary>replays scripted replies (a Response, or an IOException to throw) and records every request</summary>
    private class FakeTransport
    {
        public sealed record Call(string Url, IDictionary<string, string> Headers, string Body, TimeSpan Timeout);

        public readonly List<Call> Calls = new List<Call>();
        private readonly Queue<object> _script = new Queue<object>();

        public FakeTransport Reply(AnthropicClient.Response response)
        {
            _script.Enqueue(response);
            return this;
        }

        public FakeTransport Reply(int status, string body)
        {
            return Reply(new AnthropicClient.Response(status, body));
        }

        public FakeTransport ReplyOk(JsonObject toolInput)
        {
            return Reply(200, ToolUseBody(Tool, toolInput, "tool_use"));
        }

        public FakeTransport Fail(IOException error)
        {
            _script.Enqueue(error);
            return this;
        }

        public AnthropicClient.Response Post(string url, IDictionary<string, string> headers, string body, TimeSpan timeout)
        {
            Calls.Add(new Call(url, headers, body, timeout));
            if (!_script.TryDequeue(out object next))
            {
                Assert.Fail("more requests than scripted replies");
            }
            if (next is IOException error)
            {
                throw error;
            }
            return (AnthropicClient.Response)next;
        }
    }

    private readonly List<long> _sleeps = new List<long>();

    private AnthropicClient ClientFor(FakeTransport transport)
    {
        return new AnthropicClient(transport.Post, _sleeps.Add);
    }

    private static JsonObject Schema()
    {
        return Obj("{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"}}}");
    }

    private JsonObject Call(AnthropicClient client)
    {
        return client.CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "records things", Schema(), 4096);
    }

    private static string ToolUseBody(string name, JsonObject input, string stopReason)
    {
        JsonObject block = new JsonObject();
        block["type"] = "tool_use";
        block["id"] = "toolu_01";
        block["name"] = name;
        block["input"] = input;
        JsonArray content = new JsonArray();
        content.Add(block);
        JsonObject message = new JsonObject();
        message["id"] = "msg_01";
        message["type"] = "message";
        message["role"] = "assistant";
        message["content"] = content;
        message["stop_reason"] = stopReason;
        return message.ToJsonString();
    }

    private static ArtistAiException Failure(Action action)
    {
        return Assert.Throws<ArtistAiException>(action);
    }

    /// <summary>whatever went wrong, nothing private may leak into the exception</summary>
    private static void AssertNoLeak(ArtistAiException e)
    {
        for (Exception t = e; t != null; t = t.InnerException)
        {
            string message = t.Message;
            Assert.False(message.Contains(Key), "key leaked: " + message);
            Assert.False(message.Contains(UserSecret), "user data leaked: " + message);
            Assert.False(message.Contains(SystemPrompt), "system prompt leaked: " + message);
        }
    }

    // ---- request shape ----

    [Fact]
    public void SendsTheDocumentedRequest()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{\"x\":\"y\"}"));

        Call(ClientFor(transport));

        Assert.Single(transport.Calls);
        FakeTransport.Call sent = transport.Calls[0];
        Assert.Equal("https://api.anthropic.com/v1/messages", sent.Url);
        Assert.Equal(Key, sent.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", sent.Headers["anthropic-version"]);
        Assert.Equal("application/json", sent.Headers["content-type"]);
        Assert.Equal(TimeSpan.FromSeconds(300), sent.Timeout);

        JsonObject body = Obj(sent.Body);
        // exactly these fields: also proves temperature/top_p/top_k are not sent
        Assert.Equal(new List<string> { "max_tokens", "messages", "model", "system", "tool_choice", "tools" },
                body.Select(property => property.Key).OrderBy(name => name, StringComparer.Ordinal).ToList());
        Assert.Equal(Model, body.Str("model"));
        Assert.Equal(4096, body.Int("max_tokens"));
        Assert.Equal(SystemPrompt, body.SystemText());

        JsonArray messages = body.Arr("messages");
        Assert.Single(messages);
        Assert.Equal("user", messages[0].Str("role"));
        Assert.Equal(UserMessage, messages[0].Str("content"));

        JsonArray tools = body.Arr("tools");
        Assert.Single(tools);
        JsonObject tool = tools[0].AsObject();
        Assert.Equal(Tool, tool.Str("name"));
        Assert.Equal("records things", tool.Str("description"));
        AssertJsonEqual(Schema(), tool["input_schema"]);

        // the tool is offered, never forced: a forced tool_choice is a 400 on current models
        JsonObject toolChoice = body.Child("tool_choice");
        Assert.Equal("auto", toolChoice.Str("type"));
        Assert.False(toolChoice.ContainsKey("name"));
    }

    [Fact]
    public void EffortGoesInOutputConfigOnlyWhenAsked()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{}")).ReplyOk(Obj("{}"));

        Call(ClientFor(transport));
        ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "records things", Schema(), 4096,
                new AnthropicClient.Options("low", 0));

        Assert.False(Obj(transport.Calls[0].Body).ContainsKey("output_config"));
        Assert.Equal("low", Obj(transport.Calls[1].Body).Child("output_config").Str("effort"));
    }

    [Fact]
    public void WebSearchIsOfferedAsASecondToolWithTheRightVersionForTheModel()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{}")).ReplyOk(Obj("{}"));

        ClientFor(transport).CallTool(Key, "claude-sonnet-5-5", SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 5));
        ClientFor(transport).CallTool(Key, "claude-haiku-5-5", SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 3));

        JsonArray sonnetTools = Obj(transport.Calls[0].Body).Arr("tools");
        Assert.Equal(2, sonnetTools.Count);
        Assert.Equal(Tool, sonnetTools[0].Str("name"));
        Assert.Equal("web_search_20260209", sonnetTools[1].Str("type"));
        Assert.Equal("web_search", sonnetTools[1].Str("name"));
        Assert.Equal(5, sonnetTools[1].Int("max_uses"));

        JsonObject haikuSearch = Obj(transport.Calls[1].Body).Arr("tools")[1].AsObject();
        Assert.Equal("web_search_20250305", haikuSearch.Str("type"));
        Assert.Equal(3, haikuSearch.Int("max_uses"));
    }

    [Fact]
    public void TheSystemPromptIsOneBlockThatMayBeCachedSoARunReadsItAtATenthOfThePrice()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{}"));

        Call(ClientFor(transport));

        JsonArray system = Obj(transport.Calls[0].Body).Arr("system");
        Assert.Single(system);
        Assert.Equal("text", system[0].Str("type"));
        Assert.Equal(SystemPrompt, system[0].Str("text"));
        Assert.Equal("ephemeral", system[0].Child("cache_control").Str("type"));
        // nothing that differs from one request to the next is in the block: the lines go in the user message
        Assert.False(system[0].Str("text").Contains(UserSecret));
    }

    private static string SearchedBody(JsonObject toolInput)
    {
        JsonObject search = new JsonObject();
        search["type"] = "server_tool_use";
        search["name"] = "web_search";
        JsonObject tool = new JsonObject();
        tool["type"] = "tool_use";
        tool["name"] = Tool;
        tool["input"] = toolInput;
        JsonArray content = new JsonArray();
        content.Add(search);
        content.Add(tool);
        JsonObject message = new JsonObject();
        message["content"] = content;
        message["stop_reason"] = "tool_use";
        return message.ToJsonString();
    }

    [Fact]
    public void AnAnswerThatHadToBeSearchedForIsAskedAgainOnceWhenItCameFromMemory()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{\"from\":\"memory\"}")).Reply(200, SearchedBody(Obj("{\"from\":\"search\"}")));

        JsonObject result = ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 10, true));

        AssertJsonEqual(Obj("{\"from\":\"search\"}"), result);
        Assert.Equal(2, transport.Calls.Count);
        JsonArray again = Obj(transport.Calls[1].Body).Arr("messages");
        Assert.Single(again); // a fresh ask, not a continuation of the answer from memory
        Assert.StartsWith(UserMessage, again[0].Str("content"));
        Assert.Contains("without searching the web", again[0].Str("content"));
    }

    [Fact]
    public void AnAnswerThatDidSearchIsTakenAsItIs()
    {
        FakeTransport transport = new FakeTransport().Reply(200, SearchedBody(Obj("{\"ok\":1}")));

        JsonObject result = ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 10, true));

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.Single(transport.Calls);
    }

    [Fact]
    public void ASearchCountedInTheUsageIsASearchToo()
    {
        string body = "{\"content\":[{\"type\":\"tool_use\",\"name\":\"" + Tool + "\",\"input\":{\"ok\":1}}],\"stop_reason\":\"tool_use\","
                + "\"usage\":{\"server_tool_use\":{\"web_search_requests\":3}}}";
        FakeTransport transport = new FakeTransport().Reply(200, body);

        ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 10, true));

        Assert.Single(transport.Calls);
    }

    [Fact]
    public void AnAnswerFromMemoryIsKeptWhenTheSecondAskDoesNotSearchEitherAndNotAskedAThirdTime()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{\"n\":1}")).ReplyOk(Obj("{\"n\":2}"));

        JsonObject result = ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 10, true));

        AssertJsonEqual(Obj("{\"n\":2}"), result);
        Assert.Equal(2, transport.Calls.Count);
    }

    [Fact]
    public void NothingIsRequiredWhenSearchIsOffOrNotRequired()
    {
        FakeTransport off = new FakeTransport().ReplyOk(Obj("{\"ok\":1}"));
        ClientFor(off).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096, new AnthropicClient.Options(null, 0, true));
        Assert.Single(off.Calls); // no search tool was offered, so none can be demanded

        FakeTransport optional = new FakeTransport().ReplyOk(Obj("{\"ok\":1}"));
        ClientFor(optional).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096, new AnthropicClient.Options(null, 10));
        Assert.Single(optional.Calls);
    }

    [Fact]
    public void AProseAnswerIsAskedOnceMoreToCallTheTool()
    {
        string prose = "{\"content\":[{\"type\":\"text\",\"text\":\"Here you go\"}],\"stop_reason\":\"end_turn\"}";
        FakeTransport transport = new FakeTransport().Reply(200, prose).ReplyOk(Obj("{\"ok\":1}"));

        JsonObject result = Call(ClientFor(transport));

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.Equal(2, transport.Calls.Count);
        JsonArray messages = Obj(transport.Calls[1].Body).Arr("messages");
        Assert.Equal(3, messages.Count);
        Assert.Equal("assistant", messages[1].Str("role"));
        Assert.Equal("user", messages[2].Str("role"));
        Assert.Contains(Tool, messages[2].Arr("content")[0].Str("text"));
        Assert.Equal("ephemeral", messages[2].Arr("content")[0].Child("cache_control").Str("type")); // everything so far is cached
    }

    [Fact]
    public void AContinuationCachesEverythingSaidSoFar()
    {
        string paused = "{\"content\":[{\"type\":\"server_tool_use\",\"id\":\"s\",\"name\":\"web_search\",\"input\":{}}],"
                + "\"stop_reason\":\"pause_turn\"}";
        FakeTransport transport = new FakeTransport().Reply(200, paused).ReplyOk(Obj("{\"ok\":1}"));

        ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 4));

        JsonObject first = Obj(transport.Calls[0].Body);
        Assert.Equal(UserMessage, first.Arr("messages")[0].Str("content")); // the first request has the system block as its breakpoint
        JsonArray messages = Obj(transport.Calls[1].Body).Arr("messages");
        Assert.Equal("ephemeral", messages[1].Arr("content")[0].Child("cache_control").Str("type"));
        Assert.Equal(UserMessage, messages[0].Str("content"));
    }

    [Fact]
    public void OnlyOneCallToTheToolIsAllowed()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{}"));

        Call(ClientFor(transport));

        JsonObject toolChoice = Obj(transport.Calls[0].Body).Child("tool_choice");
        Assert.Equal("auto", toolChoice.Str("type"));
        Assert.True(toolChoice.Bool("disable_parallel_tool_use"));
    }

    [Fact]
    public void ItTellsWhetherTheAnswerCameAfterASearch()
    {
        JsonObject searched = Obj("{\"content\":[{\"type\":\"server_tool_use\",\"name\":\"web_search\"},"
                + "{\"type\":\"tool_use\",\"name\":\"" + Tool + "\",\"input\":{\"ok\":1}}],\"stop_reason\":\"tool_use\"}");
        FakeTransport withSearch = new FakeTransport().Reply(200, searched.ToJsonString());
        FakeTransport without = new FakeTransport().ReplyOk(Obj("{\"ok\":1}"));

        ClientFor(withSearch).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 4), out bool afterSearch);
        ClientFor(without).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 4), out bool fromMemory);

        Assert.True(afterSearch);
        Assert.False(fromMemory);
    }

    [Fact]
    public void AnUnsearchedAnswerInTheLastRoundIsUsedInsteadOfBeingThrownAway()
    {
        string prose = "{\"content\":[{\"type\":\"text\",\"text\":\"hm\"}],\"stop_reason\":\"end_turn\"}";
        FakeTransport transport = new FakeTransport();
        for (int i = 0; i < 7; i++)
        {
            transport.Reply(200, prose); // seven rounds of prose, each nudged to call the tool
        }
        transport.ReplyOk(Obj("{\"ok\":1}")); // and the eighth, the last round, answers from memory

        JsonObject result = ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 4, true), out bool searched);

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.False(searched);
        Assert.Equal(8, transport.Calls.Count); // no ninth request to ask for a search
    }

    [Theory]
    [InlineData("\"usage\":\"none\"")]
    [InlineData("\"usage\":{\"server_tool_use\":3}")]
    [InlineData("\"usage\":{\"server_tool_use\":{\"web_search_requests\":\"many\"}}")]
    [InlineData("\"usage\":[1,2]")]
    public void ADamagedUsageFieldIsNotAnError(string usage)
    {
        string body = "{" + usage + ",\"content\":[{\"type\":\"tool_use\",\"name\":\"" + Tool + "\",\"input\":{\"ok\":1}}],"
                + "\"stop_reason\":\"tool_use\"}";

        JsonObject result = ClientFor(new FakeTransport().Reply(200, body)).CallTool(Key, Model, SystemPrompt, UserMessage, Tool,
                "d", Schema(), 4096, new AnthropicClient.Options(null, 4), out bool searched);

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.False(searched);
    }

    [Fact]
    public void ARequestThatSearchesTheWebIsNotSentAgainWhenItTimesOut()
    {
        FakeTransport transport = new FakeTransport().Fail(new AnthropicClient.RequestTimedOutException("The request timed out", null))
                .ReplyOk(Obj("{}"));

        ArtistAiException e = Failure(() => ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d",
                Schema(), 4096, new AnthropicClient.Options(null, 10)));

        Assert.Contains("timed out", e.Message);
        Assert.Single(transport.Calls); // sending it again would be paid twice
        Assert.Empty(_sleeps);
        AssertNoLeak(e);
    }

    [Fact]
    public void ARequestWithoutASearchIsStillSentAgainAfterATimeout()
    {
        FakeTransport transport = new FakeTransport().Fail(new AnthropicClient.RequestTimedOutException("The request timed out", null))
                .ReplyOk(Obj("{\"ok\":1}"));

        JsonObject result = Call(ClientFor(transport));

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.Equal(2, transport.Calls.Count);
    }

    [Fact]
    public void WaitsAreSpreadOutByTheJitterButNeverPastTheCap()
    {
        List<long> sleeps = new List<long>();
        FakeTransport transport = new FakeTransport().Reply(429, "{}").Reply(429, "{}").Reply(429, "{}").ReplyOk(Obj("{}"));
        AnthropicClient client = new AnthropicClient(transport.Post, sleeps.Add, () => 1.0);

        client.CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096);

        Assert.Equal(new List<long> { 1250L, 2500L, 5000L }, sleeps);
        // and a long wait asked for by the server stays under the cap
        sleeps.Clear();
        FakeTransport asked = new FakeTransport().Reply(new AnthropicClient.Response(429, "{}", "30")).ReplyOk(Obj("{}"));
        new AnthropicClient(asked.Post, sleeps.Add, () => 1.0).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096);
        Assert.Equal(new List<long> { 30_000L }, sleeps);
    }

    [Fact]
    public void APausedSearchTurnIsContinuedWithoutExtraWords()
    {
        string paused = "{\"content\":[{\"type\":\"server_tool_use\",\"id\":\"s\",\"name\":\"web_search\",\"input\":{}}],"
                + "\"stop_reason\":\"pause_turn\"}";
        FakeTransport transport = new FakeTransport().Reply(200, paused).ReplyOk(Obj("{\"ok\":1}"));

        JsonObject result = ClientFor(transport).CallTool(Key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 4096,
                new AnthropicClient.Options(null, 4));

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        JsonArray messages = Obj(transport.Calls[1].Body).Arr("messages");
        Assert.Equal(2, messages.Count); // the user message, then the paused assistant turn handed back as it was
        Assert.Equal("assistant", messages[1].Str("role"));
        Assert.Equal("server_tool_use", messages[1].Arr("content")[0].Str("type"));
    }

    [Fact]
    public void KeepsAskingNoMoreThanAFewRoundsWhenTheModelNeverCallsTheTool()
    {
        string prose = "{\"content\":[{\"type\":\"text\",\"text\":\"no\"}],\"stop_reason\":\"end_turn\"}";
        FakeTransport transport = new FakeTransport().Reply(200, prose).Reply(200, prose).Reply(200, prose)
                .Reply(200, prose);

        ArtistAiException e = Assert.Throws<ArtistAiException>(() => Call(ClientFor(transport)));

        Assert.Equal("Unexpected response from the API", e.Message);
        Assert.Equal(3, transport.Calls.Count);
    }

    [Fact]
    public void NeverSendsSamplingParameters()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{}"));

        Call(ClientFor(transport));

        string body = transport.Calls[0].Body;
        Assert.False(body.Contains("temperature"));
        Assert.False(body.Contains("top_p"));
        Assert.False(body.Contains("top_k"));
    }

    [Fact]
    public void TheKeyGoesInTheHeaderOnly()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{}"));

        Call(ClientFor(transport));

        Assert.False(transport.Calls[0].Body.Contains(Key));
    }

    [Fact]
    public void ReturnsTheInputOfTheToolUseBlockForTheRequestedTool()
    {
        string body = """
                {"content":[
                  {"type":"text","text":"Sure."},
                  {"type":"tool_use","id":"a","name":"some_other_tool","input":{"wrong":true}},
                  {"type":"tool_use","id":"b","name":"record_things","input":{"right":[1,2,3]}},
                  {"type":"tool_use","id":"c","name":"record_things","input":{"second":true}}
                ],"stop_reason":"tool_use"}
                """;
        FakeTransport transport = new FakeTransport().Reply(200, body);

        JsonObject result = Call(ClientFor(transport));

        AssertJsonEqual(Obj("{\"right\":[1,2,3]}"), result);
    }

    [Fact]
    public void TolerantOfExtraFieldsAndUnknownStopReasons()
    {
        string body = """
                {"id":"msg","type":"message","model":"m","usage":{"input_tokens":1},"future_field":{"a":1},
                 "content":[{"type":"tool_use","name":"record_things","input":{"ok":1},"extra":null}],
                 "stop_reason":"something_new"}
                """;

        AssertJsonEqual(Obj("{\"ok\":1}"), Call(ClientFor(new FakeTransport().Reply(200, body))));
    }

    [Fact]
    public void MissingOrNullStopReasonIsFine()
    {
        string missing = "{\"content\":[{\"type\":\"tool_use\",\"name\":\"record_things\",\"input\":{\"ok\":1}}]}";
        string jsonNull = "{\"content\":[{\"type\":\"tool_use\",\"name\":\"record_things\",\"input\":{\"ok\":1}}],"
                + "\"stop_reason\":null}";

        AssertJsonEqual(Obj("{\"ok\":1}"), Call(ClientFor(new FakeTransport().Reply(200, missing))));
        AssertJsonEqual(Obj("{\"ok\":1}"), Call(ClientFor(new FakeTransport().Reply(200, jsonNull))));
    }

    [Fact]
    public void UnicodeAndAwkwardCharactersSurviveBothDirections()
    {
        // \x2028 is a LINE SEPARATOR, \x0001 a control character, e + \x0301 an unnormalized e acute
        string system = "规则: 'quote' \"double\" back\\slash <tag> & ampersand \x2028 line-sep \x0001 ctrl";
        string user = "[\n{\"raw\":\"에스파 (aespa)\",\"n\":1}\n]\nemoji 🎵 ROSÉ e\x0301 \t tab\r\nCRLF";
        JsonObject answer = Obj("{\"results\":[{\"raw\":\"G.E.M. 邓紫棋\",\"artists\":[{\"text\":\"周杰倫\","
                + "\"english\":\"Jay Chou\"}]}],\"note\":\"🎵\"}");
        FakeTransport transport = new FakeTransport().ReplyOk(Obj(answer.ToJsonString()));

        JsonObject result = ClientFor(transport).CallTool(Key, Model, system, user, Tool, "d", Schema(), 10);

        JsonObject body = Obj(transport.Calls[0].Body);
        Assert.Equal(system, body.SystemText());
        Assert.Equal(user, body.Arr("messages")[0].Str("content"));
        AssertJsonEqual(answer, result);
    }

    [Fact]
    public void KeyIsTrimmedBeforeUse()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(Obj("{}"));

        ClientFor(transport).CallTool("  " + Key + "\r\n", Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 10);

        Assert.Equal(Key, transport.Calls[0].Headers["x-api-key"]);
    }

    [Fact]
    public void DefaultConstructorNeedsNoNetworkToBuild()
    {
        Assert.Null(Record.Exception(() => new AnthropicClient()));
    }

    [Fact]
    public void ResponseWithoutRetryAfterHasNone()
    {
        Assert.Null(new AnthropicClient.Response(200, "{}").RetryAfter);
        Assert.Equal("5", new AnthropicClient.Response(429, "{}", "5").RetryAfter);
    }

    // ---- key problems ----

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("   \x3000 ")] // an ideographic space
    public void BlankKeyFailsBeforeAnyRequest(string key)
    {
        FakeTransport transport = new FakeTransport();

        ArtistAiException e = Failure(
                () => ClientFor(transport).CallTool(key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 10));

        Assert.Equal("No API key", e.Message);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void NullKeyFailsBeforeAnyRequest()
    {
        FakeTransport transport = new FakeTransport();

        ArtistAiException e = Failure(
                () => ClientFor(transport).CallTool(null, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 10));

        Assert.Equal("No API key", e.Message);
        Assert.Empty(transport.Calls);
    }

    [Theory]
    [InlineData("sk-ant bad")]
    [InlineData("sk-ant\nbad")]
    [InlineData("sk-ant-é")]
    [InlineData("sk-ant-\0")]
    [InlineData("sk-ant-中")]
    public void KeyThatCannotBeAHeaderValueFailsWithoutEchoingIt(string key)
    {
        FakeTransport transport = new FakeTransport();

        ArtistAiException e = Failure(
                () => ClientFor(transport).CallTool(key, Model, SystemPrompt, UserMessage, Tool, "d", Schema(), 10));

        Assert.Contains("invalid characters", e.Message);
        Assert.False(e.Message.Contains(key.Trim()));
        Assert.Empty(transport.Calls);
    }

    // ---- HTTP errors ----

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void RejectedKey(int status)
    {
        FakeTransport transport = new FakeTransport().Reply(status,
                "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key " + Key
                        + "\"}}");

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The API key was rejected (check ANTHROPIC_API_KEY)", e.Message);
        AssertNoLeak(e);
        Assert.Single(transport.Calls);
        Assert.Empty(_sleeps);
    }

    [Fact]
    public void BadRequestShowsTheApiMessage()
    {
        FakeTransport transport = new FakeTransport().Reply(400,
                "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\","
                        + "\"message\":\"Your credit balance is too low to access the Anthropic API.\"}}");

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The API rejected the request: Your credit balance is too low to access the Anthropic API.",
                e.Message);
        Assert.Single(transport.Calls);
        Assert.Empty(_sleeps);
    }

    [Fact]
    public void BadRequestMessageIsScrubbedOfTheKeyAndTidied()
    {
        string message = "bad\n  value   " + Key + "\tin\0header";
        JsonObject error = new JsonObject();
        error["message"] = message;
        JsonObject body = new JsonObject();
        body["error"] = error;
        FakeTransport transport = new FakeTransport().Reply(400, body.ToJsonString());

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The API rejected the request: bad value *** in header", e.Message);
        AssertNoLeak(e);
    }

    [Fact]
    public void LongApiMessagesAreTruncated()
    {
        JsonObject error = new JsonObject();
        error["message"] = new string('x', 5000);
        JsonObject body = new JsonObject();
        body["error"] = error;

        ArtistAiException e = Failure(() => Call(ClientFor(new FakeTransport().Reply(400, body.ToJsonString()))));

        Assert.True(e.Message.Length < 400, "length " + e.Message.Length);
        Assert.EndsWith("...", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("<html>Bad Request</html>")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"error\":\"text\"}")]
    [InlineData("{\"error\":{}}")]
    [InlineData("{\"error\":{\"message\":null}}")]
    [InlineData("{\"error\":{\"message\":\"   \"}}")]
    [InlineData("{\"error\":{\"message\":{\"a\":1}}}")]
    public void BadRequestWithoutAUsableMessage(string body)
    {
        ArtistAiException e = Failure(() => Call(ClientFor(new FakeTransport().Reply(400, body))));

        Assert.Equal("The API rejected the request (HTTP 400)", e.Message);
    }

    [Fact]
    public void BadRequestWithNullBody()
    {
        ArtistAiException e = Failure(() => Call(ClientFor(new FakeTransport().Reply(400, null))));

        Assert.Equal("The API rejected the request (HTTP 400)", e.Message);
    }

    [Fact]
    public void UnknownModel()
    {
        FakeTransport transport = new FakeTransport().Reply(404,
                "{\"type\":\"error\",\"error\":{\"type\":\"not_found_error\",\"message\":\"model: " + Model + "\"}}");

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The model was not found (check the model name: " + Model + ")", e.Message);
        Assert.Single(transport.Calls);
        Assert.Empty(_sleeps);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(405)]
    [InlineData(413)]
    [InlineData(422)]
    public void OtherClientErrorsAreNotRetried(int status)
    {
        FakeTransport transport = new FakeTransport().Reply(status,
                "{\"error\":{\"message\":\"request_too_large\"}}");

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The Anthropic API returned HTTP " + status + ": request_too_large", e.Message);
        Assert.Single(transport.Calls);
        Assert.Empty(_sleeps);
    }

    [Fact]
    public void UnexpectedRedirectIsAnError()
    {
        ArtistAiException e = Failure(() => Call(ClientFor(new FakeTransport().Reply(302, "moved"))));

        Assert.Equal("The Anthropic API returned HTTP 302", e.Message);
    }

    // ---- retries ----

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(529)]
    public void TransientStatusIsRetriedAndThenSucceeds(int status)
    {
        FakeTransport transport = new FakeTransport().Reply(status, "{\"error\":{\"message\":\"busy\"}}")
                .ReplyOk(Obj("{\"ok\":true}"));

        JsonObject result = Call(ClientFor(transport));

        AssertJsonEqual(Obj("{\"ok\":true}"), result);
        Assert.Equal(2, transport.Calls.Count);
        Assert.Equal(new List<long> { 1000L }, _sleeps);
        // the retry is the same request
        Assert.Equal(transport.Calls[0].Body, transport.Calls[1].Body);
        Assert.Equal(transport.Calls[0].Headers, transport.Calls[1].Headers);
    }

    [Fact]
    public void BackoffDoublesUntilItGivesUp()
    {
        FakeTransport transport = new FakeTransport().Reply(529, "").Reply(529, "").Reply(529, "").Reply(529, "")
                .ReplyOk(Obj("{}"));

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The Anthropic API is overloaded (HTTP 529), try again later", e.Message);
        Assert.True(transport.Calls.Count == 4, "1 try + 3 retries");
        Assert.Equal(new List<long> { 1000L, 2000L, 4000L }, _sleeps);
        AssertNoLeak(e);
    }

    [Fact]
    public void SucceedsOnTheLastAllowedRetry()
    {
        FakeTransport transport = new FakeTransport().Reply(503, "").Reply(503, "").Reply(503, "")
                .ReplyOk(Obj("{\"ok\":1}"));

        JsonObject result = Call(ClientFor(transport));

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.Equal(new List<long> { 1000L, 2000L, 4000L }, _sleeps);
    }

    [Fact]
    public void ExhaustedRetriesGiveAStatusSpecificMessage()
    {
        ArtistAiException rateLimited = Failure(() => Call(ClientFor(Rep(429))));
        ArtistAiException serverError = Failure(() => Call(ClientFor(Rep(500))));

        Assert.Equal("The Anthropic API rate limit was reached (HTTP 429), try again in a moment",
                rateLimited.Message);
        Assert.Equal("The Anthropic API is unavailable (HTTP 500), try again later", serverError.Message);
    }

    private static FakeTransport Rep(int status)
    {
        FakeTransport transport = new FakeTransport();
        for (int i = 0; i < 4; i++)
        {
            transport.Reply(status, "");
        }
        return transport;
    }

    [Fact]
    public void RetryAfterOnTheFirstFailureOnlyAffectsThatWait()
    {
        FakeTransport transport = new FakeTransport()
                .Reply(new AnthropicClient.Response(429, "", "7"))
                .Reply(429, "")
                .ReplyOk(Obj("{}"));

        Call(ClientFor(transport));

        Assert.Equal(new List<long> { 7000L, 2000L }, _sleeps);
    }

    [Theory]
    [MemberData(nameof(RetryAfterCases))]
    public void RetryAfterHeaderIsHonouredInSecondsAndCapped(string header, long expectedMillis)
    {
        FakeTransport transport = new FakeTransport()
                .Reply(new AnthropicClient.Response(429, "", header))
                .ReplyOk(Obj("{}"));

        Call(ClientFor(transport));

        Assert.Equal(new List<long> { expectedMillis }, _sleeps);
    }

    public static IEnumerable<object[]> RetryAfterCases()
    {
        yield return new object[] { "7", 7000L };
        yield return new object[] { " 3 ", 3000L };
        yield return new object[] { "1", 1000L };
        yield return new object[] { "1.5", 1500L };
        yield return new object[] { "30", 30000L };
        yield return new object[] { "31", 30000L };
        yield return new object[] { "120", 30000L };
        yield return new object[] { "99999999999999999999999", 30000L };
        yield return new object[] { new string('9', 400), 30000L }; // too big even for a double
        // unusable values fall back to the 1 s backoff
        yield return new object[] { "0", 1000L };
        yield return new object[] { "0.0", 1000L };
        yield return new object[] { "-5", 1000L };
        yield return new object[] { "", 1000L };
        yield return new object[] { "abc", 1000L };
        yield return new object[] { "1e3", 1000L };
        yield return new object[] { "NaN", 1000L };
        yield return new object[] { "Infinity", 1000L };
        yield return new object[] { "Wed, 21 Oct 2026 07:28:00 GMT", 1000L };
        yield return new object[] { null, 1000L };
    }

    [Fact]
    public void RetryAfterIsIgnoredOnNonRetryableStatuses()
    {
        FakeTransport transport = new FakeTransport().Reply(new AnthropicClient.Response(400, "", "5"));

        Failure(() => Call(ClientFor(transport)));

        Assert.Empty(_sleeps);
    }

    [Fact]
    public void MixedFailuresShareOneRetryBudget()
    {
        FakeTransport transport = new FakeTransport()
                .Reply(529, "")
                .Fail(new IOException("refused", new SocketException((int)SocketError.ConnectionRefused)))
                .Reply(503, "")
                .ReplyOk(Obj("{\"ok\":1}"));

        JsonObject result = Call(ClientFor(transport));

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.Equal(new List<long> { 1000L, 2000L, 4000L }, _sleeps);
    }

    // what Java's ConnectException, UnknownHostException, HttpTimeoutException, SocketTimeoutException and two plain
    // IOExceptions come to in .NET: an IOException, with the cause the HTTP stack would give it
    private static IOException TransportError(int index)
    {
        return index switch
        {
            0 => new IOException("Connection refused", new SocketException((int)SocketError.ConnectionRefused)),
            1 => new IOException("api.anthropic.com", new SocketException((int)SocketError.HostNotFound)),
            2 => new IOException("request timed out", new TimeoutException()),
            // a socket timeout is not an interruption of this thread
            3 => new IOException("Read timed out", new SocketException((int)SocketError.TimedOut)),
            4 => new IOException("connection reset"),
            _ => new IOException(null)
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NetworkErrorsAreRetriedThenReported(int index)
    {
        IOException error = TransportError(index);
        FakeTransport transport = new FakeTransport().Fail(error).Fail(error).Fail(error).Fail(error);

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("Could not reach the Anthropic API", e.Message);
        Assert.Same(error, e.InnerException);
        Assert.Equal(4, transport.Calls.Count);
        Assert.Equal(new List<long> { 1000L, 2000L, 4000L }, _sleeps);
        AssertNoLeak(e);
    }

    [Fact]
    public void NetworkErrorThenSuccess()
    {
        FakeTransport transport = new FakeTransport().Fail(new IOException("timed out", new TimeoutException()))
                .ReplyOk(Obj("{\"ok\":1}"));

        JsonObject result = Call(ClientFor(transport));

        AssertJsonEqual(Obj("{\"ok\":1}"), result);
        Assert.Equal(new List<long> { 1000L }, _sleeps);
    }

    [Fact]
    public void InterruptedTransportStopsWithoutRetrying()
    {
        // Java: InterruptedIOException with the interrupt flag set; .NET: the interrupted wait throws
        AnthropicClient client = new AnthropicClient((url, headers, body, timeout) => throw new ThreadInterruptedException(),
                _sleeps.Add);
        try
        {
            ArtistAiException e = Failure(() => Call(client));

            Assert.Equal("The request was cancelled", e.Message);
            Assert.Empty(_sleeps);
        }
        finally
        {
            ClearInterrupt();
        }
    }

    [Fact]
    public void InterruptedWhileWaitingStopsAndKeepsTheFlag()
    {
        FakeTransport transport = new FakeTransport().Reply(529, "").ReplyOk(Obj("{}"));
        AnthropicClient client = new AnthropicClient(transport.Post, millis => throw new ThreadInterruptedException());
        try
        {
            ArtistAiException e = Failure(() => Call(client));

            Assert.Equal("The request was cancelled", e.Message);
            Assert.Single(transport.Calls);
            // the interrupt must stay visible to the caller: the next wait of this thread ends at once
            Assert.Throws<ThreadInterruptedException>(() => Thread.Sleep(0));
        }
        finally
        {
            ClearInterrupt();
        }
    }

    // ---- unusable answers ----

    [Fact]
    public void CutOffAnswerIsReportedEvenIfTheToolInputLooksComplete()
    {
        FakeTransport transport = new FakeTransport().Reply(200, ToolUseBody(Tool, Obj("{\"results\":[]}"), "max_tokens"));

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The answer was cut off", e.Message);
        Assert.True(transport.Calls.Count == 1, "a cut-off answer is not retried");
    }

    [Fact]
    public void RefusalIsReported()
    {
        FakeTransport transport = new FakeTransport().Reply(200, "{\"content\":[],\"stop_reason\":\"refusal\"}");

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("The model declined to answer", e.Message);
    }

    [Theory]
    [MemberData(nameof(UnusableBodies))]
    public void UnusableSuccessResponses(string body)
    {
        // a reply with content but no usable call is put back to the model a couple of times before giving up
        FakeTransport transport = new FakeTransport().Reply(200, body).Reply(200, body).Reply(200, body);

        ArtistAiException e = Failure(() => Call(ClientFor(transport)));

        Assert.Equal("Unexpected response from the API", e.Message);
        AssertNoLeak(e);
        Assert.True(transport.Calls.Count >= 1 && transport.Calls.Count <= 3);
    }

    public static IEnumerable<object[]> UnusableBodies()
    {
        string[] bodies =
        {
            null,
            "",
            "   ",
            "not json at all",
            "<html><body>502 Bad Gateway</body></html>",
            "{\"content\":[{\"type\":\"tool_use\"",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"record_things\",\"input\":{\"results\":[{\"raw\":\"a",
            "{\"a\":1} trailing garbage",
            "null",
            "[]",
            "[{\"type\":\"tool_use\",\"name\":\"record_things\",\"input\":{}}]",
            "\"a string\"",
            "42",
            "{}",
            "{\"content\":null}",
            "{\"content\":\"text\"}",
            "{\"content\":{}}",
            "{\"content\":[]}",
            "{\"content\":[null,1,\"x\",[]]}",
            "{\"content\":[{\"type\":\"text\",\"text\":\"I cannot use the tool\"}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"another_tool\",\"input\":{}}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"input\":{}}]}",
            "{\"content\":[{\"name\":\"record_things\",\"input\":{}}]}",
            "{\"content\":[{\"type\":\"server_tool_use\",\"name\":\"record_things\",\"input\":{}}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"record_things\"}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"record_things\",\"input\":null}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"record_things\",\"input\":\"{}\"}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"record_things\",\"input\":[]}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":null,\"input\":{}}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":{},\"input\":{}}]}",
            "{\"content\":[{\"type\":\"tool_use\",\"name\":\"RECORD_THINGS\",\"input\":{}}]}"
        };
        foreach (string body in bodies)
        {
            yield return new object[] { body };
        }
    }

    [Fact]
    public void AnEmptyToolInputIsStillAnObject()
    {
        FakeTransport transport = new FakeTransport().ReplyOk(new JsonObject());

        AssertJsonEqual(new JsonObject(), Call(ClientFor(transport)));
    }

    [Fact]
    public void NoRequestDataIsEverPartOfAnyFailureMessage()
    {
        // every failure path in one sweep, each with the secrets planted where they could leak
        List<FakeTransport> scenarios = new List<FakeTransport>
        {
            new FakeTransport().Reply(401, UserSecret),
            new FakeTransport().Reply(400, "{\"error\":{\"message\":\"ok " + Key + "\"}}"),
            new FakeTransport().Reply(404, UserSecret),
            Rep(500),
            Rep(429),
            new FakeTransport().Reply(200, UserSecret),
            new FakeTransport().Reply(200, "{\"stop_reason\":\"max_tokens\",\"x\":\"" + UserSecret + "\"}"),
            new FakeTransport().Fail(new IOException("down")).Fail(new IOException("down"))
                    .Fail(new IOException("down")).Fail(new IOException("down"))
        };

        foreach (FakeTransport scenario in scenarios)
        {
            AssertNoLeak(Failure(() => Call(ClientFor(scenario))));
        }
    }
}
