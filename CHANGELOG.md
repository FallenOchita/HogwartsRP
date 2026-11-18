# Changelog

All notable HogwartsRP changes are recorded here.

## [Unreleased]

### Added

- Core-owned player sessions with server-generated IDs, single-use join tickets, and per-resource client-event validation.
- A WebSocket server transport with bounded JSON messages and authenticated server-to-client delivery.
- A replaceable client network adapter and launcher credential-provider interface.
- A preview launcher flow that authenticates to the selected server, requests Hogwarts Legacy launch, and keeps the protocol session open until the launcher exits.
- Lua resource APIs for registering client-to-server events and sending events to authenticated players.

### Limitations

- The preview credential prompt uses operator-issued development tickets; a trusted account identity provider is not connected.
- The launcher owns the current network session. Hogwarts Legacy itself has no compatible multiplayer adapter yet.
- Direct character-creation entry, game-client event transport, and in-game multiplayer remain unavailable.
