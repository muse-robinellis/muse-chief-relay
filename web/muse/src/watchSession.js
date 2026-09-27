// Spectator join state.
// hack.chat rejects a join with cmd "warn" and leaves the socket open
// (README: a warn before onlineSet is a rejected join). Reporting "live"
// on socket open leaves a nick collision or rate limit looking connected
// with no feed. "live" is only true after onlineSet. A warn before that
// drops the socket and retries. A taken nick rotates so the retry is not
// the same collision.

export function onWatchFrame(frame, joined) {
  const cmd = frame && frame.cmd;
  const already = !!joined;
  if (cmd === "onlineSet") {
    return { joined: true, live: true, action: "joined", rotateNick: false };
  }
  if (cmd === "warn" && !already) {
    const text = String((frame && frame.text) || "");
    return {
      joined: false,
      live: false,
      action: "retry",
      rotateNick: /taken/i.test(text),
    };
  }
  return { joined: already, live: already, action: "stay", rotateNick: false };
}
