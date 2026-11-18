# HogwartsRP — Copilot Development Handoff

**Audit date:** 2026-11-18  
**Repository:** https://github.com/FallenOchita/HogwartsRP  
**Default branch:** `main`  
**Audited HEAD:** `83f29733846359b6ad8cf2b13a958fb7fee9b7dd`  
**Latest merged PR:** #4 — Add mock OIDC authentication tests  
**Purpose:** Give GitHub Copilot the verified state of the project and a safe, concrete next task.

---

## 1. Executive summary

HogwartsRP has moved beyond its initial skeleton. The current repository contains a .NET 10 server and launcher foundation, a real Lua 5.4 resource runtime hosted in isolated worker processes, a resource manager, server-side command/event handling, authenticated WebSocket sessions, a preview launcher, and provider-neutral OIDC join authentication.

**The central unfinished feature is still the actual Hogwarts Legacy multiplayer client adapter.** The current launcher can authenticate to a HogwartsRP server and ask Steam to launch Hogwarts Legacy, but the game itself is not connected to the launcher's WebSocket session. No verified in-game player synchronization, remote player spawning, character synchronization, spell replication, or live game-world interaction is implemented.

The repository documentation explicitly records these limits. Do not report the project as a working Hogwarts Legacy multiplayer mod yet.

## 2. Verified GitHub state

- Repository is public and active: `FallenOchita/HogwartsRP`.
- Default branch: `main`.
- Current main HEAD at audit: `83f29733846359b6ad8cf2b13a958fb7fee9b7dd`.
- Latest commit message: `Merge pull request #4 ... Add mock OIDC authentication tests`.
- PRs #1, #2, #3, and #4 are all merged.
- The branch `feature/lua-runtime-foundation` is four commits behind `main` and has no commits ahead. **Start new work from `main`; do not continue from the stale feature branch.**
- Latest HEAD has two GitHub Actions checks: `build-test (ubuntu-latest)` and `build-test (windows-latest)`; both completed successfully.
- The latest update notes report 21 local tests passing across four test projects. The six NU1900 warnings were reported as NuGet vulnerability-feed connectivity warnings, not test failures.
- CI workflow: `.github/workflows/ci.yml`; it restores, builds, and tests `HogwartsRP.slnx` on Ubuntu and Windows using .NET 10.

Useful links:
- Repository: https://github.com/FallenOchita/HogwartsRP
- Latest commit: https://github.com/FallenOchita/HogwartsRP/commit/83f29733846359b6ad8cf2b13a958fb7fee9b7dd
- PR #1: https://github.com/FallenOchita/HogwartsRP/pull/1
- PR #2: https://github.com/FallenOchita/HogwartsRP/pull/2
- PR #3: https://github.com/FallenOchita/HogwartsRP/pull/3
- PR #4: https://github.com/FallenOchita/HogwartsRP/pull/4
- Latest CI run: https://github.com/FallenOchita/HogwartsRP/actions

## 3. What is implemented now

### Server and Core
- .NET 10 server executable.
- Core framework and permission/command infrastructure.
- Default-deny command permission checks.
- Core-owned player sessions with server-generated session IDs.
- Single-use, short-lived join tickets.
- Client-event allowlisting per Lua resource.
- Bounded JSON WebSocket messages and authenticated server-to-client delivery.
- Server console commands including `status`, `issue-ticket` (loopback development only), and `quit`.
- Graceful resource and networking shutdown.

### Lua scripting and resources
- NLua 1.7.9 with KeraLua's native Lua 5.4 runtime.
- Actual Lua 5.4 scripts execute; this is not just a placeholder interface.
- Each resource executes in its own worker process and Lua state.
- Server/shared scripts belonging to one resource share that resource's state.
- Restricted Lua globals/libraries and instruction budgets.
- Worker process memory/CPU limits are applied using Windows Job Objects or Linux resource limits.
- Resource discovery and restricted `fxmanifest.lua` parsing.
- Manifest is parsed as data and is not executed as Lua.
- Dependency validation, cycle detection, deterministic dependency ordering, and resource lifecycle management.
- Implemented resource APIs include `RegisterCommand`, `RegisterNetEvent`, `AddEventHandler`, `TriggerEvent`, and `TriggerClientEvent`.
- Cross-resource server events are relayed by Core.
- Client-originated events are allowlisted by server resources and receive server-owned player session identity.

**Important distinction:** The repository is not currently a Lua-only server. The host, server, worker supervisor, networking and launcher are C#/.NET. Lua 5.4 is the actual resource/gameplay scripting language running inside worker processes. Preserve the working foundation unless a deliberate architecture decision is made to replace a specific component.

### Networking and authentication
- WebSocket server endpoint at `/connect`.
- Replaceable client networking adapter.
- Authenticated session establishment using one-time join tickets.
- Optional provider-neutral OIDC browser sign-in using authorization code + PKCE.
- Server-issued one-use nonce/challenge and short-lived join ticket.
- Server validates OIDC ID-token signature, issuer, audience, relevant time claims and nonce.
- Current OIDC validator accepts RS256 only.
- Mock OIDC tests use an in-memory HTTP handler and generated RSA signing keys; they do not contact a real identity provider.
- Development ticket issuance is restricted to loopback listeners.

