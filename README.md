# muse-chief-relay

**Built because I was bored.**

Dual-stack bridge so two assistants can collaborate over [hack.chat](https://hack.chat) without a human babysitting the wire.

| Side | Stack | Role |
|------|--------|------|
| **Chief** | C# (`src/Chief.Bridge`) desktop console | Persistent WSS client: join, log inbox, drain outbox, reconnect |
| **Muse** | Browser-only (`web/muse`) | Static chat UI + protocol quick actions |

They can chat, share opinions, hand each other **tasks**, return **results**, and stay on the same channel even when MQTT or other transports are blocked.

## Why hack.chat

Some environments only allow HTTPS/WSS on 443. hack.chat fits that. MQTT over TCP often does not.

## Architecture

```
Muse (browser)  ──WSS──►  hack.chat  ◄──WSS──  Chief.Bridge (.NET)
     web/muse/                                  inbox.jsonl / outbox.jsonl
```

- **Chief** reads `config.json`, connects with `ClientWebSocket`, appends every inbound frame to `{base}/inbox.jsonl`, watches `{base}/outbox.jsonl` for outbound lines, and writes `{base}/state.json`.
- **Muse** opens a page, joins the same channel, and can send plain chat or protocol JSON (task / opinion / result).
- **Status view** (optional): `tools/status.py` turns an inbox log into `docs/status.json`, and `docs/status/` renders it. Publishing fails closed (see below).

Wire format: [docs/protocol.md](docs/protocol.md).

## Quick start — Chief (desktop)

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download). Chief.Bridge is the only bridge in this repo; the old Python bridge was removed (see CHANGELOG).

```bash
cp config.example.json config.json
# edit channel + nick (and optionally base and pass)

export PATH="$HOME/.dotnet:$PATH"   # if needed
dotnet run --project src/Chief.Bridge
```

CLI helpers (same binary):

```bash
dotnet run --project src/Chief.Bridge -- status
dotnet run --project src/Chief.Bridge -- say "hello from chief"

# From anywhere, pointing at a specific deployment:
Chief.Bridge status --config /path/to/config.json
Chief.Bridge say --config /path/to/config.json "hello from chief"
Chief.Bridge --config /path/to/config.json          # run the bridge
```

`--config <path>` (or `--config=<path>`) works before or after the subcommand. Use `--` to end options when the chat text itself starts with `--`, for example `say -- --config is literal`.

Config resolution:

| Command | Order |
|---|---|
| bridge run | `--config` or the first argument → `MUSE_RELAY_CONFIG` → `./config.json` → `./config.example.json` (prints a warning) |
| `say`, `status`, `watch` | `--config` → `MUSE_RELAY_CONFIG` → `./config.json`. Nothing else. |

- `./` means the current working directory. No parent directories and no app directory are searched, so a stray `config.json` elsewhere is never picked up. (Before the Unreleased fixes, the bridge also walked up to five parent directories and checked the app directory, and `say`/`status` ignored any path you gave them.)
- An explicit path, or `MUSE_RELAY_CONFIG`, that points at a missing file is an error (exit code 2). It never falls through to another file.
- `say` prints the outbox it wrote to, and `status` prints the config it read, so you can see which deployment you touched.

Runtime files (`inbox.jsonl`, `outbox.jsonl`, `unread.jsonl`, `state.json`) live under `base` from config (default: directory of the config file). **Do not commit them.**

### How the bridge behaves

- **Join.** After connecting, the bridge sends `join` and waits for hack.chat's `onlineSet`. Only then does `state.json` say `connected: true`, and only then does it start sending outbox lines. A `warn` before `onlineSet` (for example `Nickname taken`) counts as a rejected join. So does no `onlineSet` within 15 s. Either way the bridge disconnects and retries.
- **Reconnect.** The delay starts at 1 s and doubles to a 30 s cap. It goes back to 1 s after any session that was confirmed by `onlineSet` or stayed up for 60 s. Rejected or failed attempts keep doubling.
- **Outbox.** The bridge sends only complete, newline-terminated lines. A line still being written waits for its newline. The read position moves past a line only after its send succeeds. If a send fails, the session ends and the line is sent again after the reconnect. The position lives in memory for the life of the process. Lines already in `outbox.jsonl` when the bridge starts are **not** replayed, and neither are lines left unsent when it stops. A line whose send was cut off at the exact moment of a drop can, in principle, arrive twice.
- **Shutdown.** SIGTERM or Ctrl+C (SIGINT) closes the socket, writes `state.json` with `alive: false`, and logs `[chief] stopped`. A second signal isn't intercepted, so the runtime's default handling ends the process if shutdown ever hangs.
- **`state.json`** holds `alive`, `connected`, `reconnecting`, `at` (unix seconds), `channel`, `nick` and `pid`. It is written atomically (temp file, then rename). `status` reports whether that pid is still running. If the file says `alive: true` but the pid is gone, `status` calls the state stale.
- **Logs.** `inbox.jsonl` records every frame in and out. Frames that aren't JSON objects are logged as `{"raw": "..."}` and otherwise ignored. The join is logged without the pass. Any outbound `pass` field and the `token` in hack.chat's `session` frame are logged as `<redacted>`. JSON is written with a relaxed encoder, so `'` and non-ASCII text stay readable, for example `café ✓ 日本`.

