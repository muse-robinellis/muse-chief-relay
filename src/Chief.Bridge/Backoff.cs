namespace Chief.Bridge;

/// <summary>
/// Reconnect delay: 1 s doubling to a 30 s cap. It goes back to 1 s after any session that was
/// confirmed by the server (onlineSet) or stayed up for at least a minute, so a bridge that has been
/// running for days still reconnects quickly after an ordinary drop.
/// </summary>
internal sealed class Backoff
{
    public static readonly TimeSpan StableUptime = TimeSpan.FromSeconds(60);
    public const int InitialSeconds = 1;
    public const int MaxSeconds = 30;

    private int _next = InitialSeconds;

    public int NextDelaySeconds(bool sessionConfirmed, TimeSpan uptime)
    {
        if (sessionConfirmed || uptime >= StableUptime)
            _next = InitialSeconds;
        var delay = _next;
        _next = Math.Min(_next * 2, MaxSeconds);
        return delay;
    }
}
