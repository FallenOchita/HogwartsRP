# PR #52 — Rust server command and network-event routing

## Added
- Connect Rust worker command declarations to the server's authoritative CommandRegistry.
- Assign server-controlled command permissions, with an operator configuration override; resource code cannot self-assign permissions.
- Connect Rust inbound event declarations to the WebSocket NetworkEventPolicy.
- Route approved events only to the declaring resource's workers that registered the event.
- Resolve commands by their declaring worker, supporting multiple Rust workers per resource and rejecting ambiguous duplicate declarations.
- Preserve command routes across bounded worker restarts.

## Validation
- Added a resource lifecycle integration test that launches two workers in one resource and verifies ambiguous duplicate command names fail closed.
- Windows and Ubuntu GitHub Actions passed. The initial fixture attempt failed because duplicate manifest paths are deduplicated; the fixture was corrected to use two distinct executable paths and then passed on both platforms.

## Launcher migration
- The Rust desktop executable already replaces the WinForms shell. The remaining C# launcher support library still contains OIDC, WebSocket, character-draft, profile, status, and game-launch contracts. Its complete Rust port and removal from the .NET solution will be handled as a separate, explicit migration slice rather than silently deleting functionality from this server-routing change.
