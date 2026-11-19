# Detailed Updates

## 2026-11-18 — Experimental UE4SS player-state adapter

### Added

- Added `mod/ue4ss/HogwartsRPAdapter/Scripts/main.lua` as a separate UE4SS adapter mod, keeping the existing smoke-test mod unchanged.
- Reused the in-game findings supplied during development: `BP_Phoenix_Player_Controller_C`, `AcknowledgedPawn`, `BP_Biped_Player_C`, `GetActorLocation()`, and `GetControlRotation()`.
- Added read-only velocity and movement-component discovery, plus guarded candidate reads for movement mode, jump, sprint, and animation state.
- Exposed the adapter table as `_G.HogwartsLegacyAdapter` for later in-process integration and added a three-second diagnostic poll.
- Added installation, log-verification, rollback, and compatibility notes to `mod/ue4ss/README.md`.

### Validation boundary

- The user's reported local-player, position, and control-rotation observations are the only game-specific reads treated as confirmed.
- Movement-component discovery and jump/sprint/animation properties are exploratory and require verification in the user's installed game build.
- The adapter is read-only. It does not connect to the server, transmit transforms, spawn remote entities, or implement multiplayer.
- Hosted CI cannot validate UE4SS/game reflection behavior. Manual in-game log review is required.

### Next step

Install the adapter mod, confirm the `[HogwartsRPAdapter]` load marker, and collect logs while standing, walking, running, jumping, and rotating the camera. Use those results to promote only verified fields into the adapter contract.


## 2026-11-18 — Launcher character creation draft model

### Added

- Added `CharacterCreationDraft` to the launcher library as a UI-independent model for the future character-creation screen.
- Added client-side validation and normalization for character names, the four Hogwarts houses, background/biography length limits, and optional wand length.
- Added conversion from the draft into the existing `CharacterSubmission` contract. Appearance data remains unset until a verified appearance representation exists.
- Added a dedicated `HogwartsRP.Launcher.Tests` project covering valid mapping, invalid names/houses, wand bounds, and optional fields.
- Added the test project to `HogwartsRP.slnx` and updated the launcher documentation and changelog.

### Validation boundary

- This is the tested form-model layer only. The current launcher remains a console preview; no graphical form or game-side character creation was added.
- The server remains authoritative and must repeat validation on every submission.
- Hosted Windows and Ubuntu CI results are pending for this branch.

## 2026-11-18 — Character submission protocol regression tests

### Added

- Added a dedicated `HogwartsRP.Networking.Client.Tests` test project and included it in the solution.
- Added a local in-process WebSocket test server so the launcher protocol can be tested without a live Hogwarts Legacy installation or external server.
- Verified concurrent character submissions are correlated correctly when server responses arrive in reverse order.
- Verified a pending submission fails with a connection-closed error when the server disconnects before returning a result.
- Updated `CHANGELOG.md` and launcher/character documentation.

### Validation status

- PR #12 has been merged. The tests use a local synthetic WebSocket server and do not establish game compatibility or in-game multiplayer.

## 2026-11-18 — Launcher character submission client

### Added

- Added `SubmitCharacterAsync` to the networking-client contract and launcher connection service.
- Implemented the `submit_character` WebSocket request with a generated correlation ID and typed character submission payload.
- Routed `character_submission_result` messages to the matching pending request, allowing the launcher to receive server validation/persistence outcomes without confusing concurrent submissions.
- Added cleanup for pending submissions when the socket closes; callers can cancel while sending or awaiting the result.
- Updated launcher and protocol documentation, `CHANGELOG.md`, and this update log.

### Security and scope

- The launcher submits only character fields. The server continues to derive ownership from the authenticated session and performs authoritative validation.
- This is server-side roleplay-character registration only. It does not write character data into Hogwarts Legacy or provide in-game multiplayer.

### Validation status

- PR #10 and PR #11 were merged after their Windows and Ubuntu CI runs completed successfully.
- PR #12 added transport regression coverage and was subsequently merged.

## 2026-11-18 — PR #9 build correction

### Fixed

- Investigated the Ubuntu CI compiler errors for `HogwartsRP.GameIntegration.Abstractions`, `ICharacterRepository`, and `RegisteredCharacter`.
- Confirmed that the contract and record types are declared in `src/HogwartsRP.GameIntegration.Abstractions/CharacterCreation.cs`.
- Added an explicit project reference from `HogwartsRP.Server.csproj` to `HogwartsRP.GameIntegration.Abstractions.csproj`. This makes the contract assembly available when compiling `JsonCharacterRepository`.
- Recorded the correction in `CHANGELOG.md`.

### Validation status

- The source-level cause of the reported compile errors has been corrected on the existing PR #9 branch.
- GitHub Actions must complete on the new branch head before this change can be called CI-verified. No merge has been performed.

## 2026-11-18 — JSON-backed character persistence adapter

### Added

- Added `JsonCharacterRepository` in `src/HogwartsRP.Server/JsonCharacterRepository.cs`, implementing the existing `ICharacterRepository` contract.
- Character records are stored as JSON and loaded lazily when the repository is first used.
- Writes are serialized within the repository instance and committed through a temporary file in the same directory, followed by an atomic replacement.
- Duplicate character IDs are rejected. Reads can be filtered by the authenticated owner ID, and corrupt JSON is reported without overwriting the existing data file.
- Added server tests for persistence across repository instances, owner filtering, duplicate-ID rejection, and corrupt-store preservation.

### Configuration and limitations

- Construct the repository with a configured data path, for example `new JsonCharacterRepository("data/characters.json")`.
- The file adapter is intended for a single server process. It does not coordinate writes across multiple server instances and does not replace backups or a production database.
- The repository contract's `RegisterAsync` operation remains the persistence entry point; the concrete adapter also exposes owner-filtered retrieval.

### Validation status

- Regression tests have been added, but this branch has not yet run through hosted GitHub Actions.
- No live game or multiplayer behavior is involved in this change.

### Next step

Run the Windows and Ubuntu CI matrix, review any failures, and merge only after checks pass.

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

Run the diagnostic mod on the Steam installation, then use the PowerShell helper to produce a report for review. Confirm the report contains no unintended personal information before sharing it.

## 2026-11-18 — Sanitized UE4SS diagnostic report helper

### Added

- Added `tools/diagnostics/Get-HogwartsRPDiagnosticReport.ps1` to inspect the last 5,000 lines of a supplied `UE4SS.log`.
- The helper reports whether both HogwartsRP diagnostic markers are present, the executable product/file version when discoverable, and the UE4SS version reported by the diagnostic mod.
- It includes only recognized diagnostic output, omits the log path and unrelated log content, and performs no network requests or uploads.
- Added usage instructions to `mod/ue4ss/README.md`, including optional output to a sanitized text report.

### Validation boundary

- This helper has been added to the repository but has not been run against the user's actual UE4SS log or game installation.
- A `PASS` means only that the diagnostic Lua mod loaded and completed; it is not evidence of player discovery, game networking, or multiplayer.

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
