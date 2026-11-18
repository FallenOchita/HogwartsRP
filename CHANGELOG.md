# Changelog

All notable HogwartsRP changes are recorded here.

## [Unreleased]

### Added

- A harmless UE4SS Lua diagnostic mod for the Steam PC version, with installation and log-verification instructions.
- A local PowerShell helper that produces a sanitized UE4SS diagnostic report without uploading or exposing unrelated log content.
- Regression tests for sanitized UE4SS reports, including status detection, missing logs, optional report output, and exclusion of arbitrary log payloads.
- Core-owned player sessions with server-generated IDs, single-use join tickets, and per-resource client-event validation.
- A WebSocket server transport with bounded JSON messages and authenticated server-to-client delivery.
- A replaceable client network adapter and launcher credential-provider interface.
- Optional OIDC browser sign-in with PKCE, server-issued replay-resistant challenges, server-side ID-token validation, and short-lived join tickets.
- Mock-provider tests for OIDC signature and claim validation, challenge expiry, and single-use ticket issuance.
- A loopback-only operator development-ticket command for local protocol work.
- A preview launcher flow that authenticates to the selected server, requests Hogwarts Legacy launch, and keeps the protocol session open until the launcher exits.
- Lua resource APIs for registering client-to-server events and sending events to authenticated players.

### Changed

- The diagnostic report helper now emits only normalized known markers and constrained version tokens instead of echoing arbitrary log lines.
- CI now runs the PowerShell diagnostic-report regression tests on Windows and Ubuntu.

### Limitations

- When OIDC is not configured, the console launcher uses a development-ticket prompt for loopback servers only.
- OIDC sign-in requires issuer and public-client configuration on both launcher and server; RS256 is the only currently accepted ID-token signing algorithm.
- The launcher owns the current network session. Hogwarts Legacy itself has no compatible multiplayer adapter yet.
- Direct character-creation entry, game-client event transport, and in-game multiplayer remain unavailable.
