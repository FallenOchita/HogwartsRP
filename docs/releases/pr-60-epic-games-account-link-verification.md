# Epic Games account-link verification

## Delivered in this change

- Added a server-side Epic Games OAuth authorization-code exchange using Epic's official EOS token endpoint.
- Uses HTTP Basic client authentication; the client secret remains server-side.
- Requests the minimal basic_profile scope for the configured EOS deployment.
- Introspects the returned access token through Epic's token-info endpoint.
- Requires an active bearer token issued to the configured OAuth client.
- Persists only the Epic account ID returned by Epic's verified token-info response; it does not store access or refresh tokens.
- Adds tests for successful exchange/introspection, inactive tokens, wrong client IDs, malformed token responses, invalid account IDs, provider mismatch, and invalid authorization codes.
- Aligns account-link transaction generation with Epic's published web flow: Discord uses PKCE S256; Epic's documented web authorization-code flow and Steam OpenID do not create PKCE values.

## Provider flow and trust boundary

The central account-link service must first consume the single-use transaction bound to the authenticated browser session, central account ID, Epic provider, and exact registered HTTPS callback URI. The caller then passes that transaction and callback authorization code to EpicGamesAccountLinkClient.

The adapter exchanges the code with Epic using server-held client credentials, then calls Epic's token-info endpoint. It accepts an identity only when Epic reports the token active, the token type as bearer, the configured client ID, and a valid account ID. The browser cannot choose the HogwartsRP account owner or the Epic account subject.

Epic identity linking is not proof of Hogwarts Legacy ownership, game entitlement, or server authorization. No provider credentials or tokens are persisted.

## Configuration

Configure these values only in the central account service's secret/configuration provider:

- Epic OAuth Client ID
- Epic OAuth Client Secret
- EOS Deployment ID

The Epic client must be configured with the redirect URI registered for the OAuth client. This PR does not add the HTTP authorization-start/callback routes, production secret binding, or the browser Account Connections UI.

## PKCE note

Epic's current EOS web authorization documentation describes the authorization-code flow, state, registered redirect_uri, Basic client authentication, deployment_id, and basic_profile scope, but does not document PKCE parameters for this web flow. This change therefore does not claim PKCE protection for Epic. The callback still requires the existing random, short-lived, single-use state bound to the authenticated browser session and exact callback URI. Revisit PKCE if Epic publishes support for it.

## Shared release logs

The shared CHANGELOG.md and UPDATES.md are intentionally not edited on this feature branch to avoid the recurring merge conflicts. Consolidate the entry after this PR is merged.
