# Changelog

Merged work, newest first. Times are ET.

## Unreleased

- **#8 Removed: legacy Python bridge** (open, stacked on #7). Deleted `legacy/python/`, which held `bridge.py`,
  `bin/hc`, `parse_msg.py`, `test_pump.py`, `requirements.txt` and its README. Also deleted the root
  `relay_poll.py`, the operator poller for the old Python setup (it hard-coded `/workspace/hackchat`).
  Removed the matching `.gitignore` entries and all doc and landing-page references. Chief.Bridge is the
  only bridge. `tools/status.py` (the status publisher) stays. Past entries below still mention
  `legacy/` as history.
- **#7 Chief.Bridge reliability fixes** (open, not merged).
  - Reconnect delay: it now goes back to 1 s after a session confirmed by `onlineSet` or 60 s of uptime. Before, the reset line was unreachable, so after a few drops every reconnect waited 30 s for the rest of the process's life.
  - Clean shutdown on SIGTERM and SIGINT. `state.json` ends with `alive: false` and `[chief] stopped` is logged. Before, the process exited from the ProcessExit handler and left `state.json` saying `alive: true`.
  - Outbox: only complete, newline-terminated lines are sent, and the read position advances line by line only after a send succeeds. A line that fails to send is sent again after the reconnect. Before, the position moved before the send, so a line was lost if the send failed, and a half-written line could go out truncated.
  - UTF-8 is decoded once per whole WebSocket message. Before, it was decoded per fragment, which could corrupt a multi-byte character split across fragments.
  - `say` and `status` take `--config <path>` and honour `MUSE_RELAY_CONFIG`. They no longer fall back to parent directories or `config.example.json`. The bridge run no longer searches parent directories or the app directory. An explicit path that doesn't exist is now an error, not a silent fallthrough. See the README table.
  - `connected: true` only after `onlineSet`. A `warn` before it (e.g. `Nickname taken`), or no `onlineSet` within 15 s, counts as a rejected join and is retried with backoff. Before, the bridge could sit "connected" outside the channel.
  - Frames that aren't JSON objects are logged as `{"raw": ...}` and skipped instead of ending the session.
  - Relaxed JSON encoder, so `'` and non-ASCII text aren't `\u`-escaped.
  - hack.chat's `session` token, and any outbound `pass`, are logged as `<redacted>`.
  - The outbox is drained only after the join is confirmed. `state.json` is written atomically and includes `pid`, and `status` flags a stale state.
  - New xunit project `tests/Chief.Bridge.Tests` (43 tests).
- **Correction to #6.** PR #6's description said the .NET bridge wasn't affected by the outbox bug. That was only half right. Chief.Bridge didn't have the stale-pump race, because its pump belonged to one connection. It did have the related bug: the read position advanced before the send succeeded, so a line whose send failed was lost, and a half-written line could be sent early. The fixes above address this.

## 2026-09-25

- **#6 Legacy bridge outbox fix** (merged 07:41). After a reconnect, the old outbox pump thread in
  `legacy/python/bridge.py` kept running and raced the new one, dropping lines into the dead socket.
  Now a pump exits when its connection isn't live, and the position advances only after a send
  succeeds. Adds `legacy/python/test_pump.py`.
- **#5 Docs refresh** (merged 07:41). README, `docs/protocol.md`, new `docs/security.md` and this
  changelog.
- **#4 Status panel** (merged 06:13). `docs/status/` renders `docs/status.json`: per-state counts,
  task cards, and a coverage timeline. All values go in via `textContent`. `?demo` loads a fixture built
  by `status.py` from a synthetic log. Handles missing and malformed files. Ships the fixture only; no
  real-log status file is committed.
- **#3 Status view v1** (merged 04:41). Optional `repo` field on tasks. `tools/status.py` builds
  `docs/status.json` fail-closed: `publish_repos` allowlist (case-insensitive), every message needs a
  trip in `publish_trips` (the bridge's own included), and an empty list publishes nothing. Both
  bridges take an optional `pass` so they can have a trip. 13 unit tests in `tools/test_status.py`.
  The design came from alex's knowledge-graph idea, worked through with Fuse.
- **#2 Landing page** (merged 03:41). `docs/index.html` plus `docs/landing.css` at the GitHub Pages
  root, linking to the client already published at `docs/muse/`.
- **#1 Client auto-reconnect** (merged 03:41). Exponential backoff with jitter (1 s → 30 s). Immediate
  retry when the tab becomes visible or the browser comes back online. A rejected join no longer leaves
  the client showing "connected" outside the channel. Review nits from Fuse applied.

## 2026-09-24

- Initial relay: `Chief.Bridge` (.NET 8) desktop bridge, `web/muse` browser client, `docs/protocol.md`,
  the legacy Python bridge under `legacy/python/`, and the client published on Pages at `docs/muse/`.
