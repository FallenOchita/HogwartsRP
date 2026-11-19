# PR #53 — Complete Rust launcher service migration

## Added
- Port launcher profile, status, character-draft, game-detection, and Steam-launch services into the Rust launcher crate.
- Add browser OIDC authorization-code + PKCE with a loopback callback and server-issued join-ticket exchange.
- Add an authenticated WebSocket client for server session handshake, bounded event messages, and correlated character submission.
- Wire OIDC sign-in, server connection, character form, status checks, and game launch into the Rust desktop UI.
- Add Rust unit tests for profile persistence, character validation, endpoint policy, OIDC endpoint constraints, and network-message validation.
- Add a Windows CI step to execute the launcher crate's unit tests, in addition to its release build.

## Boundaries
- The server remains authoritative for ID-token validation, player identity, permissions, character validation, and persistence.
- The client does not validate or elevate identity claims locally.
- Continuous server-event receive, reconnect/backoff, auto-update, Epic launch, and public directory UI remain follow-up work.
- No in-game multiplayer or direct in-game character creation is claimed.

## Validation
- GitHub Actions validation is queued; results will be recorded after both Windows and Ubuntu jobs finish.