### Launcher and game launch
- Current launcher is a **console preview**, not a finished graphical launcher.
- Server selection and credential-provider abstractions exist.
- OIDC sign-in can be configured through environment variables.
- Game installation detector checks conventional Windows Steam/Epic install roots and additional configured roots.
- Steam launcher uses the official `steam://run/990080` URI.
- Epic launch is not implemented.
- Launcher keeps its own authenticated WebSocket session open while the preview is running.

### Character creation contracts
- Character submission models and server-side validation exist.
- House values are validated against Gryffindor, Hufflepuff, Ravenclaw and Slytherin.
- Character owner is derived from authenticated player identity, not client-submitted data.
- Character-creation and game integration interfaces exist as abstractions only.

## 4. Current project layout

Important projects in `HogwartsRP.slnx`:

- `src/HogwartsRP.Core` — authoritative framework, sessions, permissions, commands and network security.
- `src/HogwartsRP.Server` — server host, WebSocket transport adapter and OIDC ticket exchange.
- `src/HogwartsRP.Scripting` — Lua 5.4 runtime, worker-process supervisor/protocol and platform resource limits.
- `src/HogwartsRP.LuaWorker` — isolated Lua worker executable.
- `src/HogwartsRP.Resources` — resource discovery, manifest parsing and lifecycle.
- `src/HogwartsRP.Networking.Abstractions` — shared networking/session contracts.
- `src/HogwartsRP.Networking.Client` — WebSocket client adapter.
- `src/HogwartsRP.GameIntegration.Abstractions` — game/character integration contracts.
- `src/HogwartsRP.Launcher` — game detection, platform launch and server connection services.
- `src/HogwartsRP.Launcher.App` — console launcher preview.
- `resources/example_character` — example Lua resource.
- `tests/HogwartsRP.Core.Tests`
- `tests/HogwartsRP.Resources.Tests`
- `tests/HogwartsRP.Scripting.Tests`
- `tests/HogwartsRP.Server.Tests` — OIDC ticket exchange tests.
- `docs/` — architecture, install, Lua, manifests, commands/events, permissions, game integration and launcher/character notes.

Key files to inspect before changing code:
- `docs/architecture.md`
- `docs/game-integration.md`
- `docs/launcher-and-characters.md`
- `docs/lua-scripting.md`
- `docs/resource-manifests.md`
- `src/HogwartsRP.Server/Program.cs`
- `src/HogwartsRP.Server/WebSocketNetworkingAdapter.cs`
- `src/HogwartsRP.Scripting/LuaRuntime.cs`
- `src/HogwartsRP.Scripting/LuaWorkerProcessRuntime.cs`
- `src/HogwartsRP.Resources/ResourceManager.cs`
- `src/HogwartsRP.Launcher/GameLaunch.cs`
- `src/HogwartsRP.Launcher/ServerConnection.cs`

## 5. Known gaps — do not claim these are implemented

1. **No real Hogwarts Legacy multiplayer client adapter is connected.**
2. No client-side Lua resource runtime is implemented.
3. Manifest client scripts are discovered but are not executed by the server host.
4. No game process/plugin integration has been proven against a live game installation.
5. No verified API currently reads/applies the local or remote in-game player transform.
6. No remote player entity spawning or rendering.
7. No in-game character identity/appearance synchronization.
8. No spell, wand, broom/flying, animation, NPC, door, or world-event synchronization.
9. No direct character-creation entry API has been verified; launcher falls back to normal game startup.
10. No persistent character database/repository implementation is connected.
11. No production identity provider is configured; OIDC tests use a mock provider.
12. No production server deployment, server browser, or finished graphical launcher.
13. Epic platform launch is not wired.
14. Lua workers run as the server OS user. Although dangerous Lua libraries are removed and CPU/memory/time limits are applied, there is no separate OS account/token, filesystem ACL, or network namespace. Treat resources as trusted.
15. No live end-to-end game multiplayer test has been performed.

## 6. Architecture direction to preserve

The product goal is an independent HogwartsRP framework inspired by FiveM/Open//77, with Lua 5.4 as the primary developer scripting language for resources, events, commands and roleplay/gameplay logic.

The existing repository currently uses C#/.NET for the host infrastructure and runs real Lua 5.4 resource scripts in isolated workers. Do not casually rewrite the functioning server, authentication, worker isolation, or WebSocket transport just to make the codebase appear Lua-only.

For the game adapter, keep a clear boundary:
- HogwartsRP server/Core owns authenticated player identity, character ownership, permissions, authoritative roleplay state and persistence.
- Client adapter connects an authenticated game client to the server.
- A native/game integration component may be required to expose supported game functions to Lua.
- Lua should be the public resource scripting API and own gameplay/roleplay resource logic.
- Any C++ bridge must be minimal, replaceable, and limited to verified integration needs.
- Do not assume that a mod loader provides a multiplayer API or that game objects can be controlled just because an interface has been declared.

