# Hogwarts Legacy Game Capabilities — Feasibility Report

**Research date:** 2026-11-18  
**Repository:** FallenOchita/HogwartsRP  
**Branch:** feature/game-adapter-feasibility

## Executive decision

**Recommended investigation path: UE4SS Lua mod plus a separately built, minimal native networking bridge, subject to an installation/version smoke test.**

Hogwarts Legacy official Creator Kit documentation describes user-created quests, dungeons, cosmetics and skins. Its public documentation does not describe a runtime API for multiplayer, player transform replication, remote player spawning, or arbitrary client/server networking. The official Creator Kit does not, by itself, provide the integration surface HogwartsRP needs.

UE4SS documents a Lua scripting system for Unreal Engine games, a C++ modding API, object discovery, function hooks, and loading native Lua C libraries from a mod scripts folder. These are promising extension mechanisms, not proof that a particular UE4SS release works with the user's installed Hogwarts Legacy build or that the game's player objects can be safely controlled. Game-specific object names and methods must be revalidated against the exact installed build.

No game adapter is implemented by this report. Do not claim live game connectivity or multiplayer.

## Capability matrix

| Capability | Status | Evidence / limitation |
|---|---|---|
| Official PC mod support | DOCUMENTED | Warner Bros. Games documents PC mod support for Steam and Epic. |
| Official Creator Kit content | DOCUMENTED | Creator Kit supports quests, dungeons, character enhancements, cosmetics and skins. |
| Official multiplayer/runtime plugin API | NOT DOCUMENTED | Official public documentation reviewed describes content creation and distribution, not runtime multiplayer APIs. |
| UE4SS Lua mod loading | DOCUMENTED GENERIC CAPABILITY | UE4SS documents a Mods/<name>/scripts/main.lua layout and Lua API. Hogwarts Legacy compatibility remains build-specific. |
| UE4SS UObject discovery | DOCUMENTED GENERIC CAPABILITY | FindFirstOf, FindAllOf, StaticFindObject and object methods are documented. A valid Hogwarts Legacy player object has not been verified by HogwartsRP. |
| UE4SS function hooks | DOCUMENTED GENERIC CAPABILITY | RegisterHook is documented for existing UFunctions. Exact Hogwarts Legacy function paths and safe hook timing remain unverified. |
| Native Lua C library loading | DOCUMENTED GENERIC CAPABILITY | UE4SS guide says native Lua C libraries can be placed in a mod scripts folder. A HogwartsRP networking DLL does not exist yet. |
| Local player discovery | UNVERIFIED FOR HOGWARTSRP | An independent early-stage repository reports UE4SS findings for BP_Phoenix_Player_Controller_C and BP_Biped_Player_C; this is third-party, build-sensitive research, not a verified API contract. |
| Local player position/rotation read | UNVERIFIED FOR HOGWARTSRP | The same third-party project reports K2_GetActorLocation() / K2_GetActorRotation() on a pawn. Must reproduce on the exact game build before use. |
| Remote player spawn/render | NOT PROVEN | The third-party research project explicitly says it is not yet playable and does not have working player entity spawning. |
| Apply remote transform | NOT PROVEN | No verified HogwartsRP implementation or supported game API found. |
| Spells, broom/flying, animations, NPC/world state sync | NOT PROVEN | No verified end-to-end capability. Keep out of the first adapter milestone. |
| Game-to-HogwartsRP authenticated connection | NOT IMPLEMENTED | Existing authenticated WebSocket belongs to the launcher process; the game process is not connected. |
| Two-client live multiplayer | NOT IMPLEMENTED / NOT TESTED | No two-client in-game verification exists. |

## Evidence reviewed

### Official Hogwarts Legacy modding

