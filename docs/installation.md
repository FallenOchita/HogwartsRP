# Installation and local launch

Requires the .NET 10 SDK. From the repository root:

```powershell
dotnet restore HogwartsRP.slnx
dotnet build HogwartsRP.slnx
dotnet test HogwartsRP.slnx
dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources
```

Enter `status` to see resource state and `quit` to stop cleanly. The server listens on `http://127.0.0.1:7777` for local development. This is a protocol preview, not a Hogwarts Legacy multiplayer server.

For a local protocol session, enter `issue-ticket <account-id> <display name>` in the server console and copy the one-time credential. Then run `dotnet run --project src/HogwartsRP.Launcher.App`, select that server, and paste the ticket when prompted. Tickets expire after two minutes and can be used once. Remote servers require HTTPS/WSS plus an operator or upstream identity provider that issues a trusted credential.

The preview launcher authenticates a network session and asks Steam to start Hogwarts Legacy; it keeps the session open until you type `quit`. The game itself is not connected to the session because no compatible Hogwarts Legacy multiplayer adapter exists. The launcher detects conventional Steam/Epic installation paths but can launch only through Steam. It is not a packaged desktop UI. Direct character-creation is not supported and normal startup is used. See [launcher and character creation](launcher-and-characters.md).