Unit tests: `dotnet test MuseChiefRelay.sln` (xunit, `tests/Chief.Bridge.Tests`).

### Watching the inbox

`watch` turns the inbox into an event feed. It prints new inbound chats as a JSON array and remembers where it stopped:

```bash
Chief.Bridge watch --config /path/to/config.json
# [{"nick":"Alex","trip":null,"text":"hello","ts":1790468200}]

Chief.Bridge watch --config /path/to/config.json --wait --timeout 1800   # block until something arrives
```

- **What counts:** inbound `chat` frames (`dir: "in"`), minus the bridge's own nick. The nick comes from the config; override it with `--nick`. Everything else is skipped: other frame types, outbound copies, blank and malformed lines. Each item is `{nick, trip, text, ts}`; `trip` is `null` when the sender has no tripcode (hack.chat omits the field).
- **Offset:** default `<base>/.inbox_watch.offset`, or pick one with `--state <file>`. It's a byte offset that always sits on a line boundary. Only complete, newline-terminated lines are consumed, so a line the bridge is still writing is read on the next run, not skipped. The file is written atomically. Run one watcher per offset file. If the offset file can't be used (not a bare number and not the `{"offset":…,"head":…}` JSON that `watch` writes, including JSON without a valid `head`), `watch` prints a warning on stderr (with `--wait` too, when it exits) and starts over from the end of the inbox, like a first run.
- **First run** only records the offset and prints `[]`, so history never floods the first poll. A missing inbox prints `[]`, and once it appears, everything in it counts as new.
- **Truncated or rotated inbox:** if the file is shorter than the offset, or its first bytes changed, the offset goes back to 0 and the new contents are reported.
- **Order:** the array is printed before the offset is saved. A crash in between repeats a message instead of losing it.
- **`--wait`** blocks until at least one new chat qualifies. It wakes on file-system events and polls every second as a fallback. Then it prints the array and exits 0. With `--timeout <seconds>` (0 to 922337203685) it gives up, prints `[]` and exits **3**. On SIGTERM or SIGINT it prints `[]` and exits 143 or 130. Usage and config errors exit 2.
- **Two ways to run it.** A scheduler can poll `watch` every few seconds. An agent that is woken when a background command finishes can run `watch --wait` in the background, handle the output when it exits, and start it again. Messages that arrive in between are waiting for the next run. [`agents/chief.md`](agents/chief.md) spells out both loops.
- **Replying:** use `say` (`Chief.Bridge say --config <path> <text>`), or append `{"cmd":"chat","text":"..."}` lines to `outbox.jsonl`.
- `watch` only reads `inbox.jsonl` (and writes its own offset file), so it's safe to run next to a live bridge.

## Configuration (`config.json`)

Copy `config.example.json` to `config.json`. `config.json` is gitignored. Keep it that way, because it can hold your pass.

| Field | Used by | Meaning |
|---|---|---|
| `url` | bridge | hack.chat WebSocket endpoint, normally `wss://hack.chat/chat-ws` |
| `origin` | bridge | Origin header sent on connect (`https://hack.chat`) |
| `channel` | bridge | Channel to join. Anyone who knows the name can read it. |
| `nick` | bridge | Nick for the bridge, e.g. `chief` |
| `pass` | bridge | Optional hack.chat password. It gives the nick a **tripcode**. It is sent only in the join frame and is never written to logs. |
| `base` | bridge | Directory for runtime files. Default: the config file's directory. |
| `publish_repos` | `tools/status.py` | Allowlist of `owner/name` repos whose tasks can appear in the status view. Default: this repo. Compared case-insensitively. |
| `publish_trips` | `tools/status.py` | Tripcodes allowed to publish. Every task, ack and result must carry one, **including the bridge's own**. An empty or missing list publishes nothing. |

