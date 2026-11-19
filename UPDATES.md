# Detailed Updates

## 2026-11-18 — Launcher character creation console flow

### Added

- Added `CharacterCreationConsoleFlow` to collect a character name, Hogwarts house, optional roleplay biography/background, and optional wand details.
- Connected the flow to `ServerConnectionService.SubmitCharacterAsync`, so submissions use the already-authenticated launcher session and existing correlated WebSocket request protocol.
- The server response is displayed to the user. The flow reports server rejection and transport errors without treating a failed submission as successful.
- Users can decline character creation and continue with the existing launch flow.
- Added automated tests for successful submission, invalid draft rejection without a network call, declined creation, and server-side rejection.
- Updated the launcher/character documentation and changelog.

### Validation boundary

- The flow persists roleplay character metadata only. It does not create or alter a character in Hogwarts Legacy.
- The launcher remains a console preview; a graphical UI is a separate milestone.
- Hosted Windows and Ubuntu CI must complete before this branch can be called verified.
- No PR has been merged as part of this branch.

## 2026-11-18 — Launcher character creation draft model

### Added

- Added `CharacterCreationDraft` to the launcher library as a UI-independent model for the future character-creation screen.
- Added client-side validation and normalization for character names, the four Hogwarts houses, background/biography length limits, and optional wand length.
- Added conversion from the draft into the existing `CharacterSubmission` contract. Appearance data remains unset until a verified appearance representation exists.
- Added a dedicated `HogwartsRP.Launcher.Tests` project covering valid mapping, invalid names/houses, wand bounds, and optional fields.
- Added the test project to `HogwartsRP.slnx` and updated the launcher documentation and changelog.

### Validation boundary

- PR #14 was merged into `main`.
- This is the tested form-model layer only; the console submission flow is the next increment.
- The server remains authoritative and repeats validation on every submission.

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
