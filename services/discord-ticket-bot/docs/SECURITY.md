# Security and Production Hardening

- Store bot tokens and MongoDB credentials in a secret manager; never commit .env.
- Use a dedicated bot role with only View Channels, Send Messages, Embed Links, Attach Files, Read Message History and Manage Channels. Do not grant Administrator.
- Every command and button checks authorization in code. Discord command visibility is not treated as the security boundary.
- Ticket channels deny ViewChannel to @everyone and grant access only to the opener, configured department role, assigned staff and bot. Server administrators retain Discord's Administrator bypass.
- HTML transcripts escape message content and attachment URLs are preserved as links. Restrict transcript storage and log-channel access because tickets may contain personal information.
- Persistent MongoDB per-user state enforces the open-ticket cap and cooldown across bot restarts and multiple instances. An active-ticket partial unique index prevents duplicate same-type tickets.
- On startup, open-ticket counters are reconciled from Ticket records.
- Configure MongoDB authentication, TLS where applicable, private networking, backups, and least-privilege database credentials.
- Set a retention policy for closed tickets, transcripts and audit records; deletion must account for legal/community policy.
- Add operational monitoring for Discord API errors, MongoDB latency, transcript upload failures, and interaction failures.
- Before public launch, test role hierarchy, category overwrite behavior, transcript file size limits, rate limits, bot restart recovery and simultaneous ticket creation.
