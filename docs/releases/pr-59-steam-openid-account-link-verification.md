# PR #59 — Steam OpenID account-link verification

## Delivered

- Added a server-only Steam OpenID 2.0 assertion verifier using Steam's fixed HTTPS OpenID endpoint.
- Requires the Steam provider, a valid account-link state, and a transaction without a PKCE verifier.
- Validates the OpenID namespace, provider endpoint, Steam claimed identity, identity equality, exact callback return URL, signed-field list, and response nonce freshness.
- Sends the assertion back to Steam using `openid.mode=check_authentication`; the SteamID is persisted only when Steam returns a valid assertion response.
- Restricts the linked subject to the numeric SteamID64 in Steam's documented claimed-ID format.
- Adds tests for successful verification, provider rejection, callback mismatch, unsigned identity fields, stale nonce, and non-Steam claimed identities.

## Security and integration boundary

The caller must consume the matching, unexpired `AccountLinkAuthorizationStore` transaction before calling the adapter. That transaction binds the callback to the authenticated browser session and central HogwartsRP account. The adapter never accepts an account ID from callback data and does not treat a browser-supplied SteamID as proof of identity.

Steam identity linking is not game ownership verification, server authorization, or a grant of HogwartsRP permissions. No Steam credentials or provider tokens are stored.

This PR delivers the verification adapter only. It does not add HTTP authorization-start/callback routes or the browser Account Connections UI.

## Release-note correction

PR #58 was merged with its release note mistakenly named `pr-57-discord-account-link-verification.md`. This branch renames that note to `pr-58-discord-account-link-verification.md` and corrects its heading.

Shared `CHANGELOG.md` and `UPDATES.md` remain untouched on this feature branch to avoid the recurring merge conflicts. Consolidate both entries after the feature PR is merged.
