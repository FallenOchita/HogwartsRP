# Rust support

## Direction

HogwartsRP will support Rust in two roles:

1. **Rust resources** — resource authors compile a Rust resource as a standalone worker executable and use the hogwartsrp-resource-sdk crate. The worker protocol is JSON Lines over stdin/stdout; stdout is reserved for protocol messages and diagnostics belong on stderr.
2. **Native framework components** — the hogwartsrp-native-core crate contains bounded, deterministic primitives intended for performance-sensitive framework components. The existing .NET Core remains authoritative for sessions, permissions, account identity, persistence policy, and server orchestration.

## Current foundation

- rust/Cargo.toml defines the Rust workspace.
- rust/crates/hogwartsrp-native-core provides tested resource-name, event-name, argument-count, and protocol-frame validation.
- rust/crates/hogwartsrp-resource-sdk provides resource registration APIs and a bounded JSON-lines worker loop.
- rust/examples/rust_greeter is a sample Rust resource.
- CI runs Rust formatting checks and workspace tests on Ubuntu and Windows.

## Integration status and limitations

This is the Rust SDK and native-core foundation, **not yet an end-to-end server runtime integration**. The current C# resource manager still starts Lua workers only; it does not yet discover, build, launch, supervise, or route messages to Rust workers. A follow-up integration milestone must add manifest declarations, a C# Rust-worker process supervisor, command/event routing, shutdown and resource isolation tests, and platform-specific binary discovery.

The native-core crate currently builds as both an Rust library and a C-compatible shared library, but HogwartsRP's .NET host does not yet load it through P/Invoke. Do not assume Rust is on the hot path until a later integration change explicitly wires and benchmarks it.

## Security model

- Rust resource executables are separate processes, not dynamically loaded into the server.
- A process boundary is not a complete OS sandbox. The worker runs with the launching server user's operating-system permissions unless deployment configures stronger isolation.
- The host must remain authoritative for player IDs, permissions, event allowlists, persistence, and network delivery.
- Rust resources must not receive provider tokens, join tickets, database credentials, or arbitrary host filesystem/network handles by default.
- Worker messages are bounded; only JSON values are accepted by the protocol.
- Never use a Rust resource to bypass game protections, anti-cheat, DRM, or platform authentication.

## Build locally

Install the stable Rust toolchain, then run:

    cargo fmt --manifest-path rust/Cargo.toml --all -- --check
    cargo test --manifest-path rust/Cargo.toml --workspace
    cargo build --manifest-path rust/Cargo.toml -p hogwartsrp-rust-greeter

The example is a protocol SDK sample. Until the C# worker supervisor is merged, running it directly waits for host messages on stdin.
