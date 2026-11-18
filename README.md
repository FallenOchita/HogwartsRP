# HogwartsRP

An independent, modular roleplay framework foundation for eventual Hogwarts Legacy integration. The current milestone is a runnable server framework prototype with resource discovery, a restricted manifest parser, dependency ordering, command permissions, Lua 5.4 resource workers, and a WebSocket protocol preview.

## Quick start

Install .NET 10, then run `dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources`. To use the preview launcher, run `dotnet run --project src/HogwartsRP.Launcher.App`. See [installation](docs/installation.md), [architecture](docs/architecture.md), [Lua scripting](docs/lua-scripting.md), [manifest syntax](docs/resource-manifests.md), and [launcher/characters](docs/launcher-and-characters.md).

## Status

Resource lifecycle, Lua resource scripts, default-deny command checks, and server-validated character submissions are implemented. Lua remains the resource language and runs as native Lua 5.4 in a separate worker process per resource, with restricted libraries, OS resource limits, request timeouts, source limits, and instruction budgets. Core owns authenticated server sessions and bounded events over the WebSocket server transport. The preview launcher connects through a replaceable client adapter and credential-provider interface; its ticket prompt accepts operator-issued one-time development credentials, not account logins. No account provider or Hogwarts Legacy multiplayer adapter is connected, so the launcher session is not an in-game session. No supported direct character-creation entry API was found in official modding material. Workers still run as the server OS user without filesystem/network ACLs; client Lua execution, persistence, and resource hot reload are not implemented.
