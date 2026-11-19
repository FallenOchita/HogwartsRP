# Rust Central Account Portal — Login and Sessions

## What is included

The Rust host serves a responsive, same-origin account dashboard at `/` and `/account`.
It shows signed-in state and the central account ID, displays session expiry, and
provides a CSRF-protected sign-out action. The page uses a same-origin JavaScript
asset, a restrictive Content Security Policy, no-store caching, and text-only DOM
updates for account data.

Discord, Steam, and Epic Games are shown as optional identity connections, but
the dashboard deliberately labels linking as pending until the Rust portal is
connected to the already implemented provider verification handlers. It does
not display fabricated connected/disconnected states or pretend that disabled
controls can link accounts.

The Rust workspace now contains a small standalone Axum host at
`rust/apps/hogwartsrp-account-portal`. It is separate from the C# per-game-server
OIDC join-ticket flow.

Routes:

- `GET /` and `GET /account` serve the account dashboard; its same-origin script
  calls the session endpoint and performs sign-out.
- `GET /account/assets/account.js` serves the dashboard behavior with no-store
  caching and a JavaScript content type.
- `GET /account/login` creates a short-lived OIDC transaction and redirects to
  the configured provider using authorization-code flow and PKCE S256.
- `GET /account/callback` requires the pre-authentication cookie, consumes
  state once, exchanges the code server-side, validates the ID-token signature,
  issuer, audience, expiry and nonce, then resolves the account from the verified
  issuer + subject pair.
- `GET /account/session` returns the authenticated account ID, CSRF token and
  expiry. The bearer session cookie is HttpOnly; the separate CSRF cookie is
  readable by same-origin portal code.
- `POST /account/logout` requires the session cookie and
  `X-HogwartsRP-CSRF` header, revokes that session and clears both cookies.

All account responses are no-store. Cookies are Secure, HttpOnly for the bearer,
SameSite=Lax, scoped to `/account`, and have a 12-hour lifetime. The temporary
pre-auth cookie is short-lived and is used only to bind the OIDC state transaction
to the browser that initiated sign-in. Provider errors are intentionally generic.

## Run locally

1. Copy `config/portal-oidc.example.json` to a private local
   `config/portal-oidc.json` and fill in the issuer, client ID, and exact callback
   URI registered with the provider.
2. For local development only, use a loopback HTTP issuer/callback and set
   `AllowLoopbackHttp` to true. Production must use HTTPS.
3. Run from the repository root:

   ```sh
   cargo run --manifest-path rust/Cargo.toml -p hogwartsrp-account-portal
   ```

Configuration/environment:

- `HOGWARTSRP_PORTAL_OIDC_CONFIG`: config path (default
  `config/portal-oidc.json`).
- `HOGWARTSRP_PORTAL_ACCOUNTS_PATH`: identity JSON path (default
  `data/portal-accounts.json`).
- `HOGWARTSRP_PORTAL_LISTEN`: bind address (default `127.0.0.1:8787`).

Terminate TLS at a trusted reverse proxy for non-loopback deployments. Do not
expose the service directly to the public internet without TLS, rate limiting,
monitoring, and deployment hardening.

## Current limitations

- Browser sessions and OIDC transactions are process-local. Restarting the portal
  invalidates sessions and pending sign-ins. Multiple instances are not supported.
- The JSON identity store is a single-process development adapter, not a
  production database. It stores only the verified issuer, subject and generated
  HogwartsRP account ID; it does not store provider tokens.
- Provider account-link UI integration, distributed sessions, migrations,
  rate limiting and production deployment are follow-up work.
- This host does not issue per-server join tickets and is not wired into any
  individual HogwartsRP game server.

The OIDC client secret, when configured, remains server-side in the private
configuration file. Never commit that file or log authorization codes, cookies,
tokens, client secrets, or full callback query strings.
