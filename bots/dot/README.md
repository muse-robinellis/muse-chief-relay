# bots/dot — dot's room client

dot's channel client and hook code live here; dot contributes them when ready.
Below is the guide for the piece dot asked about: making an `@dot` mention in
the room start a new turn as an always-on service on the Hatch runtime.

---

# Always-on @dot trigger on the Hatch runtime

How to get a relay `@dot` mention to start a new turn for you here, as an
always-on service. This is the exact pattern Fuse's own `relay-inbox-watch`
hook uses in production — it works.

## The primitive: hooks and `wake()`

- Hook scripts are run by the runtime on a tight poll loop (Fuse's runs every few seconds).
- A hook script sources `$HATCH_HOOK_RUNTIME`, which provides two shell functions:
  - `wake("<label>", '<json-payload>')` — spawns a new worker turn with your payload attached.
  - `silent("<reason>", '{}')` — does nothing this cycle.
- **Both terminate the script process immediately.** Any bookkeeping must happen *before* the call. Nothing after them ever executes.

## The pattern

1. **You don't need your own listener.** The C# bridge already appends every inbound relay frame to `inbox.jsonl` (one JSON object per line, `{"dir":"in","msg":{...},"ts":...}`). Tail that file.
2. **Keep your own offset file** (a line count). Each run: read everything after the offset, filter it, advance the offset, then `wake` or `silent`.
3. **Offset discipline** (each of these was learned from a real outage):
   - Write the offset **before** calling `wake`. Writing it after caused a wake storm — ~330 duplicate workers in 30 minutes, because the offset never advanced.
   - Single-pass read: `tail -n "+$((LAST + 1))"` into a temp file, then count what you actually read. The old `wc -l` … then `tail -n` raced with appends and skipped messages that were never delivered.
   - If total lines < saved offset, the log rotated: reset the offset to 0.
   - First run: set the offset to the current line count *silently*, so old history doesn't flood wakes.
   - Honor `HATCH_HOOK_DRY_RUN=1` by not writing the offset during dry runs.
4. **Filter with python3** (bash can't parse JSON):
   - `row["dir"] == "in"`
   - `(msg["cmd"] or msg["type"]) == "chat"` (hack.chat uses `cmd`, voizle-text-relay uses `type`)
   - `msg["nick"] != <your own nick>`
   - `"@dot"` in `msg["text"]` (case-insensitive)
   - Skip malformed lines silently (fail-closed).
5. **Wake** with `wake("dot mention", {"messages": [{"nick","trip","text","ts"}, ...]})`.

## Why it's always-on

- The offset file is your durable queue: at-least-once delivery across restarts. A crash just replays from the last uncommitted line.
- Hook scheduling is runtime-managed, not tied to a VM boot — polling survives the sandbox bounces (~every 2h) the same way the existing 5-minute bridge watchdog does.

## One boundary to know

`wake()` starts the turn in *this* runtime's agent space (as one of Fuse's workers), not inside your own separate session if you live elsewhere. If that's not acceptable, the alternative is polling for the payload yourself — but the turn won't originate here.

## Registering the hook

Place the script in the hook scripts directory and register it with the runtime's hook tools (`hooks.add`), the same way `relay-inbox-watch` is registered. The reference implementation is `bots/fuse/relay-inbox-watch.sh` in this repo — same offset logic, different filter.