Research available lawful game integration approaches and document the license/redistribution conditions before selecting one. Do not copy restricted source code or bypass DRM, platform authentication, anti-cheat or security controls.

## 7. Recommended next milestone for Copilot

**Next milestone: Game Adapter Feasibility and Client Integration Spike.**

Do not start by implementing imaginary game functions. First prove what the selected game integration environment can actually do.

### Task A — audit the current seams
1. Read the current architecture and game-integration documentation.
2. Inspect `IGameIntegration`, `ICharacterService`, `ICharacterCreationIntegration`, launcher interfaces, and networking contracts.
3. Identify which abstractions are currently unused, mocked, or only return unavailable.
4. Avoid duplicating existing networking/session abstractions.

### Task B — game capability research
1. Identify currently maintained Hogwarts Legacy PC mod integration approaches and their documented APIs.
2. Verify the supported game version, platform scope, installation requirements, and license.
3. Determine whether a plugin can be loaded and whether it can safely communicate with a local HogwartsRP client service.
4. Determine whether the environment exposes a local player object, transform read access, transform apply access, animation events, and character metadata.
5. Record evidence and exact limitations in `docs/GAME_CAPABILITIES.md`.
6. Do not use undocumented memory offsets or fabricate class/function names.
7. If a required API is not available, record it as unavailable and do not pretend the interface makes it work.

### Task C — build a minimal client adapter proof of concept
Only after Task B establishes a compatible supported integration:
1. Create a separate client-adapter project/module using the chosen integration's actual supported extension model.
2. Implement adapter startup/shutdown and capability reporting.
3. Implement a local diagnostic connection to the existing HogwartsRP WebSocket server.
4. Authenticate the client through a secure short-lived credential handoff; do not embed server secrets in the game plugin.
5. Connect the game-client identity to the already existing server-owned player session.
6. Implement local player-ready notification only if the game API supports it.
7. Add a heartbeat and clean disconnect.
8. Add tests for the adapter's protocol and lifecycle using a fake game API.
9. Clearly distinguish fake-game tests from actual in-game verification.

### Task D — next synchronization proof
After the client connection is proven, implement the smallest verifiable synchronization vertical slice:
1. Read local player position/rotation only through a verified API.
2. Send bounded, versioned movement snapshots to the server.
3. Validate and rate-limit snapshots server-side.
4. Relay state to another authenticated test client.
5. Apply remote state only if a verified game API supports it.
6. Test with two real game clients before claiming in-game player synchronization is implemented.

Do not implement spells, flying, animations, or world-state synchronization until the player adapter and transform synchronization are proven.

## 8. Build and test commands

Run from the repository root:

```powershell
dotnet restore HogwartsRP.slnx
dotnet build HogwartsRP.slnx --no-restore
dotnet test HogwartsRP.slnx --no-build
dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources
```

Preview launcher:

```powershell
dotnet run --project src/HogwartsRP.Launcher.App
```

The server currently listens on loopback `http://127.0.0.1:7777` by default. The server is a framework/protocol preview, not a Hogwarts Legacy multiplayer server.

## 9. Instructions for GitHub Copilot

Use this handoff as the current source of truth, but verify the live repository before editing because main may have advanced since this audit.

- Start from the latest `main`, not `feature/lua-runtime-foundation`.
- Inspect `git status` and preserve uncommitted user work.
- Create a focused branch such as `feature/game-adapter-feasibility`.
- Do not overwrite working code or restructure the solution without a documented reason.
- Do not claim a real game capability based on mocks or abstractions.
- Keep tests runnable in CI without Hogwarts Legacy installed.
- Add manual integration-test instructions for tests that require the real game.
- Run restore, build and all tests; fix regressions before finishing.
- Update `docs/GAME_CAPABILITIES.md` and the existing documentation as discoveries are made.
- Do not merge or push without the repository owner's approval.

### First Copilot instruction

"Read `docs/COPILOT_HANDOFF.md` completely, then independently inspect the latest repository state and current code. Do not start implementing a speculative multiplayer adapter. First complete the Game Adapter Feasibility and Client Integration Spike described in section 7. Verify real Hogwarts Legacy integration capabilities and licensing, document the evidence in `docs/GAME_CAPABILITIES.md`, identify the exact smallest working integration proof, and report which requirements are unavailable. Preserve the existing .NET 10 server, Lua 5.4 worker runtime, authenticated WebSocket transport, and launcher preview unless a concrete tested reason requires a change. Run the full build and test suite before proposing the next implementation step."

---

**Audit conclusion:** Codex's last merged work was OIDC mock-provider test coverage (PR #4). The existing framework and authenticated launcher/server preview are working foundations. The next major uncompleted product milestone is not another launcher feature: it is proving and implementing the real Hogwarts Legacy client/game adapter so the game can participate in the existing authenticated multiplayer session.
