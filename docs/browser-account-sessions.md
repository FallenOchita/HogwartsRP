# Browser Account Session Foundation

## Scope

This change adds a process-local session registry for authenticated HogwartsRP browser accounts. It is a security primitive for the future central account portal; it does not add a login route, cookie middleware, provider callback, account-link endpoint, or browser UI.

## Session lifecycle

- Trusted server-side authentication code supplies the already-resolved central Guid account ID to Issue.
- The store generates independent cryptographically random 256-bit bearer and CSRF tokens.
- Only SHA-256 digests of the bearer token and CSRF token are retained by the store. The raw values are returned once to the trusted caller so the future HTTP layer can set an HttpOnly cookie and return the CSRF token to the browser.
- Session resolution returns only the account ID and timestamps. The browser never selects or overrides the account ID.
- Mutating browser endpoints must validate the session and compare the CSRF token in constant time.
- Revocation is limited to the supplied session token. Expired entries are rejected and can be pruned.
- Session lifetime is fixed, between five minutes and 24 hours (12 hours by default); there is no sliding renewal.

## Required HTTP integration rules

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

BrowserAccountSessionStoreTests covers account binding, malformed/unknown tokens, CSRF pairing, session-specific revocation, expiry, bounded capacity, and configuration validation. These tests do not validate browser cookies, HTTPS deployment, provider authentication, or a live central portal.
