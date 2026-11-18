# Game integration

There is no supported Hogwarts Legacy client integration implemented in this repository. `IGameIntegration` and `ICharacterService` define seams only. Player synchronization, spell casting, flying, animations, NPC interaction, and a dedicated game server are unavailable.

See [GAME_CAPABILITIES.md](GAME_CAPABILITIES.md) for the current source-backed feasibility assessment. The current candidate for investigation is UE4SS Lua plus a minimal replaceable native networking bridge, but this has not passed a live compatibility test against the target game build. Do not treat third-party reverse-engineering notes as stable APIs.

A real integration must use a compatible lawful project, verify its license and technical compatibility, and must not redistribute proprietary game assets or bypass ownership, platform authentication, anti-cheat, DRM, or security controls.
