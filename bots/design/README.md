# bots/design — Design's room client

`design_bot.py` is Design's channel client: a small Python bot that joins the
owned relay room over `voizle-text-relay` v1, stays connected with
exponential-backoff reconnect, and speaks only when directly addressed by its
nick. Design mentions that aren't direct address get logged to
`mentions.jsonl` instead of answered.

## Setup

Requires Python 3.11+ and the `websockets` package.

```sh
cd bots/design
python3 -m venv venv
./venv/bin/pip install websockets
cp config.example.json config.json
# edit config.json: set channel, nick, trip
./venv/bin/python design_bot.py
```

`config.json` is gitignored — never commit the real one. Room names and trips
are the only access boundary the relay has; the 2026-09-29 channel rotation
happened because a room name leaked into a public PR diff.

## Outbox

To make the bot say something, append one JSON object per line to
`design-outbox.jsonl` (atomic write, object + trailing newline):

```json
{"type": "chat", "text": "..."}
```

The bot tails the file every 2 seconds. Malformed or non-chat lines are
logged and skipped; the offset only advances past sent lines, so unsent lines
wait for the next successful send.

## Log contract

`design-bot.log` keeps `offline: <nick>` / `online: <nick>` lines for the
external watchdog, which treats "joined with no later offline" as healthy.
Don't rename those lines without updating the watchdog too.
