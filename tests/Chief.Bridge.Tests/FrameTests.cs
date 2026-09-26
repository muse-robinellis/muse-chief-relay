using System.Text;
using System.Text.Json.Nodes;

namespace Chief.Bridge.Tests;

public class MessageAssemblerTests
{
    [Fact]
    public void Multibyte_character_split_across_fragments_is_intact()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"text\":\"café ✓ 日本\"}");
        var split = Array.IndexOf(bytes, (byte)0xE2) + 1; // inside the 3-byte ✓
        var asm = new MessageAssembler(1024);

        Assert.Null(asm.Append(bytes.AsSpan(0, split), endOfMessage: false, out _));
        var text = asm.Append(bytes.AsSpan(split), endOfMessage: true, out var dropped);

        Assert.False(dropped);
        Assert.Equal("{\"text\":\"café ✓ 日本\"}", text);
    }

    [Fact]
    public void Oversized_message_is_dropped_and_the_next_one_is_fine()
    {
        var asm = new MessageAssembler(8);
        Assert.Null(asm.Append(new byte[6], false, out _));
        Assert.Null(asm.Append(new byte[6], true, out var dropped));
        Assert.True(dropped);

        Assert.Equal("ok", asm.Append("ok"u8, true, out dropped));
        Assert.False(dropped);
    }
}

public class InboundFrameTests
{
    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("not json {")]
    public void Non_object_frames_are_logged_raw_and_not_handled(string raw)
    {
        var f = InboundFrame.Parse(raw);

        Assert.Null(f.Object);
        Assert.Null(f.Cmd);
        Assert.Equal(raw, f.LogNode["raw"]!.GetValue<string>());
    }

    [Fact]
    public void Non_string_cmd_is_ignored()
    {
        var f = InboundFrame.Parse("{\"cmd\":7}");
        Assert.NotNull(f.Object);
        Assert.Null(f.Cmd);
    }

    [Fact]
    public void Session_token_is_redacted_in_the_log_copy_only()
    {
        var f = InboundFrame.Parse("{\"cmd\":\"session\",\"restored\":false,\"token\":\"eyJsecret\"}");

        Assert.Equal("session", f.Cmd);
        Assert.Equal(LogRedaction.Redacted, f.LogNode["token"]!.GetValue<string>());
        Assert.DoesNotContain("eyJsecret", f.LogNode.ToJsonString());
        Assert.Equal("eyJsecret", f.Object!["token"]!.GetValue<string>());
    }

    [Fact]
    public void Chat_frames_keep_their_token_like_text()
    {
        var f = InboundFrame.Parse("{\"cmd\":\"chat\",\"text\":\"token\",\"token\":\"x\"}");
        Assert.Equal("x", f.LogNode["token"]!.GetValue<string>());
    }

    [Fact]
    public void Outbound_log_copy_redacts_pass()
    {
        var node = JsonNode.Parse("{\"cmd\":\"join\",\"channel\":\"c\",\"nick\":\"n\",\"pass\":\"hunter2\"}")!;
        var logged = LogRedaction.Outbound(node).ToJsonString();

        Assert.DoesNotContain("hunter2", logged);
        Assert.Equal("hunter2", node["pass"]!.GetValue<string>());
    }
}

public class OutboxPayloadTests
{
    [Fact]
    public void Blank_line_sends_nothing() => Assert.Null(OutboxPayload.Build("   "));

    [Fact]
    public void Plain_text_becomes_chat()
    {
        var p = OutboxPayload.Build("hello there")!;
        Assert.Equal("chat", p["cmd"]!.GetValue<string>());
        Assert.Equal("hello there", p["text"]!.GetValue<string>());
    }

    [Fact]
    public void Object_with_cmd_passes_through()
    {
        var p = OutboxPayload.Build("{\"cmd\":\"emote\",\"text\":\"waves\"}")!;
        Assert.Equal("emote", p["cmd"]!.GetValue<string>());
    }

    [Fact]
    public void Object_with_text_only_becomes_chat()
    {
        var p = OutboxPayload.Build("{\"text\":\"hi\"}")!;
        Assert.Equal("{\"cmd\":\"chat\",\"text\":\"hi\"}", p.ToJsonString(JsonUtil.Opts));
    }

    [Fact]
    public void Other_json_is_sent_verbatim_as_chat()
    {
        var p = OutboxPayload.Build("[1,2]")!;
        Assert.Equal("[1,2]", p["text"]!.GetValue<string>());
    }
}

public class JsonUtilTests
{
    [Fact]
    public void Apostrophe_and_non_ascii_are_not_escaped()
    {
        var json = new JsonObject { ["text"] = "chief's café ✓ 日本" }.ToJsonString(JsonUtil.Opts);
        Assert.Equal("{\"text\":\"chief's café ✓ 日本\"}", json);
    }
}
