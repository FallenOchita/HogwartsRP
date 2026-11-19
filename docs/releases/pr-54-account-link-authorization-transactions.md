# PR #56 — Account-link authorization transaction foundation

## Delivered

- Added `AccountLinkAuthorizationStore`, a bounded, process-local registry for short-lived provider-link transactions.
- Each transaction is bound to the authenticated browser session, central HogwartsRP account, provider, and exact HTTPS callback URI.
- State values are cryptographically random and only SHA-256 hashes are retained in memory.
- Discord and Epic Games authorization-code transactions receive PKCE S256 challenges; the verifier remains server-side and is returned only after a matching callback consumes the transaction.
- Steam OpenID transactions use the same state/session/callback protections without PKCE.
- Transactions expire after a configurable 1–15 minute lifetime (10 minutes by default), are single-use, and are capacity-bounded.
- Invalid provider, callback, session, account, and expired-state attempts are rejected. Mismatched attempts do not consume a still-valid transaction.
- Browser session cookies use `SameSite=Lax` so the authenticated session is present on the provider's top-level GET callback; the callback must still validate the one-time state and session binding. Cookie remains Secure and HttpOnly.

## Tests

Added tests for provider-specific PKCE behavior, verifier/challenge matching, session/account binding, exact callback matching, provider mismatch, single-use consumption, expiry, capacity, HTTPS callback validation, and the callback-compatible session cookie flags.

## Deliberate boundaries

This is the transaction-security foundation only. It does **not** yet implement provider authorization URLs, Discord OAuth callbacks, Steam OpenID assertion verification, Epic token exchange, account-link HTTP routes, a browser Account Connections page, central account sign-in, or production persistence. No provider ID is accepted from browser input. The next implementation step must add provider-specific server-side verification and only then call `JsonAccountIdentityStore.LinkVerifiedIdentityAsync`.

The store is process-local and is suitable only for a single development instance. A deployed multi-instance account service must move pending transactions to a shared, atomic store before production use.

Shared `CHANGELOG.md` and `UPDATES.md` are intentionally not edited on this feature branch to avoid the recurring cross-branch conflicts; this PR-specific release note is the consolidation source after merge.
