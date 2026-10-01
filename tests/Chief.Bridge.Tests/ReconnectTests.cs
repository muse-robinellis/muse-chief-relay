using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Chief.Bridge.Tests;

public class ReconnectTests
{
    [Fact]
    public void Jitter_stays_inside_twenty_percent_and_the_cap()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(800), Backoff.WithJitter(1, 0));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), Backoff.WithJitter(1, 0.5));
        Assert.Equal(TimeSpan.FromMilliseconds(1200), Backoff.WithJitter(1, 1));
        Assert.Equal(TimeSpan.FromSeconds(30), Backoff.WithJitter(30, 1));
        Assert.Equal(TimeSpan.FromSeconds(24), Backoff.WithJitter(30, 0));
        // A bad draw is treated as 0, which is the short end of the range (80%).
        Assert.Equal(TimeSpan.FromMilliseconds(800), Backoff.WithJitter(1, double.NaN));
    }

    [Fact]
    public async Task More_than_three_transient_failures_then_a_join_succeeds()
    {
        const string pass = "s3cret-trip-pass";
        await using var fx = new RelayFixture(pass);
        // Six failures, each a different way the socket can die, then a join that sticks.
        // A cap of 3 would stop before the onlineSet.
        fx.Script.Enqueue(
            Attempt.ConnectThrows(new IOException($"dns lookup failed {pass}")),
            Attempt.ConnectThrows(new IOException("connection refused")),
            Attempt.ConnectThrows(new System.Security.Authentication.AuthenticationException("tls handshake failed")),
            Attempt.Warn("Nickname taken"),
            Attempt.CloseDuringJoin(),
            Attempt.Warn("You are joining channels too fast. Wait a moment and try again."),
            Attempt.OnlineSetHold(fx.Nick));

        await fx.RunUntilConnected();

        Assert.Equal(7, fx.Script.Created);
        Assert.Equal(
            new[] { 1, 2, 4, 8, 16, 30 }.Select(s => TimeSpan.FromSeconds(s)).ToArray(),
            fx.Delays);
        var inbox = File.ReadAllText(Path.Combine(fx.Dir.Path, "inbox.jsonl"));
        Assert.DoesNotContain(pass, inbox);
        Assert.Contains("join rejected: Nickname taken", inbox);
        Assert.Contains("\"attempt\":6", inbox);
        Assert.Contains(pass, fx.Script.SentText);
    }

    [Fact]
    public async Task A_drop_after_a_confirmed_join_reconnects()
    {
        await using var fx = new RelayFixture();
        fx.Script.Enqueue(Attempt.OnlineSetThenClose(fx.Nick), Attempt.OnlineSetHold(fx.Nick));

        await fx.RunUntilConnected(minCreated: 2);

        Assert.Equal(2, fx.Script.Created);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(fx.Delays));
        Assert.Contains("server closed the connection", File.ReadAllText(Path.Combine(fx.Dir.Path, "inbox.jsonl")));
    }

    [Fact]
    public async Task A_hung_connect_is_abandoned_and_retried()
    {
        await using var fx = new RelayFixture { ConnectTimeout = TimeSpan.FromMilliseconds(40) };
        fx.Script.Enqueue(
            Attempt.HangConnect(),
            Attempt.HangConnect(),
            Attempt.HangConnect(),
            Attempt.HangConnect(),
            Attempt.OnlineSetHold(fx.Nick));

        await fx.RunUntilConnected();

        Assert.Equal(5, fx.Script.Created);
        Assert.Contains("connect not completed within 40ms", File.ReadAllText(Path.Combine(fx.Dir.Path, "inbox.jsonl")));
    }

    [Fact]
    public async Task A_failure_to_write_the_log_or_state_does_not_exit()
    {
        await using var fx = new RelayFixture();
        // The session catch already swallows a dead socket. These two paths used to run
        // *after* that catch, so an IOException here killed the process on the first retry.
        Directory.CreateDirectory(Path.Combine(fx.Dir.Path, "inbox.jsonl"));
        Directory.CreateDirectory(Path.Combine(fx.Dir.Path, "state.json"));
        fx.Script.Enqueue(
            Attempt.ConnectThrows(new IOException("connection refused")),
            Attempt.ConnectThrows(new IOException("connection refused")),
            Attempt.ConnectThrows(new IOException("connection refused")),
            Attempt.ConnectThrows(new IOException("connection refused")));
        fx.CancelAfterCreates = 4;

        var ex = await Record.ExceptionAsync(() => fx.RunToCompletion());

        Assert.Null(ex);
        Assert.Equal(4, fx.Script.Created);
    }

    [Fact]
    public async Task A_closed_stdout_does_not_exit_the_retry_loop()
    {
        await using var fx = new RelayFixture
        {
            // The reconnect line is written after the session catch. A closed pipe used to
            // throw here and leave RunForeverAsync, which Main does not catch.
            Stdout = _ => throw new IOException("stdout closed")
        };
        fx.Script.Enqueue(
            Attempt.ConnectThrows(new IOException("connection refused")),
            Attempt.ConnectThrows(new IOException("connection refused")),
            Attempt.ConnectThrows(new IOException("connection refused")),
            Attempt.ConnectThrows(new IOException("connection refused")));
        fx.CancelAfterCreates = 4;

        var ex = await Record.ExceptionAsync(() => fx.RunToCompletion());

        Assert.Null(ex);
        Assert.Equal(4, fx.Script.Created);
    }

    [Theory]
    [InlineData("ftp://example.com/chat")]
    [InlineData("http://hack.chat/chat-ws")]
    [InlineData("not a url")]
    public async Task A_url_that_can_never_work_fails_fast(string url)
    {
        await using var fx = new RelayFixture { Url = url };
        fx.Script.Enqueue(Attempt.OnlineSetHold("n"));

        var ex = await Assert.ThrowsAsync<ConfigException>(() => fx.RunToCompletion());

        Assert.Equal(0, fx.Script.Created);
        Assert.Contains("url", ex.Message);
    }

    [Fact]
    public async Task A_real_socket_keeps_retrying_past_three_rejected_handshakes()
    {
        await using var server = await LocalChat.Start();
        await using var fx = new RelayFixture { Url = server.Url, UseRealSocket = true };
        server.RejectHandshakes(5);
        server.ThenOnlineSet("n");

        await fx.RunUntilConnected();

        Assert.True(server.Handshakes >= 6, $"server saw {server.Handshakes} handshakes");
    }

    [Fact]
    public void Muse_pages_build_is_the_vite_client()
    {
        // web/muse is the Vue source. docs/muse is the Vite build GitHub Pages
        // serves. They are not byte-identical copies anymore.
        var root = RepoRoot();
        var source = Path.Combine(root, "web", "muse");
        var published = Path.Combine(root, "docs", "muse");

        Assert.True(File.Exists(Path.Combine(source, "package.json")));
        Assert.True(File.Exists(Path.Combine(source, "src", "reconnect.js")));
        Assert.True(File.Exists(Path.Combine(source, "src", "App.vue")));

        var index = File.ReadAllText(Path.Combine(published, "index.html"));
        Assert.Contains("id=\"app\"", index, StringComparison.Ordinal);
        Assert.Contains("<script", index, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(published, "app.js")));
        Assert.False(File.Exists(Path.Combine(published, "reconnect.js")));

        var built = string.Concat(Directory.GetFiles(published, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".js", StringComparison.Ordinal) || f.EndsWith(".html", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        Assert.Contains("reconnecting in ", built, StringComparison.Ordinal);
        Assert.Contains("with a public trip", built, StringComparison.Ordinal);
        Assert.Contains("voizle-text-relay", built, StringComparison.Ordinal);
        Assert.DoesNotContain("hack.chat", built, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MuseChiefRelay.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found from " + AppContext.BaseDirectory);
    }
}
