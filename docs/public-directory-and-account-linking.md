# Public Directory and Player Account Linking — Design Foundation

## Purpose

This document defines the next HogwartsRP milestone before provider credentials, database choices, and deployment infrastructure are introduced. It covers two related but separate user-facing capabilities:

1. A public server directory that lets players discover HogwartsRP servers and inspect live aggregate status.
2. A browser-based player account page where a player can independently link Discord, Steam, and Epic Games accounts to their existing HogwartsRP identity.

This is a design foundation, not a claim that either feature is implemented. The current `/status` endpoint and saved launcher profiles remain separate: the former reports one server's aggregate status, and the latter stores only a local list.

## Existing foundation and constraints

- The current launcher authenticates to a selected game server using the existing server-specific OIDC join flow. That identity is not yet a central HogwartsRP website session.
- The current server `GET /status` endpoint is public and read-only. It reports server name, online count, capacity, and a timestamp.
- The current launcher stores saved server profiles locally. It does not discover public servers.
- Server OIDC account IDs are derived from issuer plus subject. Do not assume two unrelated servers produce a globally shared player identity.
- The Windows launcher is the first browser-opening client. A browser account portal still needs a central web identity/session service and deployment origin.
- Do not add provider secrets, API keys, OAuth tokens, or refresh tokens to the launcher, source control, logs, or public directory responses.

## Target architecture

### Central HogwartsRP account service

A centrally hosted account service owns the browser session and player account identity. It is distinct from an individual community server's join-ticket/OIDC configuration.

The browser account portal authenticates the player to this central service. Account-link start, callback, status, and unlink operations are always scoped to the authenticated central account. The browser must never submit an arbitrary HogwartsRP account ID as the owner of a link.

### External account linking

Each provider link is an identity proof, not an automatic login replacement and not a grant of administrative permissions.

| Provider | Verification approach | Minimal identity data |
|---|---|---|
| Discord | OAuth 2 authorization-code flow; use state and PKCE where supported, validate issuer/redirect and retrieve the authenticated user through Discord's API | Stable Discord user ID; display name only if needed |
| Steam | Steam OpenID 2.0 browser flow; validate the returned assertion with Steam before accepting the claimed SteamID | 64-bit SteamID |
| Epic Games | Epic Games Account Services OAuth 2 authorization-code flow using a registered web redirect and server-side code exchange | Stable Epic account identifier; display name only if needed |

Implementation requirements:

- Require a fresh, authenticated HogwartsRP browser session before beginning a link.
- Generate a cryptographically random, short-lived, single-use state value bound to the account session, provider, and callback.
- Use PKCE for authorization-code providers where supported. Validate the exact registered redirect URI and expected issuer/provider.
- Verify provider identity on the server. Never trust a client-supplied provider ID, username, avatar URL, or callback result without verification.
- Use the provider's stable subject/ID as the identity key. Usernames and display names are mutable and are not unique keys.
- Enforce a database uniqueness constraint on `(provider, provider_subject)` so one external account cannot silently attach to multiple HogwartsRP accounts.
- Enforce one link per provider per HogwartsRP account unless a later product decision explicitly changes that rule.
- Do not request email, friends, guild membership, or other scopes unless a separately approved feature needs them. Initial Discord linking needs identity only.
- Do not retain provider access/refresh tokens if the feature only needs a verified identity. If a future feature requires tokens, encrypt them at rest, narrowly scope them, rotate/revoke them, and never return them to the browser.
- Unlinking requires an authenticated session and CSRF protection. Preserve the HogwartsRP account even when all external links are removed.
- Do not use linked accounts as authorization roles. Discord server membership, Steam ownership, and Epic entitlements are separate checks and require their own explicit server-side verification.
- Record link/unlink audit events without logging authorization codes, tokens, secrets, or full callback query strings.
- Provide clear connected/disconnected state, provider display label, and a safe disconnect action in the Account Connections page.

### Public server directory

The directory is a separate centrally hosted API and persistent store. It must not be implemented as launcher-side scraping or as a list hardcoded into the client.

Expected directory responsibilities:

