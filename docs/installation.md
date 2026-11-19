# Installation and local launch

Requires the .NET 10 SDK and stable Rust toolchain. From the repository root:

```powershell
dotnet restore HogwartsRP.slnx
dotnet build HogwartsRP.slnx
dotnet test HogwartsRP.slnx
cargo build --manifest-path rust/Cargo.toml --release -p hogwartsrp-launcher
dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources
```

Enter `status` to see resource state and `quit` to stop cleanly. The server listens on `http://127.0.0.1:7777` for local development. This is a protocol preview, not a Hogwarts Legacy multiplayer server.

Run the Rust launcher executable from `rust/target/release/hogwartsrp-launcher.exe`. For a local protocol session without OIDC, enter `issue-ticket <account-id> <display name>` in the server console, copy the one-time development credential, and paste it into the launcher's masked **Loopback development ticket** field. Select **Connect with ticket**. Tickets expire after two minutes and can be used once. The server command is disabled when the listener is not loopback.

To enable provider-neutral OIDC sign-in, register a native/public client with your OIDC provider. Configure the server with `Authentication__Oidc__Authority` and `Authentication__Oidc__ClientId`, and configure the launcher with matching `HOGWARTSRP_OIDC_ISSUER` and `HOGWARTSRP_OIDC_CLIENT_ID` environment variables. The issuer must exactly equal the discovery document's `issuer`. The provider must support authorization code, PKCE S256, a loopback redirect to `127.0.0.1` with a dynamically chosen port, and RS256 signing keys in its JWKS metadata. The launcher opens the system browser; the server verifies the signed ID token and issues its own single-use ticket. Do not put a client secret in the launcher. Use HTTPS/WSS for remote servers and identity providers; HTTP/WS is allowed only on loopback during local development.

The Rust launcher authenticates its own network session and asks Steam to start Hogwarts Legacy; the game itself is not connected to that session because no compatible Hogwarts Legacy multiplayer adapter exists. The launcher detects conventional Steam/Epic install roots and custom roots supplied through `HOGWARTSRP_GAME_ROOTS`, but it can launch only through Steam. Epic launch, launcher auto-update, and the public server directory UI are not implemented. See [launcher and character creation](launcher-and-characters.md).
