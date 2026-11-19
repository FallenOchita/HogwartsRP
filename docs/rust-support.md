# Rust support

## Direction

HogwartsRP supports Rust in two roles:

1. **Rust resources** — resource authors compile a standalone worker executable and use hogwartsrp-resource-sdk. Workers communicate with the .NET host over bounded JSON Lines on stdin/stdout; stdout is reserved for protocol messages and diagnostics belong on stderr.
2. **Native framework components** — hogwartsrp-native-core contains deterministic validation primitives. The .NET Core remains authoritative for sessions, permissions, account identity, persistence policy, and server orchestration.

## Workspace foundation

- rust/Cargo.toml defines the Rust workspace.
- rust/crates/hogwartsrp-native-core provides resource-name, event-name, argument-count, and protocol-frame validation.
- rust/crates/hogwartsrp-resource-sdk provides command/event registration and the JSON-lines worker loop.
- rust/examples/rust_greeter is a sample resource.
- CI formats and tests the Rust workspace on Ubuntu and Windows; the native desktop launcher is release-built on Windows.

## Rust resource hosting

The resource manifest parser accepts one or more precompiled worker executable paths:

```lua
fx_version 'cerulean'
game 'hogwartslegacy'

server_script 'lua/main.lua'
rust_script 'bin/rust_greeter.exe'
```

Use a platform-appropriate executable name/path for each server deployment. The path is resolved relative to the resource directory and rejected if it escapes that directory. The server does not compile Rust resources at startup; build and deploy the executable for the target operating system before starting the server.

The C# ResourceManager now starts each declared Rust worker after the resource's Lua server/shared scripts. A worker must emit its ready protocol message during the startup window. The manager exposes:
- RustCommandRegistered(resourceName, commandName) when a command is declared.
- RustNetworkEventRegistered(resourceName, eventName) when a resource declares an inbound network event.
- InvokeRustCommandAsync(resourceName, commandName, arguments) for command dispatch.
- DispatchRustEventAsync(eventName, arguments) for server-originated event dispatch to all running Rust workers.

The server still owns authorization. A reported network event is only a declaration; it must be checked against the server's event policy before any client-originated message is dispatched. Rust command declarations are not automatically exposed as public or privileged server commands. The host integration must choose the permission policy and caller identity.

The process supervisor uses a 10-second startup timeout, a 15-second request timeout, a 32 KiB UTF-8 frame limit, and a two-second graceful-stop window before terminating the process tree. The SDK and .NET host use camelCase JSON field names with snake_case message type values.

## Current integration boundary

This is the first server-hosting slice, not a complete multi-language runtime:
- Rust workers can be declared, started, receive host-dispatched events, and service explicitly routed command calls.
- Automatic Cargo builds, artifact discovery, hot reload, restart policies, and per-resource build pipelines are not implemented.
- Rust worker commands are not yet registered in the framework-wide CommandRegistry.
- Network-event declarations are surfaced to the host, but the WebSocket adapter and its allowlist are not automatically wired to them.
- Cross-resource event fan-out is available through the manager dispatch method, but a shared Lua/Rust event bus adapter is not yet implemented.
- The supervisor bounds protocol frames and request duration but does not apply the Lua worker's OS memory/CPU limits to Rust executables.

The native-core crate is an rlib only. The .NET host does not load it through P/Invoke, and no native acceleration or runtime performance gain is claimed.

## Security model

- Rust resources are separate processes, not dynamically loaded into the server.
- A process boundary is not a complete OS sandbox. A Rust worker runs with the launching server user's operating-system permissions.
- Treat deployed Rust resources as trusted code until stronger OS-level isolation is configured.
- The host must remain authoritative for player IDs, permissions, event allowlists, persistence, and network delivery.
- Do not pass provider tokens, join tickets, database credentials, or arbitrary host filesystem/network handles to resources by default.
- Never use a Rust resource to bypass game protections, anti-cheat, DRM, or platform authentication.

## Build locally

Install the stable Rust toolchain, then run:

```sh
cargo fmt --manifest-path rust/Cargo.toml --all -- --check
cargo test --manifest-path rust/Cargo.toml --workspace
cargo build --manifest-path rust/Cargo.toml -p hogwartsrp-rust-greeter
```

Build the resource executable for the same operating system and architecture as the server. The SDK example is a protocol sample; it waits for host messages on stdin after startup.

## Native desktop launcher

The active desktop launcher is Rust: rust/apps/hogwartsrp-launcher uses eframe/egui. The WinForms executable has been removed from the .NET solution. See Native Rust launcher in rust-launcher.md. The .NET launcher support library remains temporarily for existing contracts/tests, but the Rust executable does not invoke it.


## Rust worker restart policy

The `RustWorkerRestartPolicy` type defines an opt-in bounded retry budget and capped exponential backoff. Its default configuration permits zero retries, preserving fail-fast behavior. The policy is currently a validated configuration primitive; process replacement and ResourceManager integration are not included in this milestone.