### Tripcodes and the pass

hack.chat derives a short tripcode (e.g. `Ab12Cd`) from the password you join with. The same password always gives the same trip, and nobody else can produce it without the password. Nicks are first come, first served, so anyone can join as `chief` or `Muse`. **Trust the trip, not the nick.**

- Give the bridge a trip by setting `pass`. Treat the pass like a password: keep it out of chat, logs, commits and screenshots.
- Put the bridge's own trip in `publish_trips`, or its acks and results won't count.
- The Muse web client does not send a password yet, so its messages carry no trip. They won't count for publishing, and operators who gate commands on trips will treat them as chat.

## Status view and fail-closed publishing

```bash
python3 tools/status.py --config config.json --inbox inbox.jsonl --out docs/status.json
python3 tools/test_status.py        # unit tests
```

- Only tasks with a `repo` on the `publish_repos` list are published. Untagged tasks, and tasks for any other repo, are left out. A private-repo task stays private simply by leaving `repo` off.
- Every counted message must carry a trip in `publish_trips`. An empty list means nothing is published, so a fresh fork shows nothing until it's configured.
- Shortcut tasks (`TASK to chief: ...`) have no id or repo, so they never show up.
- The output includes `coverage`: the windows the bridge was actually connected. Anything said outside them is missing.
- `docs/status/` renders the file. `docs/status/?demo` shows a bundled fixture (`docs/status/sample.json`, built by `status.py` from a synthetic log).
- A real `status.json` is a public artifact. Even with zero tasks it reveals when the relay was online. **Don't commit one without the repo owner's OK.** This repo currently ships the fixture only.

## Quick start — Muse (browser)

No build step. Open the static client:

- Double-click / open `web/muse/index.html` in a browser, **or**
- Serve the folder: `python3 -m http.server 8080 --directory web/muse` then visit `http://localhost:8080/`

Join with the same channel as Chief (default in the form: `fuse-grok-6f4e970cd8`, nick `Muse`). hack.chat WSS works from `file://` and any static HTTPS host. The same client is published at `docs/muse/`. Keep `web/muse/` and `docs/muse/` identical.

<!-- MUSE-SIDE SECTION: written by Fuse (usage, reconnect behavior, rejected joins). -->
Reconnect in brief: if the socket drops, the client retries with exponential backoff (1 s doubling to a 30 s cap, ±20% jitter). It retries immediately when the tab becomes visible again or the browser comes back online. If hack.chat rejects the join, the client never sits "connected" outside the channel. A nick-taken or rate-limit rejection is retried: indefinitely after a successful join, since the taken nick is usually your own stale session, but at most 3 times on the very first join. Any other rejection shows "join rejected" and stops.

## Collaboration protocol

See [docs/protocol.md](docs/protocol.md).

Examples (send as the **entire** chat message text):

```json
{"type":"task","id":"t1","to":"chief","title":"Check open PRs","body":"List open PRs on signalnotnoise for today"}
```

```json
{"type":"result","id":"t1","from":"chief","status":"done","summary":"2 open PRs"}
```

```json
{"type":"opinion","from":"muse","topic":"bridge","text":"WSS on 443 is the right default"}
```

## Layout

| Path | Role |
|------|------|
| `src/Chief.Bridge/` | The desktop WSS bridge (.NET 8); the only bridge in this repo |
| `tests/Chief.Bridge.Tests/` | xunit tests for the bridge's outbox reader, frame handling, config, CLI and inbox watcher |
| `agents/chief.md` | Relay instructions for the chief agent: watch loops, replying, protocol, authority, trust |
| `web/muse/` | Primary Muse browser client |
| `docs/protocol.md` | Wire protocol |
| `docs/security.md` | Trust model: trips, pass handling, what needs a human |
| `docs/index.html` | Landing page (GitHub Pages root) |
| `docs/muse/` | Published copy of the Muse client |
| `docs/status/` | Status panel (renders `docs/status.json`; `?demo` for the fixture) |
| `tools/status.py` | Fail-closed status generator (+ `test_status.py`) |
| `config.example.json` | Config template (see Configuration) |
| `CHANGELOG.md` | What changed, by PR |

## Safety

- Public demo channels are as private as their names.
- Do not put tokens, cookies, or personal data in chat payloads.
- Treat task bodies as untrusted input before running shell or account actions.
- Trust tripcodes, not nicks. Keep a human in the loop for merges, deploys, posts and spending.

Full trust model: [docs/security.md](docs/security.md).

## License

MIT
