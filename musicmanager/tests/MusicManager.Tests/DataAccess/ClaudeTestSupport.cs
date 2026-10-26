using System.Text.Json.Nodes;
using MusicManager.DataAccess;
using Xunit;

namespace MusicManager.Tests.DataAccess;

/// <summary>
/// what the four Claude test classes share: reading and building the JSON the fake API traffics in (Gson's
/// JsonObject / JsonArray / JsonParser become System.Text.Json.Nodes), and the interrupt flag of the test thread
/// </summary>
internal static class ClaudeTestSupport
{
    /// <summary>JsonParser.parseString(json).getAsJsonObject()</summary>
    public static JsonObject Obj(string json)
    {
        return JsonNode.Parse(json).AsObject();
    }

    /// <summary>assertEquals on two Gson elements (a JsonNode has no structural Equals)</summary>
    public static void AssertJsonEqual(JsonNode expected, JsonNode actual)
    {
        Assert.True(JsonNode.DeepEquals(expected, actual),
                "expected " + (expected == null ? "null" : expected.ToJsonString()) + " but was "
                + (actual == null ? "null" : actual.ToJsonString()));
    }

    public static string Str(this JsonNode node, string key)
    {
        return node[key].GetValue<string>();
    }

    public static int Int(this JsonNode node, string key)
    {
        return node[key].GetValue<int>();
    }

    public static bool Bool(this JsonNode node, string key)
    {
        return node[key].GetValue<bool>();
    }

    public static JsonArray Arr(this JsonNode node, string key)
    {
        return node[key].AsArray();
    }

    public static JsonObject Child(this JsonNode node, string key)
    {
        return node[key].AsObject();
    }

    /// <summary>the system prompt of a request: it is sent as one text block that may be cached (a plain string is read too)</summary>
    public static string SystemText(this JsonNode request)
    {
        JsonNode system = request["system"];
        return system is JsonArray blocks ? blocks[0]["text"].GetValue<string>() : system.GetValue<string>();
    }

    /// <summary>a reply in which the model called the tool with this input (what the API sends back)</summary>
    public static AnthropicClient.Response ToolResponse(string tool, JsonObject input)
    {
        JsonObject block = new JsonObject();
        block["type"] = "tool_use";
        block["name"] = tool;
        block["input"] = input;
        JsonArray content = new JsonArray();
        content.Add(block);
        JsonObject message = new JsonObject();
        message["content"] = content;
        message["stop_reason"] = "tool_use";
        return new AnthropicClient.Response(200, message.ToJsonString());
    }

    /// <summary>Thread.interrupted(): never leak a pending interrupt into other tests</summary>
    public static void ClearInterrupt()
    {
        try
        {
            Thread.Sleep(0);
        }
        catch (ThreadInterruptedException)
        {
            // cleared by being thrown
        }
    }
}
