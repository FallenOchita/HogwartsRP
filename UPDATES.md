# Detailed Updates

## 2026-11-18 — Hardened UE4SS diagnostic report and regression tests

### Added and changed

- Hardened `tools/diagnostics/Get-HogwartsRPDiagnosticReport.ps1` so it no longer echoes arbitrary tagged log lines.
- The report now emits only recognized startup, version, unavailable-version, and completion markers. Version values are restricted to a short safe token.
- Added `tools/diagnostics/Test-HogwartsRPDiagnosticReport.ps1` with checks for PASS, PARTIAL, NOT VERIFIED, missing-log handling, optional report output, and exclusion of unrelated or injected log content.
- Updated GitHub Actions to run the PowerShell test script on both Windows and Ubuntu runners.
- Updated `mod/ue4ss/README.md` and `docs/GAME_CAPABILITIES.md` with the hardened behavior and current validation boundary.

### Validation status

- The test harness and workflow have been committed to this branch, but hosted CI has not yet reported results.
- These tests use synthetic logs and do not require Hogwarts Legacy or UE4SS to be installed.
- The live Steam UE4SS diagnostic remains **NOT VERIFIED**. Passing the helper tests does not establish game compatibility or multiplayer.

### Next step

Wait for the branch's GitHub Actions results, review any failures, then merge only after checks pass and the changes are reviewed.

## 2026-11-18 — Sanitized UE4SS diagnostic report helper

### Added

- Added `tools/diagnostics/Get-HogwartsRPDiagnosticReport.ps1` to inspect the last 5,000 lines of a supplied `UE4SS.log`.
- The helper reports whether both HogwartsRP diagnostic markers are present, the executable product/file version when discoverable, and the UE4SS version reported by the diagnostic mod.
- It includes only recognized diagnostic output, omits the log path and unrelated log content, and performs no network requests or uploads.
- Added usage instructions to `mod/ue4ss/README.md`, including optional output to a sanitized text report.

### Validation boundary

- This helper has been added to the repository but has not been run against the user's actual UE4SS log or game installation.
- A `PASS` means only that the Lua diagnostic loaded and completed; it is not evidence of player discovery, game networking, or multiplayer.

### Next step

Run the diagnostic mod on the Steam installation, then use the PowerShell helper to produce a report for review. Confirm the report contains no unintended personal information before sharing it.


## 2026-11-18 — Steam UE4SS diagnostic mod (PR #6 merged)

### Delivered

- Added `mod/ue4ss/HogwartsRPDiagnostics/scripts/main.lua`, a minimal UE4SS Lua smoke-test mod.
- The script logs a successful load message, reports `UE4SS.GetVersion()` when available, and confirms that diagnostic mode did not access or modify gameplay objects.
- Added `mod/ue4ss/README.md` with Steam installation locations, `mods.txt` activation, log-verification steps, rollback guidance, and compatibility cautions.
- Updated `docs/GAME_CAPABILITIES.md` and the Continue project rules/prompt to make Steam PC the target and identify live UE4SS verification as the next gate.
- Pull request [#6 — Add Steam UE4SS diagnostic mod](https://github.com/FallenOchita/HogwartsRP/pull/6) was merged into `main` in commit `e71e4420fda0ae24591000b59b54f16f37381df1`.

### Verification status and limits

- The diagnostic script is committed but has **not** been verified in the user's live Hogwarts Legacy installation.
- No game objects, player state, networking, or multiplayer behavior are implemented by this diagnostic.
- No automated build or test run was performed for this Lua/documentation-only PR; the PR description records this explicitly.
- Do not combine this UE4SS diagnostic setup with HogWarp; the published HogWarp setup guidance reports incompatibility.

### Next milestone

Obtain a real UE4SS log from the user's Steam installation showing the `[HogwartsRPDiagnostics]` messages, and record the exact Hogwarts Legacy and UE4SS builds. Only after that compatibility gate passes should game-facing discovery or adapter work begin.


This file records implementation details and milestone status. For user-facing feature summaries, see [CHANGELOG.md](CHANGELOG.md).

## 2026-11-18 — Provider-neutral OIDC join authentication

### Flow

1. The launcher requests `/auth/oidc/challenge` from the selected server.
2. The server returns a random challenge ID and nonce, retaining the nonce for five minutes.
3. The launcher discovers the configured OIDC provider, opens the system browser, and performs authorization code with PKCE S256 using a loopback callback. The server-issued nonce is included in the authorization request.
4. The launcher exchanges the code at the provider token endpoint and sends only the returned ID token plus challenge ID to `/auth/tickets`.
5. The server verifies the token signature against provider JWKS, issuer, audience, authorized party where applicable, timestamps, and the one-use nonce. It derives the account key from issuer plus subject and does not accept role, permission, or owner claims.
6. The server creates a two-minute one-use WebSocket join ticket. The launcher then connects with the existing WebSocket client adapter.

### Boundaries and configuration

- OIDC remains behind `IJoinCredentialProvider` on the launcher and `IJoinTicketExchange` on the server.
- The launcher is a public client; no provider secret is required or embedded. Browser authorization uses `state`, PKCE, and a loopback-only listener.
- Server settings: `Authentication__Oidc__Authority`, `Authentication__Oidc__ClientId`.
- Launcher settings: `HogwartsRP__Oidc__Authority`, `HogwartsRP__Oidc__ClientId`.
- Both sides must use the exact issuer string and matching public client ID. The issuer must expose standard OpenID Connect discovery and RS256 signing keys.
- Production OIDC and remote server endpoints require HTTPS. Loopback HTTP is available for local development only.
- Without OIDC configuration, the launcher retains its development ticket prompt. The server disables `issue-ticket` on non-loopback listeners.

### Current limits

- The server currently validates RS256 ID tokens only. Providers using another signing algorithm need an explicit compatible validator before they can be used.
- The launcher remains a console preview. The game itself still has no compatible multiplayer adapter, and the signed-in launcher session is not an in-game session.
- Direct Hogwarts Legacy character-creation entry remains unavailable. The existing character integration contracts still require a supported game adapter.
- No external identity provider or live game installation has been configured in this milestone.

## 2026-11-18 — OIDC mock-provider verification

- Added `HogwartsRP.Server.Tests` to the solution. Tests use an in-memory HTTP handler and a generated RSA signing key; they make no requests to a real identity provider.
- Verified accepted tokens produce server-owned hashed account IDs and one-use join tickets.
- Verified rejection for invalid issuer, audience, nonce, expiry, and signature.
- Verified expired challenges are rejected before provider metadata or keys are requested, and that a challenge cannot be replayed.
- The existing GitHub Actions workflow already restores, builds, and tests the full solution on Windows and Ubuntu, so the new project is included automatically through `HogwartsRP.slnx`.
- Local result: all 21 tests passed across the four test projects. The six existing NU1900 warnings are vulnerability-feed connectivity warnings from nuget.org.
