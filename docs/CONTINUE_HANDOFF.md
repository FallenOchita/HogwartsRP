# HogwartsRP — Continue.dev Development Handoff

**Updated:** 2026-11-18  
**Repository:** https://github.com/FallenOchita/HogwartsRP  
**Default branch:** `main`  
**Last verified repository commit before this handoff:** `bfd537f576708839adc98b809241ace343d01eae`  
**Previous handoff:** [docs/COPILOT_HANDOFF.md](./COPILOT_HANDOFF.md)

> This is a project-state handoff, not a claim that Hogwarts Legacy multiplayer is already working. Re-check the live repository and working tree before every development session.

## 1. Project goal

HogwartsRP is an independent multiplayer roleplay framework for the PC version of Hogwarts Legacy, inspired by the developer experience of FiveM and Open//77.

The intended developer experience is Lua 5.4 resources for roleplay logic, events, commands, and gameplay systems. The framework infrastructure currently uses .NET 10/C# for the server host, process supervision, networking, authentication, and launcher services. A native game bridge may be required if the chosen game integration exposes functionality through a native extension model.

Do not rewrite working infrastructure solely to make the repository appear Lua-only. Lua is the primary resource scripting language; it is not currently the language of every infrastructure component.

## 2. Verified baseline at the last audit

The repository had:
- A .NET 10 solution and server host.
- A real Lua 5.4 runtime using NLua 1.7.9/KeraLua, running resource scripts in isolated worker processes.
- Resource discovery, restricted `fxmanifest.lua` parsing, dependency validation/order, and resource lifecycle management.
- Resource APIs including `RegisterCommand`, `RegisterNetEvent`, `AddEventHandler`, `TriggerEvent`, and `TriggerClientEvent`.
- An authenticated WebSocket server endpoint at `/connect`.
- One-use join tickets and optional provider-neutral OIDC authorization-code + PKCE flow.
- A console launcher preview with server connection/authentication abstractions and Steam launch support.
- Character submission models and validation contracts, including server-derived character ownership and house validation.
- Four test projects and Windows/Linux CI.

The latest known code baseline was commit `83f29733846359b6ad8cf2b13a958fb7fee9b7dd`. The Copilot handoff was then committed as `bfd537f576708839adc98b809241ace343d01eae`. That commit added documentation only; it did not change the application code. Verify whether newer commits exist before relying on this snapshot.

## 3. Known unfinished work

The following were not implemented or verified at the last audit:

1. A real Hogwarts Legacy in-game client adapter.
2. A client-side Lua resource runtime.
3. Actual in-game player transform reading or applying.
4. Remote player spawning/rendering.
5. Real character appearance/identity synchronization inside the game.
6. Spell, wand, flying, animation, NPC, door, or world-event replication.
7. Direct launch into the game's character-creation screen.
8. A connected persistent character database/repository.
9. A finished graphical launcher and production server browser.
10. Production identity-provider configuration and deployment.
11. Live end-to-end testing with two real game clients.

Mocks and interfaces are useful for development but are not proof that a game feature works.

## 4. Architecture to preserve

- `src/HogwartsRP.Core`: authoritative sessions, permissions, commands, and security-related core services.
- `src/HogwartsRP.Server`: server host, WebSocket transport adapter, and authentication/ticket exchange.
- `src/HogwartsRP.Scripting`: Lua runtime and worker-process supervision.
- `src/HogwartsRP.LuaWorker`: isolated Lua worker executable.
- `src/HogwartsRP.Resources`: resource discovery, manifest parsing, and lifecycle.
- `src/HogwartsRP.Networking.Abstractions`: networking contracts.
- `src/HogwartsRP.Networking.Client`: WebSocket client adapter.
- `src/HogwartsRP.GameIntegration.Abstractions`: game and character integration contracts.
- `src/HogwartsRP.Launcher`: game detection, platform launch, and server connection services.
- `src/HogwartsRP.Launcher.App`: console launcher preview.
- `resources/`: example Lua resources.
- `tests/`: unit and integration test projects.
- `docs/`: architecture and developer documentation.

Read the existing implementation before changing these areas. Reuse existing contracts and avoid duplicate networking/session systems.

## 5. Immediate next milestone

**Game Adapter Feasibility and Client Integration Spike.**

Do not begin by fabricating game APIs or building speculative synchronization code.

