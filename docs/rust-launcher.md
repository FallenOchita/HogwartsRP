# Native Rust launcher

The active HogwartsRP desktop launcher is now the Rust binary at `rust/apps/hogwartsrp-launcher`. The WinForms executable project has been removed from the .NET solution and its source files deleted. The Rust app does not start or call the old C# launcher.

## Build

Install the stable Rust toolchain and run on Windows:

```powershell
cargo build --manifest-path rust/Cargo.toml --release -p hogwartsrp-launcher
```

The executable is written to `rust/target/release/hogwartsrp-launcher.exe`.

The Rust launcher uses eframe/egui for its native desktop UI. Server profiles are stored under the operating system's application configuration directory using the `directories` crate.

## Current Rust launcher features

- Native desktop UI built and started by Rust.
- Add, save, select, and remove server profiles.
- Read-only `GET /status` checks with a five-second timeout.
- Open the central browser account portal when `HOGWARTSRP_ACCOUNT_PORTAL_URL` is configured.
- Detect Hogwarts Legacy in conventional Steam library locations and ask Steam to launch app 990080.
- Steam continues to perform its normal ownership and sign-in checks.

## Migration still in progress

The Rust binary is the only active launcher application, but this is not yet feature parity with the previous C# preview launcher. The following still need native Rust implementations:

- OIDC authorization-code + PKCE and the authenticated WebSocket join-ticket flow.
- Server connection lifecycle, authenticated network event delivery, and reconnect handling.
- Character creation draft UI and authenticated character submission.
- Steam library discovery beyond the conventional paths, Epic installation/launch support, and direct update delivery.
- Public server directory browsing and account-linking controls beyond opening the configured browser portal.

The Rust launcher does not call the legacy C# launcher library. That library remains in the .NET solution temporarily because its existing tests and contracts are still useful during migration; it is not the launcher executable and is not used by the Rust app. It can be retired after the remaining flows are ported and their behavior is covered by Rust tests.

## Configuration

Set `HOGWARTSRP_ACCOUNT_PORTAL_URL` to the deployed central account portal's HTTPS URL to enable the account-portal button. No production URL or credentials are hard-coded.

## Validation

CI formats the Rust workspace and runs Rust library/SDK tests on Ubuntu and Windows. The native desktop launcher is built in release mode on Windows CI. No claim is made that browser authentication, character submission, or in-game multiplayer has been ported by this milestone.
