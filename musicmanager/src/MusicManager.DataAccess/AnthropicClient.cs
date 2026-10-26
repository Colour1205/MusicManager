using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.DataAccess;

/// <summary>
/// minimal client for the Anthropic Messages API. Every request asks the model to answer by calling one tool, so the
/// answer comes back as structured JSON instead of prose that would have to be parsed.
///
/// Nothing the user typed or tagged (the API key, the request body) is ever put into an
/// exception message, and nothing is logged.
/// </summary>
public class AnthropicClient
{
    /// <summary>
    /// the HTTP exchange, split out so tests can run without a network. Throws an <see cref="IOException"/> when the
    /// service cannot be reached, and a <see cref="ThreadInterruptedException"/> when the waiting thread is interrupted.
    /// </summary>
    public delegate Response Transport(string url, IDictionary<string, string> headers, string body, TimeSpan timeout);

    /// <summary>retryAfter is the raw Retry-After header, or null when the server sent none</summary>
    public sealed record Response(int Status, string Body, string RetryAfter)
    {
        public Response(int status, string body) : this(status, body, null)
        {
        }
    }

    /// <summary>waits between retries; a delegate so tests can skip the real waiting</summary>
    public delegate void Sleeper(long millis);

    private const string Url = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    // an answer of thousands of tokens, or one that searched the web first, takes minutes
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private const int MaxRetries = 3;
    private const long MaxDelayMillis = 30_000;
    // rate limit, the API's own server errors, gateway errors and "overloaded"
    private static readonly HashSet<int> RetryableStatuses = new HashSet<int> { 429, 500, 502, 503, 504, 529 };

    private const int MaxApiMessageLength = 300;
    // Java's [\p{Cntrl}\s] is ASCII only: the control characters and the white space up to the space
    private static readonly Regex WhitespaceOrControl = new Regex("[\\x00-\\x20\\x7F]+", RegexOptions.CultureInvariant);
    // Java's \d is ASCII only, and matches() needs the whole string
    private static readonly Regex SecondsForm = new Regex("^[0-9]+(\\.[0-9]+)?\\z", RegexOptions.CultureInvariant);

    private const string UnexpectedResponse = "Unexpected response from the API";

    /// <summary>thrown by the transport when the service did not answer in time (a kind of <see cref="IOException"/>)</summary>
    public sealed class RequestTimedOutException : IOException
    {
        public RequestTimedOutException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    private readonly Transport _transport;
    private readonly Sleeper _sleeper;
    private readonly Func<double> _jitter; // 0 ... 1: how much of the extra quarter of a wait is added to it

    public AnthropicClient(Transport transport, Sleeper sleeper) : this(transport, sleeper, () => 0.0)
    {
    }

    /// <param name="jitter">
    /// a number from 0 to 1 that spreads out the waits of requests that failed together, so that a dozen workers do not
    /// come back at the same moment (tests pass none: the waits are then exact)
    /// </param>
    public AnthropicClient(Transport transport, Sleeper sleeper, Func<double> jitter)
    {
        _transport = transport;
        _sleeper = sleeper;
        _jitter = jitter;
    }

    public AnthropicClient() : this(new HttpTransport().Post, millis => Thread.Sleep(checked((int)millis)),
            () => Random.Shared.NextDouble())
    {
    }

    /// <summary>
    /// what a request can ask for beyond the tool call itself
    /// </summary>
    /// <param name="Effort">how hard the model thinks ("low" ... "max"), or null for the model's default</param>
    /// <param name="MaxSearches">how many web searches the model may run before it answers; 0 turns web search off</param>
    /// <param name="RequireSearch">
    /// the answer is worthless without a search (the model is told to search and may still answer from memory, since a
    /// tool call cannot be forced): when it answers without having searched, it is asked once more, told to search first
    /// </param>
    public sealed record Options(string Effort, int MaxSearches, bool RequireSearch = false)
    {
        public static readonly Options None = new Options(null, 0);
    }

