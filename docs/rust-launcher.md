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
- Add, save, select, and remove server profiles with atomic JSON persistence.
- Read-only `GET /status` checks with a five-second timeout.
- Open the central browser account portal when `HOGWARTSRP_ACCOUNT_PORTAL_URL` is configured.
- Browser OIDC authorization-code flow with PKCE S256 and a loopback callback.
- Exchange the server challenge and provider ID token for a server-issued, one-time join credential; the server remains responsible for validating the token.
- Authenticated WebSocket connection to `/connect`, server-owned session handshake, event sending, and correlated character submission.
- Masked manual entry of one-time loopback development tickets for local testing without OIDC.
- Character creation form with client-side validation for name, house, biography, background, and optional wand details; the server repeats validation and owns persistence.
- Detect Steam and Epic install roots, plus custom roots supplied through `HOGWARTSRP_GAME_ROOTS`; Steam launch requests use app 990080.
- Steam continues to perform its normal ownership and sign-in checks.

## Remaining migration boundaries

The Rust executable now owns the OIDC, WebSocket, character draft, profile, status, and game-launch paths; it no longer calls the C# launcher library. Remaining work includes an always-on receive loop for server-originated events, reconnect/backoff and session recovery, full Steam library VDF discovery, Epic launch support, launcher auto-update, and public server directory/account-linking UI beyond opening the configured browser portal.

The WebSocket client currently queues server events when it is reading a character-submission response; it does not yet run a separate continuous receive task while idle. No in-game multiplayer or direct character creation inside Hogwarts Legacy is implied.

## Configuration

- `HOGWARTSRP_ACCOUNT_PORTAL_URL`: optional central account portal URL.
- `HOGWARTSRP_OIDC_ISSUER` and `HOGWARTSRP_OIDC_CLIENT_ID`: required together for browser sign-in and server join-ticket acquisition. The client ID is public; no provider secret is embedded.
- For loopback development only, issue a one-time ticket with the server's `issue-ticket` console command and enter it in the launcher's masked development-ticket field.
- `HOGWARTSRP_GAME_ROOTS`: optional OS path-list of additional Hogwarts Legacy install roots.

Production OIDC endpoints and remote game servers must use HTTPS/WSS. HTTP/WS is accepted only for loopback development. No production URLs or credentials are hard-coded.

## Validation

CI formats the Rust workspace and runs Rust library/SDK tests on Ubuntu and Windows. The native desktop launcher is built in release mode on Windows CI. No claim is made that browser authentication, character submission, or in-game multiplayer has been ported by this milestone.
