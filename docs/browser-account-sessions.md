# Browser Account Session Foundation

## Scope

This change adds reusable HTTP handlers for inspecting and revoking an existing authenticated browser session, plus a secure cookie helper for a trusted sign-in callback. The route mapper is intended for a separately hosted central account service; it is deliberately not wired into the existing per-game-server WebSocket host, whose OIDC flow issues server-specific join tickets.

## Session lifecycle

- Trusted server-side authentication code supplies the already-resolved central Guid account ID to Issue.
- The store generates independent cryptographically random 256-bit bearer and CSRF tokens.
- Only SHA-256 digests of the bearer token and CSRF token are retained by the store. The raw values are returned once to the trusted caller so the future HTTP layer can set an HttpOnly cookie and return the CSRF token to the browser.
- Session resolution returns only the account ID and timestamps. The browser never selects or overrides the account ID.
- Mutating browser endpoints must validate the session and compare the CSRF token in constant time.
- Revocation is limited to the supplied session token. Expired entries are rejected and can be pruned.
- Session lifetime is fixed, between five minutes and 24 hours (12 hours by default); there is no sliding renewal.

## HTTP endpoints and cookie behavior\n\n- `GET /account/session` reads the `HogwartsRP.AccountSession` cookie and returns only the server-bound account ID and session timestamps. Missing, invalid, or expired cookies receive `401`.\n- `DELETE /account/session` requires the same cookie plus the `X-HogwartsRP-CSRF` header. A missing or mismatched CSRF token receives `403`; success revokes only that session, clears the cookie, and returns `204`.\n- Both routes set `Cache-Control: no-store` and `Pragma: no-cache`.\n- `SetSessionCookie` is a helper for trusted server-side sign-in completion code. It sets `Secure`, `HttpOnly`, `SameSite=Strict`, `Path=/account`, and the server expiry. It does not expose a public session-issuance endpoint.\n- `BrowserAccountSessionEndpoints.Map` is not called by the per-game server startup. A future central host must map it explicitly after its trusted authentication/session services are configured.\n\n## Required HTTP integration rules

When the central browser host is added, it must:

- Set the bearer token only in a Secure, HttpOnly, SameSite=Lax cookie scoped to the account portal, with an explicit expiry matching the server session.
- Return the CSRF token only in a no-store authenticated response and require it in a custom request header for every state-changing request, including unlink and logout.
- Derive the account ID exclusively from BrowserAccountSessionStore.TryGet; never accept an account ID in a request body, query string, or route parameter for account-owned operations.
- Clear the cookie on logout and revoke the matching server session.
- Require HTTPS outside loopback development, apply origin checks and rate limits, and avoid logging cookies or tokens.
- Keep provider OAuth/OpenID state separate from the browser session and bind each one-time provider state to the authenticated account session before completing a link.

## Deployment limitation

The registry is in-memory and local to one process. Restarting the service invalidates all sessions. Multiple central service instances require a shared session backend or sticky routing with an explicit operational design; production deployment should use a reviewed distributed session store. The current JSON account identity store is also single-process development storage.

## Validation

BrowserAccountSessionStoreTests covers account binding, malformed/unknown tokens, CSRF pairing, session-specific revocation, expiry, bounded capacity, and configuration validation. BrowserAccountSessionEndpointsTests covers unauthorized inspection, account-bound session responses, CSRF enforcement, logout revocation, cookie clearing, and secure cookie flags. These tests do not validate provider authentication, deployment TLS termination, or a live central portal.
