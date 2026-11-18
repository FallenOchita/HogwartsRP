---
name: Continue HogwartsRP
description: Resume HogwartsRP from its verified project state and work on the next approved milestone.
invokable: true
---

Read docs/COPILOT_HANDOFF.md and docs/GAME_CAPABILITIES.md completely. Then inspect the current repository state, branch, git status, latest main commit, existing source, tests, and CI. Do not assume documentation is newer than the code.

Continue the HogwartsRP project from the latest verified state. The user confirmed Hogwarts Legacy is installed through Steam. A harmless UE4SS diagnostic mod has been added under `mod/ue4ss/HogwartsRPDiagnostics/`; read its README and `docs/GAME_CAPABILITIES.md`. The immediate milestone is to verify the diagnostic mod in the user's exact Steam game build using a real UE4SS log.

Preserve the working .NET 10 server infrastructure, Lua 5.4 resource runtime, authenticated WebSocket transport, and launcher preview. Lua is the primary resource scripting language; do not rewrite the whole framework in Lua.

Verify the exact Hogwarts Legacy game build and UE4SS release/build. Confirm the committed `HogwartsRPDiagnostics` Lua mod loads on the user's Steam installation and capture the actual UE4SS/game build details before implementing game-facing behavior. The script is not considered tested until the user provides a real log or a live test result. Treat all third-party class names and method findings as unverified until reproduced on the target build. Review licenses before using dependencies or code. Do not combine the UE4SS diagnostic setup with HogWarp; its published setup guidance says the two are incompatible.

Only implement a client adapter after confirming a supported integration mechanism. Reuse existing networking contracts, use short-lived credentials, validate all client messages server-side, and add automated tests. Keep mock-game tests clearly separate from real in-game verification.

Create a focused feature branch, preserve uncommitted changes, run restore/build/all tests, fix failures, update documentation, and report exact results. Do not merge, force-push, deploy, or claim in-game multiplayer works without real two-client verification.
