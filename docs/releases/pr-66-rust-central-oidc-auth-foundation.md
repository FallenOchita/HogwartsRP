# PR #66 — Rust central OIDC authentication foundation

## Summary

- Adds the `hogwartsrp-portal-auth` Rust workspace crate for central-account OIDC configuration.
- Validates the issuer and callback as absolute HTTPS URLs, with HTTP allowed only for explicitly enabled loopback development.
- Requires a non-empty client ID and a unique scope list containing `openid`.
- Adds provider metadata discovery through the `openidconnect` crate, with redirects disabled and an eight-second request timeout.
- Keeps the optional client secret private and exposes only whether one is configured.

## Security boundary

This crate is for central browser-account sign-in. It does not replace or alter per-game-server join-ticket OIDC. It does not treat email as an account identifier and does not create accounts or browser sessions.

The provider discovery result is not authentication. Authorization-code + PKCE transaction handling, token exchange, ID-token signature/claim validation, account resolution, session rotation, and HTTP routes remain subsequent implementation work.

## Validation

The crate includes unit tests for HTTPS enforcement, explicit loopback development mode, callback URL restrictions, client ID validation, and OIDC scope validation. Hosted CI results are pending.

Shared `CHANGELOG.md` and `UPDATES.md` are intentionally not edited on this feature branch to avoid the recurring merge conflicts. Consolidate the release note into the shared logs after merge.
