# Changelog

All notable HogwartsRP changes are recorded here.

## [Unreleased]

### Fixed — Rust worker exit supervision race

- Wait for the worker process to fully exit after stdout reaches EOF before reading its exit code or notifying observers.
- Prevented a timing race that could report a null exit code on Unix even when the worker exited with a known nonzero code.

### Improved — Rust worker supervision and integration coverage

- Added cross-platform end-to-end tests that launch a compiled Rust resource worker, verify command and network-event registration, invoke a command, dispatch an event, and observe worker stderr output.
- Added unexpected-exit reporting and process-state visibility to the Rust worker supervisor; pending requests now fail when the worker exits.
- Isolated host observer callback exceptions so logging and registration subscribers cannot terminate the worker protocol pump.
- Validate worker response message types before completing host requests.
- Added a dedicated Rust integration-test worker fixture and CI build step on Windows and Ubuntu.

### Added — Rust resource runtime integration

- Added precompiled Rust worker declarations through repeated `rust_script` manifest entries, with resource-relative path validation.
- Added a .NET process supervisor that waits for worker readiness, bounds JSON-lines frames and request duration, routes command/event requests, captures stderr diagnostics, and performs graceful shutdown with process-tree termination fallback.
- Integrated Rust worker startup and shutdown into ResourceManager alongside Lua resource scripts, and exposed Rust command/event registration plus explicit command and event dispatch methods.
- Aligned the Rust SDK JSON field casing with the .NET web JSON protocol and added a regression test for `eventName` serialization.
- Added manifest parsing coverage and updated Rust support and architecture documentation.

### Integration limitations

- Rust resources must be compiled and deployed for the server platform; automatic Cargo builds and artifact discovery are not included.
- Rust command declarations and network-event declarations are surfaced by ResourceManager but are not yet automatically connected to the framework-wide command registry or WebSocket authorization/allowlist pipeline.
- Rust workers currently run with the server account's OS permissions; this slice does not yet apply the Lua worker's OS memory/CPU limits. Treat Rust resources as trusted code.

