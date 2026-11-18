# Installation and local launch

Requires the .NET 10 SDK. From the repository root:

```powershell
dotnet restore HogwartsRP.slnx
dotnet build HogwartsRP.slnx
dotnet test HogwartsRP.slnx
dotnet run --project src/HogwartsRP.Server -- --Resources:Path=resources
```

Enter `status` to see resource state and `quit` to stop cleanly. This launches the framework prototype only; no game or network integration is provided.

Run `dotnet run --project src/HogwartsRP.Launcher.App` to open the preview console launcher, enter a secure server address, and request launch. It currently detects conventional Steam/Epic installation paths but can launch only through Steam. It is not a packaged desktop UI. Direct character-creation is not supported and normal startup is used. See [launcher and character creation](launcher-and-characters.md).
