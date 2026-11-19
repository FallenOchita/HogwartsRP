# HogwartsRP

An independent, modular roleplay framework foundation for eventual Hogwarts Legacy integration. The current milestone is a runnable server framework prototype with resource discovery, a restricted manifest parser, dependency ordering, command permissions, Lua 5.4 resource workers, and a WebSocket protocol preview.

## Quick start

Install .NET 10, then run `dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources`. To use the preview launcher, run `dotnet run --project src/HogwartsRP.Launcher.App`. See [installation](docs/installation.md), [architecture](docs/architecture.md), [Lua scripting](docs/lua-scripting.md), [manifest syntax](docs/resource-manifests.md), and [launcher/characters](docs/launcher-and-characters.md).

## Status

Resource lifecycle, Lua resource scripts, default-deny command checks, and server-validated character submissions are implemented. Lua remains the resource language and runs as native Lua 5.4 in a separate worker process per resource, with restricted libraries, OS resource limits, request timeouts, source limits, and instruction budgets. Core owns authenticated server sessions and bounded events over the WebSocket server transport. The launcher supports provider-neutral OIDC authorization-code sign-in with PKCE when configured; the server validates signed ID tokens and mints short-lived join tickets. A loopback-only development-ticket path remains available for local work. The launcher session is not an in-game session because no Hogwarts Legacy multiplayer adapter is connected. No supported direct character-creation entry API was found in official modding material. Workers still run as the server OS user without filesystem/network ACLs; client Lua execution, persistence, and resource hot reload are not implemented.

See [CHANGELOG.md](CHANGELOG.md) for release-oriented entries and [UPDATES.md](UPDATES.md) for implementation details and current milestone notes.

## Community Discord Ticket Bot

The standalone HogwartsRP Discord ticket bot lives in [`services/discord-ticket-bot/`](services/discord-ticket-bot/). It provides support, character applications, appeals, reports, technical support and development ticket workflows. See its [installation guide](services/discord-ticket-bot/docs/INSTALLATION.md), [administrator guide](services/discord-ticket-bot/docs/ADMIN_GUIDE.md), and [architecture](services/discord-ticket-bot/docs/ARCHITECTURE.md). The bot is maintained as a separate Node.js service and does not run inside the HogwartsRP game server.


## Asset Research Toolkit

A standalone read-only Hogwarts Legacy installation inventory and loose Unreal asset catalog tool is available in [`tools/asset-research/`](tools/asset-research/). See its [usage guide](tools/asset-research/README.md), [architecture](docs/asset-research-toolkit/ARCHITECTURE.md), and [roadmap](docs/asset-research-toolkit/ROADMAP.md). It inventories package containers without extracting them and labels heuristic asset metadata clearly.
