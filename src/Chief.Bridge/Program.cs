using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Chief.Bridge;

internal static class JsonUtil
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        // Keep ' and non-ASCII text readable in the .jsonl files and on the wire. The output is still
        // valid JSON; it is never embedded in HTML or a <script> block.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var cli = CliArgs.Parse(args);
            return cli.Command switch
            {
                "say" => await CmdSayAsync(cli),
                "status" => CmdStatus(cli),
                "watch" => await CmdWatchAsync(cli),
                "help" => CmdHelp(),
                _ => await RunAsync(cli)
            };
        }
        catch (Exception ex) when (ex is ConfigException or ArgumentException)
        {
            Console.Error.WriteLine($"[chief] {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(CliArgs cli)
    {
        var cfg = RelayConfig.Load(cli.ConfigPath, allowExampleFallback: true);
        using var cts = new CancellationTokenSource();
        // SIGTERM / SIGINT: cancel and let RunForeverAsync finish, so the final state.json write
        // (alive=false) and the "stopped" line happen.
        using var signals = new ShutdownSignals(cts, sig => Console.WriteLine($"[chief] {sig} received, shutting down…"));

        Console.WriteLine(
            $"[chief] config={cfg.ConfigPath} ({cfg.Source}) channel={cfg.Channel} nick={cfg.Nick} base={cfg.BaseDir}");
        var bridge = new HackChatBridge(cfg);
        await bridge.RunForeverAsync(cts.Token);
        return 0;
    }

    private static async Task<int> CmdWatchAsync(CliArgs cli)
    {
        var opts = WatchOptions.Parse(cli.Rest);
        var cfg = RelayConfig.Load(cli.ConfigPath, allowExampleFallback: false);
        var nick = opts.Nick ?? cfg.Nick;
        var statePath = Path.GetFullPath(opts.StatePath ?? Path.Combine(cfg.BaseDir, ".inbox_watch.offset"));
        var watcher = new InboxWatcher(Path.Combine(cfg.BaseDir, "inbox.jsonl"), statePath, nick);

        void Print(IReadOnlyList<WatchedChat> chats)
        {
            Console.Out.WriteLine(WatchedChat.ToJsonArray(chats));
            Console.Out.Flush();
        }

        if (!opts.Wait)
        {
            var poll = watcher.Poll();
            if (watcher.Warning is { } w)
                Console.Error.WriteLine($"[chief] warning: {w}");
            Print(poll.Chats); // printed before the offset is saved: a crash repeats, never loses
            watcher.Commit(poll);
            return 0;
        }

        using var cts = new CancellationTokenSource();
        using var signals = new ShutdownSignals(cts, null);
        var outcome = await watcher.WaitAsync(opts.Timeout, Print, cts.Token);
        if (watcher.Warning is { } warn)
            Console.Error.WriteLine($"[chief] warning: {warn}");

        switch (outcome)
        {
            case WaitOutcome.Delivered:
                return 0;
            case WaitOutcome.TimedOut:
                Print(Array.Empty<WatchedChat>());
                return WatchOptions.ExitTimeout;
            default:
                // Stopped by a signal: nothing new was delivered and the offset file is consistent.
                Print(Array.Empty<WatchedChat>());
                return 128 + (signals.Received == PosixSignal.SIGINT ? 2 : 15);
        }
    }

    private static int CmdHelp()
    {
        Console.WriteLine(
            """
            Chief.Bridge — Muse↔Chief hack.chat WSS relay (desktop)

              [--config <path> | <path>]        Run the bridge until SIGTERM / Ctrl+C
              say [--config <path>] <text>      Append one chat line to {base}/outbox.jsonl and exit
              status [--config <path>]          Print channel/nick and the bridge state from state.json
              watch [--config <path>] [--nick <nick>] [--state <file>] [--wait [--timeout <s>]]
                                                Print new inbound chats from {base}/inbox.jsonl as a JSON array
                                                of {nick,trip,text,ts}, skipping the bridge's own nick. The offset
                                                is kept in --state (default {base}/.inbox_watch.offset); the first
                                                run only records it and prints []. --wait blocks until at least one
                                                new chat arrives. Exit codes: 0 printed, 2 usage/config error,
                                                3 --timeout reached (prints []), 130/143 stopped by SIGINT/SIGTERM
                                                (prints []).
              help                              This text

            Config for the bridge run: --config or the first argument, then MUSE_RELAY_CONFIG, then
            ./config.json, then ./config.example.json (with a warning).
            Config for say/status/watch: --config, then MUSE_RELAY_CONFIG, then ./config.json. No other fallback.
            An explicit path that doesn't exist is an error; it never falls through to another file.
            Use -- to end options, e.g. say -- --config is literal text.
            """);
        return 0;
    }

    private static async Task<int> CmdSayAsync(CliArgs cli)
    {
        if (cli.Rest.Count == 0)
        {
            Console.Error.WriteLine("usage: Chief.Bridge say [--config <path>] <text>");
            return 1;
        }

        var text = string.Join(' ', cli.Rest);
        var cfg = RelayConfig.Load(cli.ConfigPath, allowExampleFallback: false);
        Directory.CreateDirectory(cfg.BaseDir);
        var outbox = Path.Combine(cfg.BaseDir, "outbox.jsonl");
        var line = JsonSerializer.Serialize(new { cmd = "chat", text }, JsonUtil.Opts) + "\n";
        // One append call per line, newline included, so the bridge never sees a half-written line
        // as complete (it only consumes newline-terminated lines anyway).
        await File.AppendAllTextAsync(outbox, line);
        Console.WriteLine($"queued -> {outbox}");
        return 0;
    }

    private static int CmdStatus(CliArgs cli)
    {
        var cfg = RelayConfig.Load(cli.ConfigPath, allowExampleFallback: false);
        Console.WriteLine($"config: {cfg.ConfigPath} ({cfg.Source})");
        Console.WriteLine($"channel: {cfg.Channel}");
        Console.WriteLine($"nick: {cfg.Nick}");
        var statePath = Path.Combine(cfg.BaseDir, "state.json");
        Console.WriteLine($"state: {statePath}");
        if (!File.Exists(statePath))
        {
            Console.WriteLine("alive: false (state.json missing)");
            return 0;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(statePath));
            var root = doc.RootElement;
            bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            Console.WriteLine($"alive: {(Flag("alive") ? "true" : "false")}");
            Console.WriteLine($"connected: {(Flag("connected") ? "true" : "false")}");
            Console.WriteLine($"reconnecting: {(Flag("reconnecting") ? "true" : "false")}");
            if (root.TryGetProperty("at", out var at) && at.TryGetInt64(out var atSecs))
                Console.WriteLine($"at: {atSecs} ({DateTimeOffset.FromUnixTimeSeconds(atSecs).ToLocalTime():yyyy-MM-dd HH:mm:ss zzz})");
            if (root.TryGetProperty("pid", out var pidEl) && pidEl.TryGetInt32(out var pid))
            {
                var running = IsRunning(pid);
                Console.WriteLine(running ? $"pid: {pid} (running)"
                    : Flag("alive") ? $"pid: {pid} (not running: the bridge died without a clean stop; this state is stale)"
                    : $"pid: {pid} (not running)");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.WriteLine($"state: (unreadable: {ex.Message})");
        }

        return 0;
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