    /// <summary>sent again when an answer that had to be searched for came without a search</summary>
    private const string SearchReminder = "\n\nYou answered without searching the web. Search the web first, for every " +
            "item above (at least one web_search each, using the name and the spellings given), and then call the tool " +
            "with what the search results show. Do not answer from memory.";

    // one request normally answers; a few more allow for a prose answer that needs a nudge or a paused search turn
    private const int MaxRounds = 3;
    private const int MaxRoundsWithSearch = 8;

    /// <summary>
    /// asks the model to answer by calling one tool and returns that call's input object.
    /// </summary>
    /// <exception cref="ArtistAiException">with a short, user-friendly message if the call cannot be completed</exception>
    public JsonObject CallTool(string apiKey, string model, string system, string userMessage, string toolName,
            string toolDescription, JsonObject inputSchema, int maxTokens)
    {
        return CallTool(apiKey, model, system, userMessage, toolName, toolDescription, inputSchema, maxTokens,
                Options.None);
    }

    /// <summary>
    /// Newer models do not allow forcing a tool call, so the tool is only offered ("auto") and the system prompt
    /// says to call it. If the model answers in prose anyway it is asked once more to call the tool, and a search
    /// turn that was paused is continued.
    /// </summary>
    /// <exception cref="ArtistAiException">with a short, user-friendly message if the call cannot be completed</exception>
    public JsonObject CallTool(string apiKey, string model, string system, string userMessage, string toolName,
            string toolDescription, JsonObject inputSchema, int maxTokens, Options options)
    {
        return CallTool(apiKey, model, system, userMessage, toolName, toolDescription, inputSchema, maxTokens, options,
                out bool _);
    }

    /// <summary>
    /// Same, and tells whether the model searched the web to make the answer it gave (a search that was asked for and
    /// not made leaves the answer one from memory, whatever it claims to be sure of).
    /// </summary>
    /// <param name="searched">true if the answer came after a web search</param>
    /// <exception cref="ArtistAiException">with a short, user-friendly message if the call cannot be completed</exception>
    public JsonObject CallTool(string apiKey, string model, string system, string userMessage, string toolName,
            string toolDescription, JsonObject inputSchema, int maxTokens, Options options, out bool searched)
    {
        searched = false;
        if (apiKey == null || JavaString.IsBlank(apiKey))
        {
            throw new ArtistAiException("No API key");
        }
        // an environment variable or pasted key often carries a trailing newline
        string key = JavaString.Strip(apiKey);
        if (!IsHeaderSafe(key))
        {
            throw new ArtistAiException("The API key contains invalid characters (check ANTHROPIC_API_KEY)");
        }

        OrderedDictionary<string, string> headers = new OrderedDictionary<string, string>();
        headers["x-api-key"] = key;
        headers["anthropic-version"] = ApiVersion;
        headers["content-type"] = "application/json";

        List<JsonObject> messages = new List<JsonObject>();
        messages.Add(Message("user", userMessage));

        int rounds = options.MaxSearches > 0 ? MaxRoundsWithSearch : MaxRounds;
        bool reminded = false;
        for (int round = 0; round < rounds; round++)
        {
            string body = BuildBody(model, system, messages, toolName, toolDescription, inputSchema, maxTokens,
                    options);
            // a request that searches the web may legitimately run out of time: sending all of it again would only pay twice
            Response response = Send(headers, body, options.MaxSearches == 0);
            if (response.Status < 200 || response.Status >= 300)
            {
                throw FailureFor(response, key, model);
            }

            JsonObject root = ParseObject(response.Body);
            if (root == null)
            {
                throw new ArtistAiException(UnexpectedResponse);
            }
            // a cut-off tool call is truncated JSON, so even a well-formed-looking input can't be trusted
            string stopReason = ClaudeJson.GetString(root, "stop_reason");
            if ("max_tokens".Equals(stopReason))
            {
                throw new ArtistAiException("The answer was cut off");
            }
            if ("refusal".Equals(stopReason))
            {
                throw new ArtistAiException("The model declined to answer");
            }

            searched |= UsedSearch(root);
            JsonObject input = ToolInput(root, toolName);
            if (input != null)
            {
                // (with no round left to ask in, the answer in hand is the one that is used)
                if (options.RequireSearch && options.MaxSearches > 0 && !searched && !reminded && round + 1 < rounds)
                {
                    // answered from memory: start again, this time telling it to search (the first answer is dropped)
                    reminded = true;
                    messages.Clear();
                    messages.Add(Message("user", userMessage + SearchReminder));
                    continue;
                }
                return input;
            }
            if (root["content"] is not JsonArray content || content.Count == 0)
            {
                throw new ArtistAiException(UnexpectedResponse);
            }

            // continue the same turn: the reply goes back exactly as received (thinking and search blocks included)
            JsonObject assistant = new JsonObject();
            assistant["role"] = "assistant";
            assistant["content"] = content.DeepClone();
            messages.Add(assistant);
            if (!"pause_turn".Equals(stopReason))
            {
                messages.Add(Message("user", "Call the " + toolName + " tool now, with your answer."));
            }
        }
        throw new ArtistAiException(UnexpectedResponse);
    }

