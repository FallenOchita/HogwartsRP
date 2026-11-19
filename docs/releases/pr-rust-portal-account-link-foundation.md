# PR — Rust portal account-link foundation

## Added
- Added a Rust development JSON store for verified Discord, Steam, and Epic identity links.
- Enforced one identity per provider per HogwartsRP account and global ownership uniqueness per provider.
- Added authenticated `GET /account/links`, returning connection flags only; provider identifiers are never returned to the browser.
- Added `DELETE /account/links/{provider}`, scoped to the account resolved from the current browser session and protected by CSRF and same-origin Origin validation.
- Wired the portal dashboard to display real connection status and expose disconnect controls only for linked providers.
- Added `HOGWARTSRP_PORTAL_ACCOUNT_LINKS_PATH` for the development link-store path.

## Security boundaries
- No HTTP request accepts a caller-supplied HogwartsRP account ID or external provider subject.
- Only trusted provider verification code should call `link_verified_identity`.
- Disconnect operations can remove only the selected provider from the currently authenticated account.
- Provider IDs and tokens are not exposed to the dashboard.

## Not included yet
- Discord, Steam, and Epic authorization/callback verification is not connected to the Rust portal yet, so new provider links cannot yet be created from the UI.
- The JSON store is a single-process development adapter, not production database storage.
- The central portal remains separate from per-game-server sign-in and join tickets.
