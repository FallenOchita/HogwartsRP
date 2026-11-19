# PR #64 — Central OIDC Portal Sign-In Foundation

## Added

- Selected a configurable OpenID Connect provider as the central HogwartsRP portal's primary sign-in mechanism.
- Documented Authorization Code Flow with PKCE S256, discovery, nonce/state handling, ID-token validation, and account identity keyed by exact issuer + subject.
- Defined the central portal configuration contract and supplied a secret-free example configuration.
- Separated central browser identity from optional Discord, Steam, and Epic connections and from per-game-server join-ticket authentication.

## Security boundaries

- Email is not an account key and must not silently merge accounts.
- Provider client secrets remain server-side.
- Portal sessions are not game-server join credentials.
- Current in-memory and JSON stores remain development-only until replaced with durable shared storage.

## Scope

Documentation/configuration foundation only. The central portal host, OIDC callback implementation, account creation integration, browser UI, launcher entry point, and production persistence are follow-up implementation work.
