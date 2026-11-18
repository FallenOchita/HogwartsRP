---
name: Continue HogwartsRP
description: Resume HogwartsRP from its verified project handoff and work on the next approved milestone.
invokable: true
---

Read `docs/CONTINUE_HANDOFF.md` and `docs/COPILOT_HANDOFF.md` completely. Then inspect the current repository state, branch, git status, latest main commit, existing source, tests, and CI. Do not assume the handoff is newer than the code.

Continue the HogwartsRP project from the latest verified state. The immediate milestone is the Game Adapter Feasibility and Client Integration Spike.

First inspect the existing game integration, character, launcher, and networking abstractions. Research and verify supported Hogwarts Legacy PC integration mechanisms, supported APIs, compatibility, and licenses. Record evidence and exact capability status in `docs/GAME_CAPABILITIES.md`. Do not invent game APIs, memory offsets, or launch arguments.

Preserve the working .NET 10 server infrastructure, Lua 5.4 resource runtime, authenticated WebSocket transport, and launcher preview. Lua is the primary resource scripting language; do not rewrite the whole framework in Lua.

Only implement a client adapter after confirming a supported integration mechanism. Reuse existing networking contracts, use short-lived credentials, validate all client messages server-side, and add automated tests. Keep mock tests clearly separate from real in-game verification.

Create a focused feature branch, preserve uncommitted changes, run restore/build/all tests, fix failures, update documentation, and report exact results. Do not merge, force-push, deploy, or claim in-game multiplayer works without real two-client verification.
