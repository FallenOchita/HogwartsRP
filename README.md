# HogwartsRP

An independent, modular roleplay framework foundation for eventual Hogwarts Legacy integration. The current milestone is a runnable server framework prototype with resource discovery, a restricted manifest parser, dependency ordering, command permissions, a local event bus, and an interim managed Lua adapter.

## Quick start

Install .NET 10, then run `dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources`. To use the preview launcher, run `dotnet run --project src/HogwartsRP.Launcher.App`. See [installation](docs/installation.md), [architecture](docs/architecture.md), [Lua scripting](docs/lua-scripting.md), [manifest syntax](docs/resource-manifests.md), and [launcher/characters](docs/launcher-and-characters.md).

## Status

Resource startup and stop, manifest parsing, local commands/events, default-deny command checks, a configurable installation detector, platform-launch abstraction, and server-side character submission validation are implemented. The Steam launcher uses the Steam app URI to preserve platform ownership/authentication. The character creation bridge is an interface with no real game adapter. No supported direct character-creation entry API was found in official modding material; launch falls back to normal game startup. Lua runtime is MoonSharp and therefore Lua 5.2, not Lua 5.4. Network/game APIs are interfaces without a compatible multiplayer adapter. Client Lua execution, persistence, and resource hot reload are not implemented. Do not infer actual in-game behavior from the local demo.
