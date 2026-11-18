# Launcher and character creation

The launcher flow selects a server, detects the game, asks a platform adapter to launch, and reports state changes. Installation detection is limited to conventional Steam/Epic Windows install paths and explicitly configured roots. Steam uses the platform app URI. A compatible Epic adapter and multiplayer client are not included.

Official Hogwarts Legacy mod support provides a Creator Kit and in-game mod management for content such as quests, dungeons, cosmetics, and skins. Public official documentation does not expose a supported direct character-creation launch API. Community character editor mods demonstrate in-game editing via a mod loader and hotkeys, not a stable launcher API. Consequently the launcher makes no direct-entry attempt by default and reports normal-launch fallback. `ICharacterCreationBridge` and `ICharacterCreationIntegration` allow a future verified integration to be added without changing launcher logic.

Server submission data includes name, optional appearance JSON, house, wand, background, and roleplay profile. Validation limits input sizes and restricts house values. The server stamps ownership from its authenticated player session and does not accept client-supplied permission or owner fields. Persistence must be supplied by a repository adapter.

No real game installation or integration has been tested in this environment. There is no claim that direct character creation or in-game multiplayer works.
