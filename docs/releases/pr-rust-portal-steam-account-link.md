# PR — Rust Portal Steam Account Linking

## Added

- Optional Steam OpenID 2.0 linking through the Rust central account portal.
- A session-bound, short-lived, single-use state transaction for the Steam browser redirect.
- CSRF and same-origin protection on the connect-start request.
- Exact callback URI and state validation, Steam endpoint and claimed-identity checks, signed-field checks, and response-nonce freshness validation.
- Server-to-server `check_authentication` verification against Steam's fixed HTTPS endpoint before storing the verified SteamID64.
- A Steam Connect action and callback feedback in the account dashboard.
- Optional `HOGWARTSRP_STEAM_REDIRECT_URI` configuration.

## Security boundaries

- No Steam client secret is required or stored.
- The browser cannot submit a SteamID to create a link.
- The Steam identity is linked only after Steam validates the assertion.
- Steam IDs and OpenID assertion values are not returned to the dashboard.
- A linked Steam account is identity proof only; it does not establish Hogwarts Legacy ownership or grant server access.

## Limitations

- The account/session/transaction JSON and in-memory stores remain development, single-process implementations.
- The callback has unit coverage for redirect configuration, URL construction, SteamID shape, and nonce timestamp handling; a live Steam round-trip has not been performed.
- Epic Games linking remains a later provider milestone.
