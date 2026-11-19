# PR #57 — Discord account-link verification adapter

## Delivered

- Added a server-only Discord OAuth adapter that exchanges an authorization code using the matching transaction's PKCE verifier.
- Sends the configured Discord client ID and secret only from server code to Discord's fixed HTTPS token endpoint.
- Uses the returned bearer token only for a server-to-server request to Discord's authenticated `/users/@me` endpoint.
- Derives the provider subject from Discord's authenticated response, not browser-supplied IDs or display names.
- Calls `JsonAccountIdentityStore.LinkVerifiedIdentityAsync` only after both token exchange and current-user retrieval succeed.
- Does not persist provider access tokens or refresh tokens.
- Adds tests for PKCE submission, endpoint selection, bearer identity lookup, malformed responses, provider mismatch, token exchange failure, invalid authorization codes, and duplicate provider ownership.

## Security and integration boundary

The caller must consume a matching, unexpired `AccountLinkAuthorizationStore` transaction before calling the adapter. The transaction must have been bound to the initiating authenticated browser session and the exact registered HTTPS callback. The adapter does not itself expose an HTTP callback route, create a browser session, build the Discord authorization URL, or perform the initial portal sign-in.

Configure `ClientId` and `ClientSecret` only in server-side secret configuration. Do not put the client secret in launcher settings, browser JavaScript, public directory responses, or logs.

## Not yet delivered

- Discord authorization-start and callback HTTP routes.
- Account Connections browser UI.
- Steam OpenID verification.
- Epic Games OAuth verification.
- Production multi-instance persistence for sessions, link transactions, and account identities.

This adapter is a provider-verification building block, not yet an end-to-end user-facing Discord linking flow.

Shared `CHANGELOG.md` and `UPDATES.md` are intentionally unchanged on this feature branch to avoid recurring merge conflicts. Consolidate the release notes after merge.