- Publish only servers that have completed an authenticated registration/claim process.
- Give each registered server a revocable server credential for directory registration and heartbeat updates. Store only a hash of the credential at rest.
- Accept server name, public endpoint, supported game/platform metadata, maximum players, and optional description under strict validation.
- Accept authenticated heartbeats and aggregate online counts. Expire stale servers after a configurable timeout and show them as offline rather than inventing availability.
- Probe or validate endpoint reachability separately from heartbeat claims. Clearly distinguish last heartbeat from a successful reachability check.
- Keep public listing responses limited to public server metadata and aggregate counts. Never expose player names, account IDs, session IDs, credentials, or private owner data.
- Support server-owner management through a verified owner claim flow. Owners can edit their own listing, rotate/revoke server credentials, and remove the listing. A client-provided owner ID is never sufficient authorization.
- Rate-limit registration, updates, heartbeat, and search endpoints; validate public endpoints to prevent SSRF and reject loopback, link-local, private, multicast, and reserved targets for public listings.
- Provide pagination, bounded query sizes, deterministic ordering, and no-store or short-cache semantics appropriate to each endpoint.

The launcher will add a Public Servers view that queries this API, shows listing details and live aggregate status, and lets the player save a selected server locally. Selecting a listing must still connect to that server's own authenticated join endpoint; directory presence is not a join credential.

## Browser account portal

The Account Connections page is hosted by the central account service and opened from the launcher. It should show three independent optional rows:

- Discord — Connect / Connected / Disconnect
- Steam — Connect / Connected / Disconnect
- Epic Games — Connect / Connected / Disconnect

A provider being unavailable or unconfigured must not block the other providers or ordinary HogwartsRP sign-in. The page must explain that linking verifies account identity only; it does not imply ownership of Hogwarts Legacy, grant game access, or add the player to a Discord server.

## Delivery sequence

1. **Design and security foundation (this change):** record trust boundaries, provider flows, directory registration/heartbeat requirements, privacy rules, and implementation gates.
2. **Central account service foundation:** central browser sign-in/session, durable account/link persistence, CSRF protections, and account ownership tests.
3. **Provider linking:** implement and test Discord, Steam, and Epic verification independently, including replay, cross-account, duplicate-link, cancellation, and unlink tests.
4. **Account Connections UI:** browser page plus launcher entry point; verify all three optional connections and disconnected/error states.
5. **Directory service:** persistent listing store, server registration/claim, revocable server credentials, authenticated heartbeat, expiry, public search/status API.
6. **Launcher public browser:** searchable directory, listing detail, aggregate status, save-to-local-profile, and connection handoff.
7. **Deployment and operational hardening:** HTTPS, secret management, migrations/backups, rate limits, monitoring, abuse handling, and production smoke tests.

Do not skip the central account service and identity ownership foundation by adding provider callbacks directly to individual community game servers. Do not claim the directory is live until a hosted directory endpoint and a registered server heartbeat have been tested.

## Required test matrix

### Account linking

- Valid link succeeds only for the account session that initiated it.
- Missing, expired, replayed, or mismatched state is rejected.
- Provider issuer, audience/client, redirect URI, and response are validated.
- A provider subject already linked to another HogwartsRP account is rejected without moving the existing link.
- Two concurrent link attempts cannot create duplicate provider links.
- Provider cancellation and provider outage leave the HogwartsRP account usable.
- Unlinking requires the authenticated account and CSRF protection.
- Logs and API responses never contain secrets, codes, or provider tokens.

### Directory

- Unauthenticated or invalid server credentials cannot register or heartbeat.
- Revoked credentials stop working.
- Stale heartbeats transition listings to offline.
- Player counts are bounded by configured capacity.
- Private/reserved targets and malformed endpoints are rejected.
- Public results never contain player identities or secret owner data.
- Pagination and rate limits bound resource use.
- A listed server still requires its own join authentication.

## Provider documentation

- Discord OAuth2: https://discord.com/developers/docs/topics/oauth2
- Steam user authentication and browser OpenID: https://partner.steamgames.com/doc/features/auth
- Epic Games Auth Web APIs: https://dev.epicgames.com/docs/web-api-ref/authentication

Provider documentation and registered application requirements must be rechecked when each provider adapter is implemented. No provider credentials are assumed to exist yet.
