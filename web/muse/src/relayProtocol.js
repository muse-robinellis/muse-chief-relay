// voizle-text-relay v1 frames. JSON text. The server speaks first (hello).
// The client then joins with room and nick, plus either a public trip or a
// password. Since voizle#7 the server hashes a join password (SHA-256 with
// the server's salt) into a public trip; the client never hashes, displays,
// logs, or persists it.

export const PROTOCOL_NAME = "voizle-text-relay";
export const PROTOCOL_VERSION = 1;

// A public trip is the short code people write as !XXXX. The user types the
// code without "!". Anything that is not a six-character public code is
// refused here — a password belongs in the join's password field, never in
// trip.
const PUBLIC_TRIP_BODY = /^[A-Za-z0-9+/]{6}$/;

export function publicTrip(raw) {
  let trip = String(raw == null ? "" : raw).trim();
  if (!trip) return "";
  if (trip.startsWith("!")) trip = trip.slice(1);
  if (!PUBLIC_TRIP_BODY.test(trip)) return "";
  return "!" + trip;
}

export function isHello(frame) {
  return !!(
    frame &&
    frame.type === "hello" &&
    frame.v === PROTOCOL_VERSION &&
    frame.protocol === PROTOCOL_NAME
  );
}

// password is sent verbatim in the join frame; the server hashes it into a
// public trip. The client must never hash, display, log, or persist it.
// A password and a public trip are alternative ways to claim an identity:
// when both are given, the password wins and the trip is omitted.
export function joinFrame({ room, nick, trip, password }) {
  const frame = {
    v: PROTOCOL_VERSION,
    type: "join",
    room: String(room || ""),
    nick: String(nick || ""),
  };
  const pw = String(password == null ? "" : password);
  if (pw) {
    frame.password = pw;
    return frame;
  }
  const t = publicTrip(trip);
  if (t) frame.trip = t;
  return frame;
}

export function chatFrame(text) {
  return { v: PROTOCOL_VERSION, type: "chat", text: String(text || "") };
}
