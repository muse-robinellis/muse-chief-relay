# Changelog

Merged work, newest first. Times are ET.

## Unreleased

- **Chief.Bridge speaks voizle-text-relay v1.** When `url` is not a hack.chat host, the bridge waits for `hello` (`protocol` `voizle-text-relay`, `v` 1) and only then sends `{"v":1,"type":"join","room","nick","trip?"}`. It does not send a hack.chat `cmd`/`channel` join, and it does not send `pass`. Optional `trip` is a public code (`Ab12Cd` or `!Ab12Cd`); the wire value is `!` plus the six characters. A password in `trip` is a bad config (exit 2). `welcome` confirms the join. `error` before that is a rejected join. Chat and auto-acks go out as `{"v":1,"type":"chat","text"}`. Inbound `type`/`room` frames are stored with `cmd`/`channel` as well, and a leading `!` is stripped from trips so watch, hook, auto-ack, and `tools/status.py` match allowlists that already drop it. `welcome.replay` chats are logged and watched, but not auto-acked, and a `welcome` with no trip clears a trip remembered from an earlier session. hack.chat URLs still join with `cmd`/`channel` and wait for `onlineSet`.
