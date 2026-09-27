using System.Text;

namespace Chief.Bridge.Tests;

public class OutboxReaderTests
{
    [Fact]
    public void Returns_only_newline_terminated_lines()
    {
        using var dir = new TempDir();
        var path = dir.File("outbox.jsonl");
        File.WriteAllText(path, "one\ntwo\nhalf-writ");
        var reader = new OutboxReader(path, 0);

        var lines = reader.ReadPending();

        Assert.Equal(new[] { "one", "two" }, lines.Select(l => l.Text));
        Assert.Equal(8, lines[^1].EndOffset);
    }

    [Fact]
    public void Half_written_line_is_returned_once_its_newline_arrives()
    {
        using var dir = new TempDir();
        var path = dir.File("outbox.jsonl");
        File.WriteAllText(path, "{\"text\":\"hal");
        var reader = new OutboxReader(path, 0);
        Assert.Empty(reader.ReadPending());

        File.AppendAllText(path, "f\"}\n");
        var lines = reader.ReadPending();

        Assert.Single(lines);
        Assert.Equal("{\"text\":\"half\"}", lines[0].Text);
    }

    [Fact]
    public void Reading_does_not_advance_until_commit()
    {
        using var dir = new TempDir();
        var path = dir.File("outbox.jsonl");
        File.WriteAllText(path, "a\nb\n");
        var reader = new OutboxReader(path, 0);

        var first = reader.ReadPending();
        reader.Commit(first[0]);           // "a" sent, "b" failed
        var again = reader.ReadPending();  // after reconnect

        Assert.Equal(new[] { "b" }, again.Select(l => l.Text));
        reader.Commit(again[0]);
        Assert.Empty(reader.ReadPending());
    }

    [Fact]
    public void AtEnd_skips_lines_that_existed_before_start()
    {
        using var dir = new TempDir();
        var path = dir.File("outbox.jsonl");
        File.WriteAllText(path, "old\n");
        var reader = OutboxReader.AtEnd(path);
        File.AppendAllText(path, "new\n");

        Assert.Equal(new[] { "new" }, reader.ReadPending().Select(l => l.Text));
    }

    [Fact]
    public void Missing_file_gives_nothing_and_truncation_restarts_from_top()
    {
        using var dir = new TempDir();
        var path = dir.File("outbox.jsonl");
        var reader = new OutboxReader(path, 0);
        Assert.Empty(reader.ReadPending());

        File.WriteAllText(path, "aaaa\nbbbb\n");
        foreach (var l in reader.ReadPending()) reader.Commit(l);
        File.WriteAllText(path, "c\n"); // shorter than the saved position

        Assert.Equal(new[] { "c" }, reader.ReadPending().Select(l => l.Text));
    }

    [Fact]
    public void Crlf_and_multibyte_text_are_handled()
    {
        using var dir = new TempDir();
        var path = dir.File("outbox.jsonl");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("café ✓ 日本\r\nnext\n"));
        var reader = new OutboxReader(path, 0);

        var lines = reader.ReadPending();

        Assert.Equal("café ✓ 日本", lines[0].Text);
        Assert.Equal(Encoding.UTF8.GetByteCount("café ✓ 日本\r\n"), lines[0].EndOffset);
    }
}
