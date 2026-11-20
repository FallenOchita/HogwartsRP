# Game integration

There is no supported Hogwarts Legacy client integration implemented in this repository. `IGameIntegration` and `ICharacterService` define seams only. Player synchronization, spell casting, flying, animations, NPC interaction, and a dedicated game server are unavailable.

See [GAME_CAPABILITIES.md](GAME_CAPABILITIES.md) for the current source-backed feasibility assessment. The current candidate for investigation is UE4SS Lua plus a minimal replaceable native networking bridge, but this has not passed a live compatibility test against the target game build. Do not treat third-party reverse-engineering notes as stable APIs.

A real integration must use a compatible lawful project, verify its license and technical compatibility, and must not redistribute proprietary game assets or bypass ownership, platform authentication, anti-cheat, DRM, or security controls.


## Game adapter connection protocol

The server now has a versioned game-adapter handshake contract. After the normal one-use join-ticket authentication creates a server-owned player session, a game client may identify its adapter/game/UE4SS versions and report bounded capability names. The server replies with the authoritative session ID and display name. A strictly increasing heartbeat sequence keeps the adapter connection alive. See [game-adapter-protocol.md](game-adapter-protocol.md).

This is server-side protocol support only. The UE4SS mod still has no native WebSocket transport or game-side ticket acquisition, so a game-owned authenticated connection and two-client multiplayer remain unimplemented. Capability names reported by a client are informational and do not authorize server actions.
