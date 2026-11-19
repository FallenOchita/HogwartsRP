# Changelog

All notable HogwartsRP changes are recorded here.

## [Unreleased]

### Added

- Added a launcher-side character creation draft model that trims and validates names, Hogwarts houses, roleplay text, and optional wand details before building a typed submission.
- Added dedicated launcher tests for valid draft mapping, invalid names/houses, wand-length bounds, and omitted optional fields.
- Added dedicated WebSocket client protocol tests covering concurrent character submissions with out-of-order responses and connection loss before a response.
- Added authenticated WebSocket character submissions backed by the JSON character repository, with server-derived account ownership and correlated results.
- Wired the character repository into server startup with a configurable `Storage:CharactersPath`.
- A harmless UE4SS Lua diagnostic mod for the Steam PC version, with installation and log-verification instructions.
- A local PowerShell helper that produces a sanitized UE4SS diagnostic report without uploading or exposing unrelated log content.
- Regression tests for sanitized UE4SS reports, including status detection, missing logs, optional report output, and exclusion of arbitrary log payloads.
- A JSON-backed character repository with atomic file replacement, duplicate-ID protection, and owner-filtered retrieval.
- Regression tests for character persistence across repository instances, owner isolation, duplicate IDs, and corrupt-store preservation.
- Core-owned player sessions with server-generated IDs, single-use join tickets, and per-resource client-event validation.
- A WebSocket server transport with bounded JSON messages and authenticated server-to-client delivery.
- A replaceable client network adapter and launcher credential-provider interface.
- Optional OIDC browser sign-in with PKCE, server-issued replay-resistant challenges, server-side ID-token validation, and short-lived join tickets.
- Mock-provider tests for OIDC signature and claim validation, challenge expiry, and single-use ticket issuance.
- A loopback-only operator development-ticket command for local protocol work.
- A preview launcher flow that authenticates to the selected server, requests Hogwarts Legacy launch, and keeps the protocol session open until the launcher exits.
- Lua resource APIs for registering client-to-server events and sending events to authenticated players.

### Changed

- Added the missing `HogwartsRP.GameIntegration.Abstractions` project reference to `HogwartsRP.Server`, resolving the character repository contract types during compilation.
- The diagnostic report helper now emits only normalized known markers and constrained version tokens instead of echoing arbitrary log lines.
- CI now runs the PowerShell diagnostic-report regression tests on Windows and Ubuntu.

### Limitations

- The launcher character creation draft is a reusable model, not a graphical character-creation screen; the launcher remains a console preview.
- The JSON character repository is a single-server persistence adapter; it is not a multi-instance database or backup system.
- Character submission is available through the authenticated launcher/server WebSocket protocol and JSON repository, but it does not create or modify a character inside Hogwarts Legacy.
- When OIDC is not configured, the console launcher uses a development-ticket prompt for loopback servers only.
- OIDC sign-in requires issuer and public-client configuration on both launcher and server; RS256 is the only currently accepted ID-token signing algorithm.
- The launcher owns the current network session. Hogwarts Legacy itself has no compatible multiplayer adapter yet.
- Direct character-creation entry, game-client event transport, and in-game multiplayer remain unavailable.