    /// <summary>true if the model ran a web search to make this reply (a search block, or a search counted in the usage)</summary>
    private static bool UsedSearch(JsonObject root)
    {
        if (root["content"] is JsonArray content)
        {
            foreach (JsonNode block in content)
            {
                if (block is JsonObject item && "server_tool_use".Equals(ClaudeJson.GetString(item, "type")))
                {
                    return true;
                }
            }
        }
        return root["usage"] is JsonObject usage
                && usage["server_tool_use"] is JsonObject serverTools
                && serverTools["web_search_requests"] is JsonValue count
                && ClaudeJson.TryGetAsInt(count, out int searches)
                && searches > 0;
    }

    private static JsonObject Message(string role, string text)
    {
        JsonObject message = new JsonObject();
        message["role"] = role;
        message["content"] = text;
        return message;
    }

    /// <summary>web search needs the dynamic-filtering tool version on current models; Haiku only has the basic one</summary>
    public static string WebSearchType(string model)
    {
        return model != null && model.ToLowerInvariant().Contains("haiku") ? "web_search_20250305" : "web_search_20260209";
    }

    /// <summary>
    /// The system prompt as a block that may be cached: the tool definition and the prompt come before the lines of a
    /// request, are the same in every request of a run, and are read back at a tenth of the price. A prompt that is
    /// too short to be cached is simply not (no error), and the answer is the same either way.
    /// </summary>
    private static JsonArray CachedSystem(string system)
    {
        JsonObject block = new JsonObject();
        block["type"] = "text";
        block["text"] = system;
        JsonObject cacheControl = new JsonObject();
        cacheControl["type"] = "ephemeral";
        block["cache_control"] = cacheControl;
        JsonArray blocks = new JsonArray();
        blocks.Add(block);
        return blocks;
    }

    private static JsonObject EphemeralCache()
    {
        JsonObject cacheControl = new JsonObject();
        cacheControl["type"] = "ephemeral";
        return cacheControl;
    }

    /// <summary>marks the end of a message as a cache breakpoint (the message is the request's own copy)</summary>
    private static void CacheThrough(JsonObject message)
    {
        if (message["content"] is JsonValue text && text.GetValueKind() == JsonValueKind.String)
        {
            string value = text.GetValue<string>();
            if (value.Length == 0)
            {
                return; // an empty text block cannot be cached
            }
            JsonObject block = new JsonObject();
            block["type"] = "text";
            block["text"] = value;
            block["cache_control"] = EphemeralCache();
            JsonArray blocks = new JsonArray();
            blocks.Add(block);
            message["content"] = blocks;
        }
        else if (message["content"] is JsonArray content && content.Count > 0 && content[content.Count - 1] is JsonObject last)
        {
            string type = ClaudeJson.GetString(last, "type");
            bool cacheable = type is "tool_use" or "server_tool_use" or "web_search_tool_result" or "tool_result"
                    || (type == "text" && !string.IsNullOrEmpty(ClaudeJson.GetString(last, "text")));
            if (cacheable)
            {
                last["cache_control"] = EphemeralCache();
            }
        }
    }

