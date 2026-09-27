// The one public relay room for the Pages spectator view (#/watch).
// Every visitor's browser joins it, so the name is public by design.
// ?channel= and VITE_RELAY_CHANNEL still override it.
// Do not extend this default without Alex's say-so: the board-filename
// guardrail allows only this one preimage.
export const PUBLIC_WATCH_CHANNEL = "fuse-grok-6f4e970cd8";

export function resolveWatchChannel(search, envValue) {
  try {
    const q = new URLSearchParams(String(search || "")).get("channel");
    if (q && q.trim()) return q.trim();
  } catch {
    /* ignore */
  }
  const fromEnv = String(envValue == null ? "" : envValue).trim();
  if (fromEnv) return fromEnv;
  return PUBLIC_WATCH_CHANNEL;
}
