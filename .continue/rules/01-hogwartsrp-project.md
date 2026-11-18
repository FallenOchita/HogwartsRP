---
name: HogwartsRP Project Rules
---

# HogwartsRP — Continue.dev project rules

## Project identity
- This repository builds an independent Hogwarts Legacy PC multiplayer roleplay framework.
- Lua 5.4 is the primary resource/gameplay scripting language.
- Existing infrastructure is .NET 10/C#: server host, worker supervision, networking, authentication, and launcher services.
- A minimal native bridge may be required for verified game integration. Do not assume undocumented game APIs exist.

## Required context
- Before significant work, read docs/COPILOT_HANDOFF.md and docs/GAME_CAPABILITIES.md.
- Inspect the live repository, current branch, git status, solution, tests, and existing implementations. Handoff documents can become stale.
- Reuse existing Core, session, authentication, WebSocket, resource, and game-integration abstractions instead of creating parallel systems.

## Architecture constraints
- Do not rewrite working C# infrastructure merely to make the project look Lua-only.
- Keep Lua 5.4 as the public resource scripting API and keep server authority for identity, character ownership, permissions, and persistent state.
- Keep the game adapter replaceable and separate from server/Core.
- Do not implement fictional game functions, undocumented offsets, guessed launch arguments, or placeholder APIs presented as functional.
- Distinguish IMPLEMENTED, TESTED, EXPERIMENTAL, MOCK ONLY, and UNAVAILABLE.
- A mock game adapter or unit test is not proof of live Hogwarts Legacy functionality.

## Security and networking
- Validate all client-originated messages on the server.
- Do not trust client-supplied identity, character ownership, permissions, or privileged state.
- Use bounded, versioned messages, authentication, rate limits, and short-lived credentials.
- Never embed permanent server secrets in a game plugin or client binary.
- Do not log credentials or tokens.
- Do not bypass DRM, platform authentication, access controls, anti-cheat, or game security protections.
- Review third-party dependency licenses and redistribution restrictions before adoption.

## Development workflow
- Inspect git status and preserve uncommitted user changes.
- Start from the latest main and create a focused feature branch.
- Make small changes with automated tests.
- Keep CI tests runnable without Hogwarts Legacy installed; document manual game tests separately.
- Run restore, build, and all tests; fix regressions before declaring completion.
- Update docs and .continue rules/prompts when architecture, capabilities, tests, or next steps change.
- Do not merge, force-push, or deploy without explicit user authorization.
- Finish with changed files, exact test results, limitations, and next task.

## Immediate project priority
- The user confirmed Steam PC. The initial UE4SS diagnostic mod is in `mod/ue4ss/HogwartsRPDiagnostics/`; see its README and `docs/GAME_CAPABILITIES.md`.
- The immediate milestone is to get a real UE4SS log proving the diagnostic mod loads on the user's exact Steam game build. Do not claim success based on source code alone.
- Do not combine the diagnostic UE4SS setup with HogWarp; its published setup guidance says they are incompatible.
The next milestone is the UE4SS compatibility and diagnostic smoke test described in docs/GAME_CAPABILITIES.md. Verify the exact installed game and UE4SS builds before writing game-facing code. Do not treat third-party reverse-engineering notes as supported APIs.
