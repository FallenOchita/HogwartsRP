# PR #74 — Rust portal login callback and browser sessions

## Summary

- Adds a standalone Rust/Axum account portal host, separate from per-game-server OIDC join-ticket authentication.
- Adds login-start and callback routes using the existing state/session-bound, single-use authorization transaction store.
- Builds the provider authorization request with the configured callback URI, scopes, nonce and PKCE S256 challenge.
- Exchanges authorization codes server-side and accepts only the ID token's verified issuer + subject identity.
- Adds a JSON development account identity store keyed by verified issuer + subject.
- Adds bounded in-memory browser sessions with random bearer and CSRF tokens; only SHA-256 token digests are retained.
- Adds authenticated session inspection and CSRF-protected logout routes with Secure, HttpOnly, SameSite=Lax cookies and no-store responses.
- Hardens cookie parsing so malformed or duplicate security cookies fail closed.
- Adds a runnable Rust portal host, example OIDC configuration and deployment documentation.

## Security and deployment boundaries

The portal does not trust browser-supplied account IDs or unverified identity claims. It does not use email as an account key or persist provider tokens. The existing C# per-game-server OIDC join-ticket flow is unchanged.

This is a development host, not a production-ready deployment: account persistence is a single-process JSON adapter, browser sessions and OIDC transactions are process-local, and TLS must terminate at a trusted reverse proxy for non-loopback deployments. The browser account UI, distributed stores, rate limiting, monitoring, migrations and production deployment remain follow-up work.

## Validation

Hosted Rust formatting, workspace tests and the Windows/Ubuntu CI matrix must pass before merge. No passing result is claimed until CI completes.

Shared CHANGELOG.md and UPDATES.md are intentionally not edited on the feature branch to avoid recurring merge conflicts; consolidate this release note after merge.
