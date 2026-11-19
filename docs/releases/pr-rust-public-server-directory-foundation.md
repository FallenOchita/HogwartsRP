# PR — Public Server Directory Foundation

## Added

- Standalone Rust directory API and host application.
- SQLite-backed server listings with WAL mode and a stable public listing schema.
- Operator-authorized registration that returns a one-time per-server bearer credential while storing only its SHA-256 digest.
- Authenticated heartbeat updates with player-count capacity validation.
- Automatic stale-heartbeat expiry in public listings.
- Bounded pagination, optional game/name filters, and privacy-limited listing responses.
- Public endpoint validation for private, loopback, link-local, multicast, reserved IP ranges, and local/test hostnames.
- Unit tests for endpoint validation and bearer parsing.

## Scope and limitations

- Registration is operator-controlled; verified server-owner claims are not implemented.
- Credential rotation/revocation management endpoints and launcher public-server UI are follow-up work.
- The service is not deployed, and no live server heartbeat or endpoint reachability check has been tested.
- Shared `CHANGELOG.md` and `UPDATES.md` are intentionally omitted from this feature branch to avoid recurring merge conflicts.
