# Central account-link HTTP endpoints

## Delivered

- Added central-account route handlers for provider connection status, authorization start, provider callbacks, and unlink.
- Added Discord OAuth authorization redirects with the `identify` scope and S256 PKCE.
- Added Steam OpenID 2.0 authorization redirects with state bound into the callback URL and server-side assertion verification.
- Added Epic web authorization redirects with `basic_profile` and session-bound state.
- Required the authenticated browser session, paired CSRF header, and same-origin check for start and unlink mutations.
- Kept account ownership server-derived from the session cookie; callback state remains single-use and bound to account, session, provider, and exact callback URI.
- Made provider configuration independent so an unconfigured provider does not block the other providers.
- Added route tests and deployment/security documentation.

## Boundaries

- Routes are reusable ASP.NET Core handlers and are not wired into the per-game-server host.
- No central account website has been deployed or connected to these handlers.
- Existing session, transaction, and JSON identity stores remain process-local development foundations.
- The browser Account Connections UI, launcher entry point, durable production storage, and live provider-credential smoke tests remain follow-up work.
- Linked provider identities are identity proofs only; they do not prove game ownership or grant server permissions.