    private static string BuildBody(string model, string system, List<JsonObject> messages, string toolName,
            string toolDescription, JsonObject inputSchema, int maxTokens, Options options)
    {
        JsonObject tool = new JsonObject();
        tool["name"] = toolName;
        tool["description"] = toolDescription;
        tool["input_schema"] = inputSchema.DeepClone(); // a node can only belong to one parent, and the schema is sent again each round
        JsonArray tools = new JsonArray();
        tools.Add(tool);

        if (options.MaxSearches > 0)
        {
            JsonObject search = new JsonObject();
            search["type"] = WebSearchType(model);
            search["name"] = "web_search";
            search["max_uses"] = options.MaxSearches;
            tools.Add(search);
        }

        // the tool is offered, not forced: forcing it is a 400 on current models (and would stop the model searching)
        JsonObject toolChoice = new JsonObject();
        toolChoice["type"] = "auto";
        toolChoice["disable_parallel_tool_use"] = true; // one call to the tool: a second one would be lost

        JsonArray messageList = new JsonArray();
        foreach (JsonObject message in messages)
        {
            messageList.Add(message.DeepClone());
        }
        if (messages.Count > 1)
        {
            // a continuation sends everything said so far again (search results included): read back at a tenth of the price
            CacheThrough((JsonObject)messageList[messageList.Count - 1]);
        }

        // no temperature/top_p/top_k: newer models reject them
        JsonObject body = new JsonObject();
        body["model"] = model;
        body["max_tokens"] = maxTokens;
        body["system"] = CachedSystem(system);
        body["messages"] = messageList;
        body["tools"] = tools;
        body["tool_choice"] = toolChoice;
        if (options.Effort != null)
        {
            JsonObject outputConfig = new JsonObject();
            outputConfig["effort"] = options.Effort;
            body["output_config"] = outputConfig;
        }
        return ClaudeJson.Compact(body);
    }

    /// <summary>posts the request, retrying transient failures; returns the last response (which may still be a failure)</summary>
    private Response Send(IDictionary<string, string> headers, string body, bool retryTimeouts)
    {
        for (int attempt = 0; ; attempt++)
        {
            Response response = null;
            try
            {
                response = _transport(Url, headers, body, RequestTimeout);
            }
            catch (ThreadInterruptedException)
            {
                // Java's InterruptedIOException plus the thread's interrupt flag: the interrupt stays visible to the caller
                Thread.CurrentThread.Interrupt();
                throw Cancelled();
            }
            catch (RequestTimedOutException e) when (!retryTimeouts)
            {
                // the service took longer than the limit: the same request would most likely do so again, and is billed again
                throw new ArtistAiException("The request timed out (a request that searches the web can take longer than the limit)", e);
            }
            catch (IOException e)
            {
                if (attempt == MaxRetries)
                {
                    throw new ArtistAiException("Could not reach the Anthropic API", e);
                }
            }

            if (response != null && (!RetryableStatuses.Contains(response.Status) || attempt == MaxRetries))
            {
                return response;
            }
            Pause(DelayMillis(attempt, response));
        }
    }

