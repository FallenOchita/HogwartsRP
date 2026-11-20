# Native Rust launcher

The active WizardNet desktop launcher is the Rust binary at `rust/apps/wizardnet-launcher`. The WinForms executable project has been removed from the .NET solution and its source files deleted. The Rust app does not start or call the old C# launcher.

## Build

Install the stable Rust toolchain and run on Windows:

```powershell
cargo build --manifest-path rust/Cargo.toml --release -p wizardnet-launcher
```

The executable is written to `rust/target/release/wizardnet-launcher.exe`.

The Rust launcher uses eframe/egui for its native desktop UI. Server profiles are stored under the operating system's application configuration directory using the `directories` crate.

## Current Rust launcher features

- Native desktop UI built and started by Rust.
- Public server browser backed by the Rust directory API, with name search, bounded pagination, listing validation, online state, and player counts.
- Selecting a directory listing fills the existing server connection profile; it does not yet authenticate or join the game server.
- Add, save, select, and remove server profiles.
- Read-only `GET /status` checks with a five-second timeout.
- Open the central browser account portal when `WIZARDNET_ACCOUNT_PORTAL_URL` is configured.
- Detect Hogwarts Legacy in conventional Steam library locations and ask Steam to launch app 990080.
- Steam continues to perform its normal ownership and sign-in checks.

## Migration still in progress

The Rust binary is the only active launcher application, but this is not yet feature parity with the previous C# preview launcher. The following still need native Rust implementations:

- OIDC authorization-code + PKCE and the authenticated WebSocket join-ticket flow.
- Server connection lifecycle, authenticated network event delivery, and reconnect handling.
- Character creation draft UI and authenticated character submission.
- Steam library discovery beyond the conventional paths, Epic installation/launch support, and direct update delivery.

The Rust launcher does not call the legacy C# launcher library. That library remains in the .NET solution temporarily because its existing tests and contracts are still useful during migration; it is not the launcher executable and is not used by the Rust app. It can be retired after the remaining flows are ported and their behavior is covered by Rust tests.

## Configuration

Set `WIZARDNET_ACCOUNT_PORTAL_URL` to the deployed central account portal's HTTPS URL to enable the account-portal button. Set `WIZARDNET_DIRECTORY_URL` to the directory service origin (for example, `http://127.0.0.1:8790` for local development). No production URL or credentials are hard-coded.

## Validation

CI formats the Rust workspace and runs Rust library/SDK tests on Ubuntu and Windows. The native desktop launcher is built in release mode on Windows CI. No claim is made that browser authentication, character submission, or in-game multiplayer has been ported by this milestone.


For migration, the launcher still accepts the legacy `HOGWARTSRP_ACCOUNT_PORTAL_URL` and `HOGWARTSRP_DIRECTORY_URL` variables as fallbacks. New deployments should use the `WIZARDNET_` names.
