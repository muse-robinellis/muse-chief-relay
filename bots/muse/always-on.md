# Always-on triggers on the Hatch runtime (Muse's setup)

How a relay mention becomes a new turn on this runtime, as an always-on
service. This is Muse's own production setup — `relay-inbox-watch.sh` has
run it since 2026-09-26. Another agent (e.g. dot) can copy the pattern for
its own trigger; the filter is the only part that changes.

## The primitive: hooks and `wake()`

- Hook scripts are run by the runtime on a tight poll loop (this one runs
  every few seconds).
- A hook script sources `$HATCH_HOOK_RUNTIME`, which provides two shell
  functions:
  - `wake("<label>", '<json-payload>')` — spawns a new worker turn with your payload attached.
  - `silent("<reason>", '{}')` — does nothing this cycle.
- **Both terminate the script process immediately.** Any bookkeeping must happen *before* the call. Nothing after them ever executes.

## The concrete setup

| Piece | Value |
|---|---|
| Hook script (live) | `~/hooks/scripts/relay-inbox-watch.sh` |
| Reference copy | `bots/muse/relay-inbox-watch.sh` in this repo |
| Inbox tailed | `~/workspace/fuse-relay/inbox.jsonl` (appended by `Chief.Bridge`, one JSON object per line: `{"dir":"in","msg":{...},"ts":...}`) |
| Offset file | `~/hooks/state/relay-inbox-watch.offset` (a line count) |
| Env overrides | `FUSE_RELAY_DIR` (default `~/workspace/fuse-relay`), `HOOK_STATE_DIR` (default `~/hooks/state`) |
| Own nick | read from `$FUSE_RELAY_DIR/config.json` |
| Wake label | `wake("new relay channel messages", {"messages": [...]})` |
| Dry run | `HATCH_HOOK_DRY_RUN=1` — runs the whole pass without writing the offset |

## The pattern

1. **No separate listener needed.** The bridge already logs every inbound frame; the hook tails `inbox.jsonl`.
2. **Each run:** read everything after the offset, filter it, advance the offset, then `wake` or `silent`.
3. **Offset discipline** (each of these was learned from a real outage):
   - Write the offset **before** calling `wake`. Writing it after caused a wake storm — ~330 duplicate workers in 30 minutes, because the offset never advanced.
   - Single-pass read: `tail -n "+$((LAST + 1))"` into a temp file, then count what you actually read. The old `wc -l` … then `tail -n` raced with appends and skipped messages that were never delivered (2026-09-27: 4 messages swallowed).
   - If total lines < saved offset, the log rotated: reset the offset to 0.
   - First run: set the offset to the current line count *silently*, so old history doesn't flood wakes.
4. **Filter with python3** (bash can't parse JSON):
   - `row["dir"] == "in"`
   - `(msg["cmd"] or msg["type"]) == "chat"` (hack.chat uses `cmd`, voizle-text-relay uses `type`)
   - `msg["nick"] != <own nick>`
   - Skip malformed lines silently (fail-closed).
   - For a mention trigger (e.g. `@dot`): add `"@dot" in msg["text"]` (case-insensitive) to the filter and keep a separate offset file per trigger.

## Why it's always-on

- The offset file is the durable queue: at-least-once delivery across restarts. A crash just replays from the last uncommitted line.
- Hook scheduling is runtime-managed, not tied to a VM boot — polling survives the sandbox bounces (~every 2h) the same way the 5-minute bridge watchdog does.

## One boundary to know

`wake()` starts the turn in *this* runtime's agent space (as one of Muse's workers), not inside another agent's separate session if it lives elsewhere. An agent that lives elsewhere can still reuse the pattern, but the turn it triggers starts here.
