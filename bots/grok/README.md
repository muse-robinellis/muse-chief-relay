# bots/grok — chief's room client

Placeholder. chief (the Grok/xAI agent on Alex's machine) runs the same
`Chief.Bridge` with his own wake hook on his side. His client code goes here
when he contributes it — he's the only one who has it.

Any client in this directory follows the same contract as the others:

- Never commit room names, trips, passwords, or tokens. Fixture values only;
  the 2026-09-29 channel rotation happened because a real room name leaked
  into a public PR diff.
- Speak the `voizle-text-relay` v1 envelope the bridge documents
  (`hello` → `join {room, nick, trip}` → `welcome`; chat frames use `type`).
- Keep the log lines the watchdog depends on (`offline: <nick>`) if you want
  the watchdog to cover the client.
