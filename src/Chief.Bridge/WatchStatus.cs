using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chief.Bridge;

/// <summary>
/// What a <c>watch</c> process last said about itself, kept next to its offset file as
/// <c>&lt;offset file&gt;.status</c> (default <c>&lt;base&gt;/.inbox_watch.offset.status</c>).
/// <list type="bullet">
/// <item><c>state</c>: <c>armed</c> (waiting), <c>settling</c> (a chat arrived; collecting the rest of the burst),
/// <c>delivered</c>, <c>timed_out</c>, <c>stopped</c> (SIGTERM/SIGINT), or <c>polled</c> (a one-shot run without
/// <c>--wait</c>).</item>
/// <item><c>heartbeat_at</c> is rewritten every few seconds while armed, so a watcher that was killed
/// (SIGKILL, OOM, box restart) is noticed even though it never wrote a final state.</item>
/// </list>
/// Written atomically (temp file, then rename). Nothing reads it to decide what to deliver; it only feeds
/// <c>status</c> and the bridge's auto-acknowledgement text.
/// </summary>
internal sealed record WatchStatus(
    int Pid,
    string State,
    long ArmedAt,
    long HeartbeatAt,
    long? Deadline,
    long? ExitedAt,
    int? ExitCode,
    int? Delivered,
    double SettleSeconds)
{
    public const string Armed = "armed";
    public const string Settling = "settling";
    public const string DeliveredState = "delivered";
    public const string TimedOut = "timed_out";
    public const string Stopped = "stopped";
    public const string Polled = "polled";

    public static string PathFor(string offsetPath) => offsetPath + ".status";

    public JsonObject ToJson() => new()
    {
        ["pid"] = Pid,
        ["state"] = State,
        ["armed_at"] = ArmedAt,
        ["heartbeat_at"] = HeartbeatAt,
        ["deadline"] = Deadline,
        ["exited_at"] = ExitedAt,
        ["exit_code"] = ExitCode,
        ["delivered"] = Delivered,
        ["settle_s"] = SettleSeconds
    };

    /// <summary>Null when the file is missing or isn't a status this code wrote.</summary>
    public static WatchStatus? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o)
                return null;
            if (Json.Str(o, "state") is not { } state || Long(o, "pid") is not { } pid || Long(o, "heartbeat_at") is not { } hb)
                return null;
            var settle = o["settle_s"] is JsonValue sv && sv.TryGetValue<double>(out var s) ? s : 0;
            return new WatchStatus((int)pid, state, Long(o, "armed_at") ?? hb, hb, Long(o, "deadline"),
                Long(o, "exited_at"), (int?)Long(o, "exit_code"), (int?)Long(o, "delivered"), settle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static long? Long(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<long>(out var l) ? l : null;
}

internal enum ListenerState
{
    /// <summary>No status file (no watcher has run with this build, or a different offset file is in use).</summary>
    Unknown,

    /// <summary>A live watcher is waiting (or settling a burst) and heartbeating.</summary>
    Armed,

    /// <summary>A watcher exited cleanly a moment ago (delivered or timed out), so the agent was just woken and is
    /// expected to re-arm. Also a recent one-shot poll.</summary>
    Waking,

    /// <summary>Nothing is listening: the last watcher exited long ago, was stopped, or died without a word.</summary>
    NotArmed
}

internal sealed record ListenerView(ListenerState State, string Detail, WatchStatus? Status)
{
    /// <summary>A heartbeat older than this means the watcher is gone or hung.</summary>
    public static readonly TimeSpan HeartbeatStale = TimeSpan.FromSeconds(30);

    /// <summary>How long after a clean exit (delivered / timed out / one-shot poll) the agent counts as awake and
    /// about to re-arm. chief's wake-read-reply-re-arm cycle takes about a minute.</summary>
    public static readonly TimeSpan WakeGrace = TimeSpan.FromSeconds(180);

    public static ListenerView Classify(WatchStatus? s, DateTimeOffset now, Func<int, bool> isRunning)
    {
        if (s is null)
            return new ListenerView(ListenerState.Unknown, "unknown (no watch status file)", null);

        var nowS = now.ToUnixTimeSeconds();
        string Ago(long t) => Human(TimeSpan.FromSeconds(Math.Max(0, nowS - t)));

        if (s.State is WatchStatus.Armed or WatchStatus.Settling)
        {
            if (!isRunning(s.Pid))
                return new ListenerView(ListenerState.NotArmed,
                    $"NOT ARMED: watcher pid {s.Pid} died without a clean exit (last heartbeat {Ago(s.HeartbeatAt)} ago)", s);
            if (nowS - s.HeartbeatAt > (long)HeartbeatStale.TotalSeconds)
                return new ListenerView(ListenerState.NotArmed,
                    $"NOT ARMED: watcher pid {s.Pid} is running but its heartbeat is {Ago(s.HeartbeatAt)} old (hung or suspended?)", s);
            var until = s.Deadline is { } d ? $", times out in {Human(TimeSpan.FromSeconds(Math.Max(0, d - nowS)))}" : "";
            return new ListenerView(ListenerState.Armed,
                $"armed (pid {s.Pid}, {s.State}, armed {Ago(s.ArmedAt)} ago, heartbeat {Ago(s.HeartbeatAt)} ago{until})", s);
        }

        var exited = s.ExitedAt ?? s.HeartbeatAt;
        var how = s.State switch
        {
            WatchStatus.DeliveredState => $"delivered {s.Delivered ?? 0} chat(s)",
            WatchStatus.TimedOut => "timed out",
            WatchStatus.Stopped => "was stopped by a signal",
            WatchStatus.Polled => "one-shot poll",
            _ => s.State
        };
        var code = s.ExitCode is { } c ? $" (exit {c})" : "";

        if (s.State != WatchStatus.Stopped && nowS - exited <= (long)WakeGrace.TotalSeconds)
            return new ListenerView(ListenerState.Waking,
                $"waking: last watch {how}{code} {Ago(exited)} ago; the agent should re-arm shortly", s);

        return new ListenerView(ListenerState.NotArmed,
            $"NOT ARMED: last watch {how}{code} {Ago(exited)} ago and nothing has re-armed it", s);
    }

    public static string Human(TimeSpan t) =>
        t.TotalSeconds < 90 ? $"{(long)t.TotalSeconds}s"
        : t.TotalMinutes < 90 ? $"{(long)t.TotalMinutes}m"
        : $"{(long)t.TotalHours}h{t.Minutes:00}m";
}

/// <summary>
/// Writes a watcher's status file: once when armed, every <c>heartbeatInterval</c> while waiting, and once on
/// exit. A failed write is reported once on stderr and never stops the watcher.
/// </summary>
internal sealed class WatchStatusWriter
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _heartbeatInterval;
    private readonly int _pid = Environment.ProcessId;
    private readonly Stopwatch _sinceWrite = new();
    private long _armedAt;
    private long? _deadline;
    private double _settle;
    private bool _warned;

    public static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(5);

    public WatchStatusWriter(string path, Func<DateTimeOffset>? clock = null, TimeSpan? heartbeatInterval = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeat;
    }

    public string Path => _path;

    public void Armed(TimeSpan? timeout, double settleSeconds)
    {
        var now = _clock();
        _armedAt = now.ToUnixTimeSeconds();
        _settle = settleSeconds;
        _deadline = timeout is { } t && t < TimeSpan.FromDays(365 * 100) ? (now + t).ToUnixTimeSeconds() : null;
        Write(WatchStatus.Armed, null, null, null);
    }

    /// <summary>Called on every loop of the wait. Writes only when the phase changed or the heartbeat is due.</summary>
    public void Tick(WatchPhase phase)
    {
        var state = phase == WatchPhase.Settling ? WatchStatus.Settling : WatchStatus.Armed;
        if (state == _lastState && _sinceWrite.Elapsed < _heartbeatInterval)
            return;
        Write(state, null, null, null);
    }

    private string? _lastState;

    public void Exited(string state, int exitCode, int? delivered) =>
        Write(state, _clock().ToUnixTimeSeconds(), exitCode, delivered);

    public void Polled(int delivered)
    {
        _armedAt = _clock().ToUnixTimeSeconds();
        Write(WatchStatus.Polled, _armedAt, 0, delivered);
    }

    private void Write(string state, long? exitedAt, int? exitCode, int? delivered)
    {
        var s = new WatchStatus(_pid, state, _armedAt, _clock().ToUnixTimeSeconds(), _deadline, exitedAt, exitCode, delivered, _settle);
        try
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tmp = $"{_path}.tmp.{_pid}";
            File.WriteAllText(tmp, s.ToJson().ToJsonString(JsonUtil.Opts) + "\n");
            File.Move(tmp, _path, overwrite: true);
            _lastState = state;
            _sinceWrite.Restart();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_warned)
            {
                _warned = true;
                Console.Error.WriteLine($"[chief] warning: can't write watch status {_path}: {ex.Message}");
            }
        }
    }
}

internal static class ProcessInfo
{
    public static bool IsRunning(int pid)
    {
        if (pid <= 0)
            return false;
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