    /// <summary>1 s, 2 s, 4 s unless the server said how long to wait (and up to a quarter more, to spread out requests that failed together); never more than 30 s</summary>
    private long DelayMillis(int attempt, Response response)
    {
        long delay = 1000L << attempt;
        if (response != null && response.RetryAfter != null)
        {
            string seconds = JavaString.Strip(response.RetryAfter);
            // only the "seconds" form; an HTTP-date or garbage falls back to the backoff
            if (SecondsForm.IsMatch(seconds))
            {
                double parsed = double.Parse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (parsed > 0)
                {
                    double millis = parsed * 1000;
                    delay = millis >= long.MaxValue ? long.MaxValue : (long)millis; // a huge value saturates, and is capped below
                }
            }
        }
        delay += (long)(delay / 4.0 * Math.Clamp(_jitter(), 0.0, 1.0));
        return Math.Min(delay, MaxDelayMillis);
    }

    private void Pause(long millis)
    {
        try
        {
            _sleeper(millis);
        }
        catch (ThreadInterruptedException)
        {
            Thread.CurrentThread.Interrupt();
            throw Cancelled();
        }
    }

    private static ArtistAiException Cancelled()
    {
        return new ArtistAiException("The request was cancelled");
    }

    private static ArtistAiException FailureFor(Response response, string apiKey, string model)
    {
        int status = response.Status;
        string detail = ApiMessage(response.Body, apiKey);
        return status switch
        {
            401 or 403 => new ArtistAiException("The API key was rejected (check ANTHROPIC_API_KEY)"),
            400 => new ArtistAiException("The API rejected the request" + (detail == null ? " (HTTP 400)" : ": " + detail)),
            404 => new ArtistAiException("The model was not found (check the model name: " + model + ")"),
            429 => new ArtistAiException("The Anthropic API rate limit was reached (HTTP 429), try again in a moment"),
            529 => new ArtistAiException("The Anthropic API is overloaded (HTTP 529), try again later"),
            _ => RetryableStatuses.Contains(status)
                    ? new ArtistAiException("The Anthropic API is unavailable (HTTP " + status + "), try again later")
                    : new ArtistAiException("The Anthropic API returned HTTP " + status + (detail == null ? "" : ": " + detail))
        };
    }

    /// <summary>the error.message text of an API error body, made safe to show; null if there is none</summary>
    private static string ApiMessage(string body, string apiKey)
    {
        JsonObject root = ParseObject(body);
        if (root == null || root["error"] is not JsonObject error)
        {
            return null;
        }
        string message = ClaudeJson.GetString(error, "message");
        if (message == null)
        {
            return null;
        }
        message = JavaString.Strip(WhitespaceOrControl.Replace(message.Replace(apiKey, "***"), " "));
        if (message.Length == 0)
        {
            return null;
        }
        return message.Length <= MaxApiMessageLength ? message : message.Substring(0, MaxApiMessageLength) + "...";
    }

    /// <summary>the input of the call to the named tool, or null if the reply holds no such call</summary>
    private static JsonObject ToolInput(JsonObject root, string toolName)
    {
        if (root["content"] is JsonArray content)
        {
            foreach (JsonNode element in content)
            {
                if (element is JsonObject block
                        && "tool_use".Equals(ClaudeJson.GetString(block, "type"))
                        && toolName.Equals(ClaudeJson.GetString(block, "name"))
                        && block["input"] is JsonObject input)
                {
                    return (JsonObject)input.DeepClone(); // detached from the reply it was read from
                }
            }
        }
        return null;
    }

    /// <summary>the body as a JSON object, or null if it is empty, malformed or not an object</summary>
    private static JsonObject ParseObject(string body)
    {
        if (body == null || JavaString.IsBlank(body))
        {
            return null;
        }
        try
        {
            return ClaudeJson.Parse(body) as JsonObject;
        }
        catch (Exception e) when (ClaudeJson.IsParseFailure(e))
        {
            return null;
        }
    }

