using HogwartsRP.Launcher;

Console.WriteLine("HogwartsRP Launcher (preview)");
Console.WriteLine("Select the server to use before starting Hogwarts Legacy.");
Console.Write("Server name: ");
var name = Console.ReadLine()?.Trim();
Console.Write("Server address (wss:// or https://): ");
var address = Console.ReadLine()?.Trim();
if (string.IsNullOrWhiteSpace(name) || !Uri.TryCreate(address, UriKind.Absolute, out var endpoint))
{
    Console.Error.WriteLine("A server name and valid secure server address are required.");
    return 2;
}

var service = new LauncherService(new WindowsGameInstallationDetector(), new SteamGameLauncher());
service.StatusChanged += status => Console.WriteLine($"[{status.State}] {status.Message}");
var result = await service.PlayAsync(new ServerSelection(Guid.NewGuid().ToString("N"), name, endpoint));
if (result.State == LaunchState.Failed) return 1;
Console.WriteLine("The game platform owns game startup and authentication. Complete character creation in game when a compatible integration is available.");
return 0;
