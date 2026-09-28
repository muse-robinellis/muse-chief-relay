// Spectator channel from build or deploy config only.
// Pass VITE_WATCH_CHANNEL (Vite inlines it). An empty value means the
// page should show "watch channel not configured" and not join.
// Never commit the value.
export function resolveWatchChannel(envValue) {
  return String(envValue == null ? "" : envValue).trim();
}