1. Warner Bros. Games, [Hogwarts Legacy Mods — Authors FAQ](https://portkeygamessupport.wbgames.com/hc/en-us/articles/35104134540179-Hogwarts-Legacy-Mods-Authors-FAQ): states that PC mods are supported on Steam and Epic and describes the Creator Kit/Epic Editor and account requirements.
2. Official [Hogwarts Legacy / CurseForge Creator Kit site](https://hogwartslegacy.curseforge.com/): describes the Creator Kit, publishing and in-game discovery; its FAQ lists quests, dungeons, cosmetics and skins as supported content types.
3. Warner Bros. Games, [How To Install and Enable a Mod on PC](https://portkeygamessupport.wbgames.com/hc/en-us/articles/38048531445779-How-To-Install-and-Enable-a-Mod-on-PC): documents the in-game mod menu and account linking.

These official materials do not document a multiplayer networking or arbitrary runtime plugin API.

### UE4SS extension model

1. [UE4SS release documentation](https://docs.ue4ss.com/release/): describes the Lua scripting API, C++ modding API, SDK/header dumping and Blueprint mod loading.
2. [Creating a Lua Mod](https://docs.ue4ss.com/lua-api/guides/creating-a-lua-mod.html): documents the mod folder and scripts/main.lua entry point, plus use of native Lua C libraries from the scripts folder.
3. [UE4SS Lua API](https://docs.ue4ss.com/lua-api/): documents object lookup and interaction APIs.
4. [FindFirstOf](https://trumank.github.io/RE-UE4SS/lua-api/global-functions/findfirstof.html) and [RegisterHook](https://trumank.github.io/RE-UE4SS/lua-api/global-functions/registerhook.html): describe generic object lookup and UFunction hooks. Hook targets must already exist in memory when registered.

UE4SS explicitly warns that it is not plug-and-play for every Unreal game and that compatibility may require game-specific work. Treat its documented generic API as a candidate extension model only.

### Third-party Hogwarts Legacy multiplayer research

[Kheartz/HogwartsLegacyMP](https://github.com/Kheartz/HogwartsLegacyMP) is a public early-development research project. Its README describes a C++/UE4SS Lua client, a native Lua networking extension DLL, and a C++/ENet server. It reports game-specific class names and pawn location/rotation methods. The README also states that it is not yet playable and has no release available.

Important restrictions for HogwartsRP:
- This is third-party research, not a supported game API.
- The repository currently has no top-level LICENSE file visible through GitHub's repository contents API. Do not copy, adapt, vendor, or redistribute its code unless its maintainers provide a clear license and permission compatible with HogwartsRP.
- Do not treat its reported class names or methods as stable. Reproduce findings on the target game build.
- Use the project only as a pointer to areas that require independent verification.

## Architecture recommendation

Keep the existing HogwartsRP server and Lua resource runtime unchanged.

Proposed boundary, pending a live smoke test:

1. **UE4SS client mod (Lua):** game-facing script loaded by UE4SS. It reports adapter startup and only calls game APIs verified for the exact game build.
2. **Native client transport module (C++):** a small, replaceable native component if needed for a supported, reliable network client. Do not put server secrets in this module. Do not assume UE4SS embedded Lua has the same libraries or version as HogwartsRP server-side Lua 5.4.
3. **HogwartsRP client connection:** authenticate using a short-lived, single-use credential issued by the existing server. Do not reuse or expose the launcher process credentials.
4. **Existing .NET server/WebSocket:** remains authoritative for player identity, character ownership, permissions, rate limits and server-side validation.
5. **Lua resources:** remain the public gameplay/roleplay scripting API. The UE4SS client script is an adapter, not a replacement for the server resource runtime.

Do not select a transport implementation until the team proves that it can be built, loaded, connected, shut down cleanly, and authenticated in the target environment. The first proof should be a diagnostic handshake only; no player movement or entity manipulation.

## Required validation before coding the game-facing adapter

1. Record the exact installed Hogwarts Legacy platform (Steam or Epic), executable version/build, and UE4SS release/build being tested.
2. Confirm UE4SS starts successfully on that exact installation and loads a harmless diagnostic Lua mod.
3. Confirm the mod can report UE4SS version and basic startup/shutdown lifecycle without touching gameplay state.
4. Inspect the live UE4SS object browser/dumper for the local player controller and pawn. Do not rely on class names copied from another project.
5. Confirm the candidate object is valid and stable across loading screens and character/session transitions.
6. Verify location/rotation reads on the local player only after identifying the actual reflected object and function signatures for this build.
7. Test native transport separately with a local diagnostic endpoint; then connect to a development HogwartsRP server using a short-lived credential.
8. Add lifecycle, malformed-message, timeout, reconnect and credential-replay tests.
9. Keep all automated CI tests independent of a Hogwarts Legacy installation. Mark manual game checks separately.

## Immediate next implementation slice

After the validation above, create a separate adapter project/module that does only:

- load and report adapter version;
- report UE4SS/game build identifiers where safely available;
- establish a diagnostic authenticated connection to the existing server;
- send a bounded protocol-version/capability handshake;
- heartbeat and cleanly disconnect;
- expose explicit capability flags, with unsupported game functions reported as unavailable.

Do not add transform sync, remote player spawning, spells, flying, animations or world synchronization in this slice.

## Current HogwartsRP status

- Existing .NET 10 server and authenticated WebSocket: IMPLEMENTED.
- Server-side Lua 5.4 worker runtime: IMPLEMENTED.
- Launcher-owned authenticated connection: IMPLEMENTED.
- Game-owned authenticated connection: NOT IMPLEMENTED.
- UE4SS diagnostic mod: NOT IMPLEMENTED.
- Native networking bridge: NOT IMPLEMENTED.
- Local player transform read/apply: NOT VERIFIED.
- Remote player entity creation: NOT VERIFIED.
- Two-client game test: NOT PERFORMED.

## Decision

Proceed with a UE4SS compatibility and diagnostic smoke test as the next task. The public UE4SS Lua/C++ extension model is the most concrete candidate found, but no game-specific adapter code should be merged until the exact installed game build and UE4SS build pass that smoke test.
