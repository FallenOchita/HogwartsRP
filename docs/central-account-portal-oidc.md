# Central Account Portal: Configurable OpenID Connect Sign-In

## Decision

The central HogwartsRP browser portal uses one configurable OpenID Connect (OIDC) provider as its primary sign-in mechanism. Discord, Steam, and Epic Games remain optional, independent account connections managed after sign-in. None of those linked providers is required to create or access a HogwartsRP account.

This is a central-account concern. It must not be wired into each game server's join-ticket OIDC configuration or change the existing per-server authentication flow.

## Identity model

- The stable primary identity key is the exact pair `(issuer, subject)` from a successfully validated ID token.
- The issuer is the configured issuer, verified against OIDC discovery metadata and the token's `iss` claim.
- The subject is the token's non-empty `sub` claim. Display names, email addresses, and usernames are profile attributes, never account keys.
- Email equality must never automatically merge two HogwartsRP accounts or attach a provider connection.
- A returning `(iss, sub)` resolves to the same central HogwartsRP account. A previously unseen pair creates an account according to the portal's configured registration policy.
- Discord, Steam, and Epic linking stays in `JsonAccountIdentityStore` and is authorized only from the authenticated central account session.

## Authorization-code flow

The central portal must implement Authorization Code Flow with PKCE (S256):

1. The browser requests sign-in from the portal. The portal creates cryptographically random, single-use `state`, `nonce`, and PKCE verifier values.
2. The portal discovers the provider's authorization, token, and JWKS endpoints from the configured issuer's `/.well-known/openid-configuration` document. Discovery metadata must report the exact configured issuer.
3. The browser is redirected to the discovered authorization endpoint with `response_type=code`, `scope=openid profile`, configured client ID, exact registered callback URI, `state`, `nonce`, and S256 challenge.
4. The callback validates and atomically consumes the short-lived state transaction before exchanging the code at the discovered token endpoint. The PKCE verifier and exact configured redirect URI are sent server-side.
5. The portal validates the ID token signature against the issuer's JWKS and validates `iss`, `aud`, `azp` when applicable, `sub`, `nonce`, `exp`, `iat`, and `nbf`. Reject unsupported algorithms and key types. Do not accept an ID token supplied directly by browser input.
6. The portal resolves or creates the account using only validated `iss` + `sub`, issues a new opaque browser session, rotates any prior session cookie, and redirects to a fixed local portal destination.
7. Authorization codes, ID tokens, access tokens, refresh tokens, state, nonce, and PKCE verifiers are never written to application logs or returned in URLs after callback processing.

The portal must reject duplicate callback parameters, missing or mismatched state/nonce, expired transactions, issuer mismatch, audience mismatch, invalid signatures, and provider errors. Transactions are single-use even when the provider callback fails after state validation.

## Configuration contract

The central portal has its own configuration namespace, separate from per-game-server settings:

| Setting | Required | Purpose |
| --- | --- | --- |
| `Portal:PublicBaseUrl` | Yes | Canonical HTTPS origin for the browser portal |
| `Authentication:PortalOidc:Authority` | Yes | Exact OIDC issuer URL; used for discovery |
| `Authentication:PortalOidc:ClientId` | Yes | OIDC client identifier |
| `Authentication:PortalOidc:ClientSecret` | Provider-dependent | Confidential-client secret, stored server-side only |
| `Authentication:PortalOidc:CallbackUri` | Yes | Exact HTTPS redirect URI registered at the provider |
| `Authentication:PortalOidc:Scopes` | No | Defaults to `openid profile`; additional scopes must be explicitly configured |
| `Authentication:PortalOidc:DisplayNameClaim` | No | Preferred display-name claim; defaults to `name`, with safe fallbacks |

The callback URI must be absolute HTTPS, contain no user info or fragment, and be registered exactly with the identity provider. Never construct it from an untrusted `Host` or forwarded-host header. The authority must be HTTPS in production; loopback HTTP may be allowed only in explicit local development mode.

Secrets must come from environment variables or a deployment secret manager, not committed configuration, launcher settings, or browser JavaScript. The portal must not expose the client secret, token endpoint credentials, or signing-key material.

## Browser session

After successful sign-in, the portal issues its own random opaque session and CSRF token through the existing `BrowserAccountSessionStore` contract. The session cookie remains `Secure`, `HttpOnly`, `SameSite=Lax`, and scoped to `/account`; all account mutation endpoints continue to require CSRF and same-origin checks. The sign-in callback is a top-level GET return, so Lax is intentional.

The callback must rotate the session rather than preserving an unauthenticated/pre-login session identifier. Session storage is currently process-local; it is a development foundation only until replaced with a durable shared store suitable for multiple portal instances.

## Portal and launcher responsibilities

- The browser portal owns sign-in, sign-out, account profile, and the Discord/Steam/Epic connection page.
- The launcher opens the configured portal URL in the system browser. It does not collect provider passwords, host OAuth client secrets, or implement a second independent login UI.
- The launcher may learn the signed-in account state through a future short-lived, narrowly scoped handoff. It must not receive the portal's browser session cookie.
- Game servers continue to authenticate their own player sessions and issue their own one-time join tickets. Central portal sessions are not accepted as game-server join credentials.

## Deployment and security requirements

- Use TLS at the portal edge and configure trusted reverse proxies explicitly.
- Enforce HTTPS discovery and endpoint URLs, exact issuer matching, bounded HTTP response sizes, request timeouts, and disabled automatic redirects for token/JWKS requests.
- Validate ID-token signatures and claims on the server. Pin allowed algorithms; do not trust `alg` from an unverified header.
- Use short-lived, bounded, single-use login transactions. Store hashes of state/session secrets where possible; protect PKCE verifiers and nonce values as server-side transaction data.
- Apply rate limits to sign-in start/callback, account creation, and session endpoints.
- Do not use email as an account key or silently merge accounts. Account recovery and identity-provider changes require a separately designed, verified process.
- Persist accounts, primary OIDC identities, login transactions, and browser sessions in durable storage before production or multi-instance deployment. The current JSON and in-memory stores are single-process development components.
- Add security audit events without logging tokens, codes, secrets, or raw assertions.
- Provide an operator-controlled registration policy (open, invite-only, or closed) before public launch.

## Implementation sequence

1. Add a dedicated central portal host, separate from the per-game server executable.
2. Add configurable OIDC discovery, authorization-code + PKCE transaction handling, token exchange, and strict ID-token validation.
3. Add atomic `(issuer, subject)` account lookup/creation and secure browser session issuance.
4. Wire the existing account-link handlers into the central host and expose a browser Account Connections page.
5. Add a launcher action that opens the configured portal in the user's browser.
6. Replace process-local stores with durable shared storage and add deployment/rate-limit/audit controls before public production use.

## Current scope of this design milestone

This document records the selected architecture and configuration contract. It does not claim that the central portal host, OIDC callback, browser UI, launcher handoff, or production persistence is implemented. The existing per-game-server OIDC join-ticket flow and the already implemented Discord/Steam/Epic verification endpoints are unchanged.
