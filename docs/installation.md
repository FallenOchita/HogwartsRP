# Installation and local launch

Requires the .NET 10 SDK. From the repository root:

```powershell
dotnet restore HogwartsRP.slnx
dotnet build HogwartsRP.slnx
dotnet test HogwartsRP.slnx
dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources
```

Enter `status` to see resource state and `quit` to stop cleanly. The server listens on `http://127.0.0.1:7777` for local development. This is a protocol preview, not a Hogwarts Legacy multiplayer server.

For a local protocol session without OIDC, enter `issue-ticket <account-id> <display name>` in the server console and copy the one-time development credential. Then run `dotnet run --project src/HogwartsRP.Launcher.App`, select that server, and paste the ticket when prompted. Tickets expire after two minutes and can be used once. This command is disabled when the server listener is not loopback.

To enable provider-neutral OIDC sign-in, register a native/public client with your OIDC provider. Configure the server with `Authentication__Oidc__Authority` and `Authentication__Oidc__ClientId`, and configure the launcher with matching `HogwartsRP__Oidc__Authority` and `HogwartsRP__Oidc__ClientId` environment variables. `Authority` must exactly equal the discovery document's `issuer`. The provider must support authorization code, PKCE S256, a loopback redirect to `127.0.0.1` with a dynamically chosen port, and RS256 signing keys in its JWKS metadata. The launcher opens the system browser; the server verifies the signed ID token and issues its own single-use ticket. Do not put a client secret in the launcher. Use HTTPS for remote servers and identity providers; HTTP is allowed only on loopback during local development.

The preview launcher authenticates a network session and asks Steam to start Hogwarts Legacy; it keeps the session open until you type `quit`. The game itself is not connected to the session because no compatible Hogwarts Legacy multiplayer adapter exists. The launcher detects conventional Steam/Epic installation paths but can launch only through Steam. It is not a packaged desktop UI. Direct character-creation is not supported and normal startup is used. See [launcher and character creation](launcher-and-characters.md).
