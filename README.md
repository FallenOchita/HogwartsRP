# HogwartsRP

An independent, modular roleplay framework foundation for eventual Hogwarts Legacy integration. The current milestone is a runnable server framework prototype with resource discovery, a restricted manifest parser, dependency ordering, command permissions, Lua 5.4 resource workers, and a WebSocket protocol preview.

## Quick start

Install .NET 10, then run `dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources`. Build the native Rust launcher on Windows with `cargo build --manifest-path rust/Cargo.toml --release -p hogwartsrp-launcher`; the executable is produced under `rust/target/release/`. See [Rust launcher](docs/rust-launcher.md). See [installation](docs/installation.md), [architecture](docs/architecture.md), [Lua scripting](docs/lua-scripting.md), [manifest syntax](docs/resource-manifests.md), and [launcher/characters](docs/launcher-and-characters.md).

## Status

Resource lifecycle, Lua resource scripts, default-deny command checks, and server-validated character submissions are implemented. Lua remains the resource language and runs as native Lua 5.4 in a separate worker process per resource, with restricted libraries, OS resource limits, request timeouts, source limits, and instruction budgets. Core owns authenticated server sessions and bounded events over the WebSocket server transport. The Rust launcher currently supports public server directory browsing, saved server profiles, read-only server status checks, account-portal browser opening, and Steam launch requests. Authenticated server connection and character submission are not yet ported to the Rust executable; the legacy C# launcher is no longer built or used as the application shell. The launcher session is not an in-game session because no Hogwarts Legacy multiplayer adapter is connected. No supported direct character-creation entry API was found in official modding material. Workers still run as the server OS user without filesystem/network ACLs; client Lua execution, persistence, and resource hot reload are not implemented.

See [CHANGELOG.md](CHANGELOG.md) for release-oriented entries and [UPDATES.md](UPDATES.md) for implementation details and current milestone notes.

## Asset Research Toolkit

A standalone read-only Hogwarts Legacy installation inventory and loose Unreal asset catalog tool is available in [`tools/asset-research/`](tools/asset-research/). See its [usage guide](tools/asset-research/README.md), [architecture](docs/asset-research-toolkit/ARCHITECTURE.md), and [roadmap](docs/asset-research-toolkit/ROADMAP.md). It inventories package containers without extracting them and labels heuristic asset metadata clearly.