    /// <summary>printable ASCII only: anything else makes the HTTP client throw (with the value in its message)</summary>
    private static bool IsHeaderSafe(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c < 0x21 || c > 0x7E)
            {
                return false;
            }
        }
        return true;
    }

    private sealed class HttpTransport
    {
        // one client for the whole program: a new one per call would leak sockets
        private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            AllowAutoRedirect = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan // the request timeout is applied per call
        };

        public Response Post(string url, IDictionary<string, string> headers, string body, TimeSpan timeout)
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
            // run on the thread pool and wait: the wait can be interrupted (the sync send cannot), and a UI thread cannot deadlock
            Task<Response> exchange = Task.Run(() => PostAsync(url, headers, body, cancellation.Token));
            try
            {
                return exchange.GetAwaiter().GetResult();
            }
            catch (ThreadInterruptedException)
            {
                cancellation.Cancel();
                throw;
            }
            catch (OperationCanceledException e)
            {
                // what Java's HttpClient reports as an HttpTimeoutException (an IOException)
                throw new RequestTimedOutException("The request timed out", e);
            }
            catch (Exception e) when (e is HttpRequestException or SocketException or AuthenticationException)
            {
                // what Java's HttpClient reports as an IOException
                throw new IOException(e.Message, e);
            }
        }

        private static async Task<Response> PostAsync(string url, IDictionary<string, string> headers, string body,
                CancellationToken cancellation)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            request.Content = new StringContent(body, new UTF8Encoding(false));
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (string.Equals(header.Key, "content-type", StringComparison.OrdinalIgnoreCase))
                {
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellation).ConfigureAwait(false);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
            string retryAfter = null;
            if (response.Headers.NonValidated.TryGetValues("Retry-After", out HeaderStringValues values))
            {
                foreach (string value in values)
                {
                    retryAfter = value;
                    break;
                }
            }
            return new Response((int)response.StatusCode, Encoding.UTF8.GetString(bytes), retryAfter);
        }
    }
}

/// <summary>
/// what the three Claude classes need of JSON that System.Text.Json does not give: Gson's exact output (compact like
/// JsonElement.toString(), pretty like a pretty-printing Gson, optionally HTML-safe like a plain Gson), Gson's
/// getAsString / getAsInt on a primitive, and a parse that reports every failure as an exception to catch.
/// </summary>
internal static class ClaudeJson
{
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>JsonElement.toString(): compact, nothing HTML-escaped, nulls kept</summary>
    public static string Compact(JsonNode node)
    {
        return Write(node, false, false, true);
    }

    /// <summary>new Gson().toJson(element): compact, HTML-escaped (&lt; &gt; &amp; = '), nulls left out</summary>
    public static string CompactHtmlSafe(JsonNode node)
    {
        return Write(node, false, true, false);
    }

    /// <summary>a pretty-printing Gson without HTML escaping: two spaces, \n, nulls left out</summary>
    public static string Pretty(JsonNode node)
    {
        return Write(node, true, false, false);
    }

    /// <summary>the JSON text as a tree: null for the literal null; any problem is thrown (see <see cref="IsParseFailure"/>)</summary>
    public static JsonNode Parse(string text)
    {
        return JsonNode.Parse(text, null, new JsonDocumentOptions { MaxDepth = 512 });
    }

    /// <summary>true for what a parse or a read of the tree can throw on malformed input (Gson's JsonParseException and friends)</summary>
    public static bool IsParseFailure(Exception e)
    {
        return e is JsonException or ArgumentException or InvalidOperationException or FormatException;
    }

    /// <summary>the primitive at key as a string (a number or boolean as its text), or null if there is none (missing, null, object, array)</summary>
    public static string GetString(JsonObject jsonObject, string key)
    {
        return jsonObject[key] is JsonValue value ? AsString(value) : null;
    }

