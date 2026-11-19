# Central Account-Link HTTP Routes

## Scope

This change adds reusable ASP.NET Core route handlers for the central browser account service. It does not map them into the per-game-server process and does not create or deploy a public website. The future central account host must explicitly call `AccountLinkEndpoints.Map` after its session store, account store, authorization transaction store, and configured provider adapters are created.

## Routes

- `GET /account/links` — requires the central account session cookie and returns connected/disconnected state for Discord, Steam, and Epic. The response is `no-store` and is scoped to the account from the server-side session.
- `POST /account/links/{provider}/start` — requires the session cookie, paired `X-HogwartsRP-CSRF` header, and same-origin `Origin`. Creates a short-lived transaction and redirects to the selected provider.
- `GET /account/links/discord/callback` — consumes the matching session-bound state and exchanges the authorization code through the Discord verifier.
- `GET /account/links/steam/callback` — consumes the state carried in Steam's exact `openid.return_to` URL and verifies the complete OpenID assertion server-to-server.
- `GET /account/links/epic/callback` — consumes the matching state and exchanges the authorization code through the Epic verifier.
- `DELETE /account/links/{provider}` — requires the session cookie, CSRF header, and same-origin `Origin`; removes only the current account's link for that provider.

Provider route names are `discord`, `steam`, and `epic`. Unknown providers are rejected.

## Provider behavior

- Discord requests only the `identify` scope and uses the transaction's S256 PKCE challenge.
- Steam uses OpenID 2.0 identifier selection and carries the random transaction state inside the exact callback URL. The verifier checks the assertion with Steam before persisting the SteamID64.
- Epic uses the documented web authorization endpoint with `basic_profile`, `state`, and the registered HTTPS redirect URI. The Epic client secret remains in the server-side verifier; the browser receives only the non-secret client ID.
- Provider configuration is independent. A missing provider adapter or OAuth client ID makes only that provider unavailable; the other configured providers continue to work.
- Callback failures redirect to the configured portal with only a generic provider/result indicator. Authorization codes, tokens, provider error descriptions, and assertion contents are not copied into that redirect.
- Callback, status, and redirect responses are marked `no-store`; callback responses also set `Referrer-Policy: no-referrer`.

## Trust and deployment boundaries

- The central account ID is never read from route, query, or body input. It is derived from the validated browser session.
- Start and unlink mutations require both the session-paired CSRF token and a matching same-origin header.
- Callback state is single-use, short-lived, and bound to the initiating session, account, provider, and exact registered callback URI. The session cookie is Secure, HttpOnly, SameSite=Lax, and scoped to `/account`.
- Provider verification remains server-side. The adapters persist only provider-verified stable subjects; access and refresh tokens are not retained.
- Identity linking does not prove Hogwarts Legacy ownership, grant server membership, or grant administrative permissions.
- Configure callback URIs as exact HTTPS URLs registered with each provider. Keep Discord/Epic secrets in the central host's secret provider, never in the launcher or browser.
- These route handlers are not yet wired into a hosted central account service. The current browser-session, transaction, and JSON identity stores are process-local/single-process development foundations; production requires durable shared storage, secret management, HTTPS deployment, rate limiting, audit events, and operational monitoring.
- The Account Connections page and launcher entry point are still separate follow-up work.

## Validation

`AccountLinkEndpointsTests` covers Discord S256 redirect construction, CSRF rejection, independent provider configuration, rejection of callbacks without valid state, account-scoped unlinking, and account-scoped link status. Provider adapters retain their own provider-response verification tests. These tests do not exercise live provider credentials or a deployed browser portal.
