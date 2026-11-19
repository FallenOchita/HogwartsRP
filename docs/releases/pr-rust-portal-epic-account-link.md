# PR — Rust Portal Epic Games Account Linking

## Added

- Optional Epic EOS OAuth authorization-code linking in the Rust central account portal.
- A Connect Epic Games action and callback feedback in the account dashboard.
- A CSRF- and same-origin-protected start route bound to the authenticated browser session.
- Short-lived, single-use state consumption on callback.
- Server-side authorization-code exchange using HTTP Basic client authentication.
- Token introspection against Epic's fixed tokenInfo endpoint.
- Validation of active bearer status, configured OAuth client ID, and the 32-character hexadecimal Epic account ID.
- Bounded provider response reads, fixed HTTPS endpoints, disabled redirects, and request timeouts.
- Server-only configuration through the Epic client ID, client secret, deployment ID, and exact callback URI environment variables.

## Security boundaries

- Epic credentials and access tokens remain server-side and are not persisted.
- The browser cannot submit a HogwartsRP account ID or Epic account ID to create a link.
- Only an Epic account ID returned by a successful tokenInfo verification is stored.
- The flow uses session-bound single-use state. Epic's published web authorization-code flow documents state but does not document PKCE, so this implementation does not claim PKCE support.
- Epic identity linking does not prove Hogwarts Legacy ownership or grant game/server access.

## Limitations

- Automated tests cover configuration/redirect policy, authorization URL construction, and token-introspection identity validation. A live Epic OAuth round-trip has not been performed.
- Browser sessions, authorization transactions, and JSON identity/link stores remain development single-process adapters.
- Shared `CHANGELOG.md` and `UPDATES.md` are intentionally not edited on this feature branch to avoid the previously observed merge conflicts; this PR has a dedicated release note.
