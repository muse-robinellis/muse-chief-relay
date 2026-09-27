using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chief.Bridge;

/// <summary>
/// Collects the fragments of one WebSocket message and decodes UTF-8 once, at end of message, so a
/// multi-byte character split across two fragments comes out intact. A message larger than
/// <c>maxBytes</c> is dropped whole rather than growing memory without bound.
/// </summary>
internal sealed class MessageAssembler(int maxBytes)
{
    private readonly MemoryStream _buf = new();
    private bool _overflow;

    /// <returns>The decoded message at end of message; null while a message is incomplete or when it was dropped.</returns>
    public string? Append(ReadOnlySpan<byte> chunk, bool endOfMessage, out bool dropped)
    {
        dropped = false;
        if (!_overflow)
        {
            if (_buf.Length + chunk.Length > maxBytes)
            {
                _overflow = true;
                _buf.SetLength(0);
            }
            else
            {
                _buf.Write(chunk);
            }
        }

        if (!endOfMessage)
            return null;

        if (_overflow)
        {
            _overflow = false;
            dropped = true;
            return null;
        }

        var text = Encoding.UTF8.GetString(_buf.GetBuffer(), 0, (int)_buf.Length);
        _buf.SetLength(0);
        return text;
    }
}

/// <summary>One inbound frame: the object (if it is one), its cmd, and a redacted copy for the inbox log.</summary>
internal sealed record InboundFrame(JsonNode LogNode, JsonObject? Object, string? Cmd)
{
    public static InboundFrame Parse(string raw)
    {
        JsonNode? node = null;
        try
        {
            node = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            // not JSON: logged as raw below
        }

        // Anything that isn't a JSON object (unparsable text, arrays, bare values) is logged as
        // {"raw": "..."} and otherwise ignored. It never ends the session.
        if (node is not JsonObject obj)
            return new InboundFrame(new JsonObject { ["raw"] = raw }, null, null);

        var cmd = Json.Str(obj, "cmd");
        return new InboundFrame(LogRedaction.Inbound(obj, cmd), obj, cmd);
    }
}

internal static class LogRedaction
{
    public const string Redacted = "<redacted>";

    /// <summary>
    /// Copy of an inbound frame for inbox.jsonl. hack.chat's <c>session</c> frame carries a session token;
    /// it is replaced with a marker.
    /// </summary>
    public static JsonNode Inbound(JsonObject obj, string? cmd)
    {
        var copy = (JsonObject)obj.DeepClone();
        if (cmd == "session" && copy.ContainsKey("token"))
            copy["token"] = Redacted;
        return copy;
    }

    /// <summary>Copy of an outbound frame for inbox.jsonl, with any <c>pass</c> field redacted.</summary>
    public static JsonNode Outbound(JsonNode payload)
    {
        var copy = payload.DeepClone();
        if (copy is JsonObject o && o.ContainsKey("pass"))
            o["pass"] = Redacted;
        return copy;
    }
}

internal static class OutboxPayload
{
    /// <summary>
    /// Turn one outbox line into a frame. A JSON object with a string <c>cmd</c> is sent as is; an
    /// object with a string <c>text</c> becomes a chat; anything else is sent as chat text verbatim.
    /// Blank lines give null (nothing to send).
    /// </summary>
    public static JsonObject? Build(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
            return null;

        try
        {
            if (JsonNode.Parse(trimmed) is JsonObject o)
            {
                if (Json.Str(o, "cmd") is not null)
                    return o;
                if (Json.Str(o, "text") is { } text)
                    return new JsonObject { ["cmd"] = "chat", ["text"] = text };
            }
        }
        catch (JsonException)
        {
            // plain text
        }

        return new JsonObject { ["cmd"] = "chat", ["text"] = trimmed };
    }
}

internal static class Json
{
    public static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