### Step A — inspect the repository
- Check the current branch, `git status`, latest commit, solution projects, and CI.
- Read `docs/COPILOT_HANDOFF.md` and existing architecture/game-integration docs.
- Inspect the real definitions and call sites for the game integration, character, launcher, and networking abstractions.
- Record which interfaces are implemented, unused, mocked, or return an unavailable result.

### Step B — verify game integration capabilities
- Research supported Hogwarts Legacy PC mod integration approaches using primary documentation and current project sources.
- Verify supported game version, platform, extension mechanism, dependencies, and license/redistribution terms.
- Determine whether a supported extension can load with the game and communicate with a local HogwartsRP client process.
- Verify whether documented APIs expose local player readiness, position/rotation read access, remote state application, animation, and character metadata.
- Record evidence and limitations in `docs/GAME_CAPABILITIES.md`.
- Do not invent class names, function names, memory offsets, or launch arguments.
- Do not bypass DRM, platform authentication, game security protections, or access controls.
- If no supported API exists for a capability, mark it unavailable and propose a safe alternative or research question.

### Step C — smallest client adapter proof
Only if Step B verifies a viable extension mechanism:
- Create a separate, replaceable client-adapter component using the actual supported extension model.
- Implement startup, shutdown, diagnostics, and capability reporting.
- Connect to the existing server networking contract; do not create a second protocol unnecessarily.
- Use a short-lived, authenticated credential handoff. Never embed permanent server secrets in the client plugin.
- Add heartbeat, disconnect, protocol version checks, bounded messages, and error handling.
- Test lifecycle and protocol behavior with a fake game API, clearly labelled as mock tests.
- Do not claim live game integration until manually tested in the actual game.

### Step D — only then prove synchronization
After client connection works:
- Read local player transform only through a verified game API.
- Send bounded, versioned movement snapshots.
- Validate and rate-limit client updates server-side.
- Relay updates to a second authenticated test client.
- Apply remote player state only if the game integration provides a verified supported API.
- Distinguish transport-level tests, fake-adapter tests, and real two-client in-game tests.

Do not start with spells, flying, animation replication, or a full world simulation. First prove the smallest real player-state vertical slice.

## 6. Build and test

Run from repository root, after checking the current solution filename and SDK availability:

```powershell
dotnet restore HogwartsRP.slnx
dotnet build HogwartsRP.slnx --no-restore
dotnet test HogwartsRP.slnx --no-build
dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources
```

Launcher preview:

```powershell
dotnet run --project src/HogwartsRP.Launcher.App
```

The server preview is not itself a functioning Hogwarts Legacy multiplayer game server. Confirm exact current behavior from code and tests.

## 7. Engineering and safety rules

- Preserve user changes; inspect `git status` before editing.
- Work from the latest `main`; create a focused feature branch.
- Do not force-push, merge, or deploy without explicit authorization.
- Make small, reviewable changes.
- Add tests for new behavior and update docs with implementation status.
- Keep CI tests independent of a local Hogwarts Legacy installation.
- Mark capabilities as IMPLEMENTED, TESTED, EXPERIMENTAL, MOCK ONLY, or UNAVAILABLE.
- Never describe an abstraction, mock, or passing unit test as proof of in-game functionality.
- Do not introduce secrets into source control or logs.
- Respect dependency licenses and game/platform terms.
- Run restore, build, and the full test suite; report exact results and any warnings.
- If a build/test fails, diagnose and fix it before declaring the milestone complete.

## 8. Session completion report

At the end of each Continue session, update this handoff if project state materially changes and report:
1. What was inspected.
2. What was implemented.
3. Files changed.
4. Tests/build commands and exact results.
5. Which claims were verified with a real game versus mocks.
6. Known limitations and risks.
7. Recommended next small task.
8. Branch and commit/PR status.

## 9. First instruction

Read this file and `docs/COPILOT_HANDOFF.md`, then independently inspect the latest repository and working tree. Start with the Game Adapter Feasibility and Client Integration Spike. First produce a factual capability audit in `docs/GAME_CAPABILITIES.md`; only implement a client adapter after identifying a supported, licensed integration mechanism. Preserve the current .NET 10 server, Lua 5.4 worker runtime, authenticated WebSocket transport, and launcher preview. Run the complete build and test suite before finishing. Do not claim real in-game multiplayer until tested with actual game clients.
