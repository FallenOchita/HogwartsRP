# HogwartsRP

An independent, modular roleplay framework foundation for eventual Hogwarts Legacy integration. The current milestone is a runnable server framework prototype with resource discovery, a restricted manifest parser, dependency ordering, command permissions, a local event bus, and an interim managed Lua adapter.

## Quick start

Install .NET 10, then run `dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources`. To use the preview launcher, run `dotnet run --project src/HogwartsRP.Launcher.App`. See [installation](docs/installation.md), [architecture](docs/architecture.md), [Lua scripting](docs/lua-scripting.md), [manifest syntax](docs/resource-manifests.md), and [launcher/characters](docs/launcher-and-characters.md).

## Status

Resource lifecycle, Lua resource scripts, default-deny command checks, and server-validated character submissions are implemented. Lua remains the resource language and runs as native Lua 5.4 in a separate worker process per resource, with restricted libraries, OS resource limits, request timeouts, source limits, and instruction budgets. Core now provides authenticated server-owned sessions, a one-time join-ticket boundary, bounded client/server events, and a WebSocket server transport. The local console ticket command is an operator bootstrap only; no account provider or launcher ticket exchange is connected. The launcher uses the Steam app URI to preserve platform ownership/authentication, but does not connect a game client. No supported direct character-creation entry API was found in official modding material. There is no Hogwarts Legacy multiplayer adapter, so the protocol server is not in-game multiplayer. Workers still run as the server OS user without filesystem/network ACLs; client Lua execution, persistence, and resource hot reload are not implemented.