    /// <summary>JsonPrimitive.getAsString(): a string as it is, a number as written, a boolean as true / false</summary>
    public static string AsString(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                return value.GetValue<string>();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            default:
                return value.ToJsonString();
        }
    }

    /// <summary>
    /// JsonPrimitive.getAsInt(): a number is truncated the way Gson does it, a string is parsed; false where Gson throws
    /// a NumberFormatException (a boolean, or a string that is not an integer)
    /// </summary>
    public static bool TryGetAsInt(JsonValue value, out int result)
    {
        result = 0;
        switch (value.GetValueKind())
        {
            case JsonValueKind.Number:
                string text = value.ToJsonString();
                if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
                {
                    return true;
                }
                if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole))
                {
                    result = unchecked((int)whole);
                    return true;
                }
                // BigDecimal.intValue(): the fraction dropped, then the low 32 bits
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    result = double.IsFinite(number)
                            ? unchecked((int)(uint)(System.Numerics.BigInteger.Parse(
                                    Math.Truncate(number).ToString("F0", CultureInfo.InvariantCulture),
                                    CultureInfo.InvariantCulture) & uint.MaxValue))
                            : 0;
                    return true;
                }
                return false;
            case JsonValueKind.String:
                return int.TryParse(value.GetValue<string>(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                        out result);
            default:
                return false;
        }
    }

    private static string Write(JsonNode node, bool pretty, bool htmlSafe, bool serializeNulls)
    {
        StringBuilder text = new StringBuilder();
        Write(text, node, pretty, htmlSafe, serializeNulls, 0);
        return text.ToString();
    }

    private static void Write(StringBuilder text, JsonNode node, bool pretty, bool htmlSafe, bool serializeNulls,
            int depth)
    {
        switch (node)
        {
            case null:
                text.Append("null");
                break;
            case JsonObject jsonObject:
            {
                bool any = false;
                text.Append('{');
                foreach (KeyValuePair<string, JsonNode> property in jsonObject)
                {
                    if (property.Value == null && !serializeNulls)
                    {
                        continue;
                    }
                    if (any)
                    {
                        text.Append(',');
                    }
                    any = true;
                    NewLine(text, pretty, depth + 1);
                    WriteString(text, property.Key, htmlSafe);
                    text.Append(pretty ? ": " : ":");
                    Write(text, property.Value, pretty, htmlSafe, serializeNulls, depth + 1);
                }
                if (any)
                {
                    NewLine(text, pretty, depth);
                }
                text.Append('}');
                break;
            }
            case JsonArray array:
            {
                bool any = false;
                text.Append('[');
                foreach (JsonNode element in array)
                {
                    if (any)
                    {
                        text.Append(',');
                    }
                    any = true;
                    NewLine(text, pretty, depth + 1);
                    Write(text, element, pretty, htmlSafe, serializeNulls, depth + 1);
                }
                if (any)
                {
                    NewLine(text, pretty, depth);
                }
                text.Append(']');
                break;
            }
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        WriteString(text, value.GetValue<string>(), htmlSafe);
                        break;
                    case JsonValueKind.True:
                        text.Append("true");
                        break;
                    case JsonValueKind.False:
                        text.Append("false");
                        break;
                    case JsonValueKind.Null:
                        text.Append("null");
                        break;
                    default:
                        text.Append(value.ToJsonString());
                        break;
                }
                break;
        }
    }

    private static void NewLine(StringBuilder text, bool pretty, int depth)
    {
        if (pretty)
        {
            text.Append('\n').Append(' ', depth * 2);
        }
    }

    /// <summary>Gson's JsonWriter.string(): quotes, backslash and control characters escaped, everything else as it is</summary>
    private static void WriteString(StringBuilder text, string value, bool htmlSafe)
    {
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case LineSeparator:
                    text.Append("\\u2028");
                    break;
                case ParagraphSeparator:
                    text.Append("\\u2029");
                    break;
                case '<' when htmlSafe:
                    text.Append("\\u003c");
                    break;
                case '>' when htmlSafe:
                    text.Append("\\u003e");
                    break;
                case '&' when htmlSafe:
                    text.Append("\\u0026");
                    break;
                case '=' when htmlSafe:
                    text.Append("\\u003d");
                    break;
                case '\'' when htmlSafe:
                    text.Append("\\u0027");
                    break;
                default:
                    if (c < 0x20)
                    {
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }
                    break;
            }
        }
        text.Append('"');
    }
}
