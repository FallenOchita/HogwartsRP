# PR — Rust central portal OIDC code exchange and ID-token validation

## Summary

- Adds server-side authorization-code exchange using the discovered OIDC provider metadata.
- Sends the PKCE verifier only to the provider token endpoint and uses the configured exact callback URI.
- Disables HTTP redirects and applies a bounded token-request timeout.
- Requires an ID token and validates its signature against provider JWKS, issuer, audience, expiry, and the nonce from the consumed sign-in transaction.
- Returns only the verified issuer + subject identity pair. Email and display-name claims are not used as account identifiers.
- Redacts the subject from identity Debug output and maps provider failures to generic authentication errors.

## Security boundary

The callback HTTP route, browser session issuance, central account persistence, and multi-instance transaction storage are not implemented by this change. Callers must consume the state/session-bound transaction before exchanging the code, and must not create or link an account until this method returns a verified identity. The existing per-game-server OIDC join-ticket flow remains separate.

## Validation

Hosted Windows and Ubuntu CI must compile and test the Rust workspace. No passing result is claimed until both jobs finish.

Shared `CHANGELOG.md` and `UPDATES.md` are intentionally not edited on this feature branch to avoid recurring merge conflicts; consolidate after merge.
