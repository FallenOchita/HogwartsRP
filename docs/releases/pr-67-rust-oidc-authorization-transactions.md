# PR #67 — Rust OIDC authorization transaction store

## Summary

- Adds cryptographically random state, nonce, and PKCE verifier generation for central portal sign-in.
- Uses an S256 PKCE challenge; only the challenge and state are exposed to the browser redirect layer.
- Stores only a SHA-256 hash of the browser session identifier and a SHA-256 hash of the state key.
- Binds each transaction to the initiating browser session.
- Enforces a five-minute expiry, bounded capacity, expired-entry cleanup, and atomic single-use consumption.
- Keeps nonce and verifier server-side and redacts them from Debug output.

## Security boundary

This is an in-memory, single-process transaction store. Multi-instance deployments must use a shared store with atomic consume semantics. The callback still needs to exchange the authorization code and validate the ID token (signature, issuer, audience, expiry, nonce) before resolving or creating an account. No account or browser session is issued by this change.

The store is separate from per-game-server join-ticket authentication and does not modify the existing C# Discord/Steam/Epic linking endpoints.

## Validation

Unit tests cover S256 challenge generation, browser-session binding, wrong-session rejection without consuming the rightful transaction, single-use consumption, expiry, bounded capacity, and empty session rejection. Hosted CI results are pending.

Shared `CHANGELOG.md` and `UPDATES.md` are intentionally not edited on this feature branch to avoid recurring merge conflicts. Consolidate after merge.
