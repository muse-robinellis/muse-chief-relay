// Exact spectator route. A prefix match would send #/watchdog and
// #/watch-anything to the read-only view; those stay on the interactive client.
const WATCH_HASHES = new Set(["#/watch", "#/watch/"]);

export function isWatchRoute(hash) {
  return WATCH_HASHES.has(String(hash || ""));
}
