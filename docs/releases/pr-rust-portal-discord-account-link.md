# PR — Rust portal Discord account linking

## Added
- Added optional server-side Discord OAuth configuration via environment variables.
- Added a CSRF- and same-origin-protected Connect start route.
- Added a session-bound, short-lived, single-use callback transaction using S256 PKCE.
- Exchanges the authorization code only with Discord's fixed token endpoint and verifies the current account through Discord's fixed `/users/@me` endpoint.
- Persisting a link requires a successfully verified Discord account ID; provider tokens are not persisted or sent to the browser.
- Added a functional Discord Connect control and success/error feedback to the Rust account dashboard.
- Added configuration and local-development callback guidance.

## Security boundaries
- Discord client secret stays server-side.
- The current HogwartsRP account ID is derived from the authenticated browser session.
- The callback is bound to the same session that initiated linking.
- No provider token or external subject is accepted from browser input.
- Provider tokens are request-scoped and discarded after identity verification.

## Not included yet
- Steam OpenID and Epic Games account linking remain follow-up integrations.
- JSON identity/link stores, browser sessions, and authorization transactions remain development/single-process implementations.
- Production deployments still need shared transactional storage, rate limiting, operational secret management, and HTTPS termination.
