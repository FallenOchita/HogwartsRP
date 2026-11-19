# PR #49 — Automatic bounded Rust worker recovery

## Added

- Connected `RustWorkerRestartPolicy` to `ResourceManager` through an optional constructor setting. The default remains zero retries, preserving fail-fast behavior.
- Added the `Restarting` lifecycle state and capped-backoff worker replacement. A successful replacement returns the resource to `Started`; exhausted retries leave it `Failed`.
- Made intentional shutdown cancel pending restart delays before workers are disposed.
- Added a real-process integration test using a Rust fixture that exits once and then answers a command after the manager restarts it.
- Updated Rust support and architecture documentation, including the retry-budget scope and loss of in-memory worker state after a restart.

## Boundaries

- Recovery is per resource manager lifetime; it does not restart Lua runtimes, dependent resources, or the server.
- Rust workers still run with the server user's OS permissions and do not yet receive the Lua worker's CPU/memory limits.
- Hosted Windows and Ubuntu CI must pass before this integration is considered verified.

## Release-log handling

This PR keeps its release notes in this PR-specific file to avoid concurrent edits to the shared `CHANGELOG.md` and `UPDATES.md`. Consolidate the entry into those shared logs after this feature branch has been merged.
