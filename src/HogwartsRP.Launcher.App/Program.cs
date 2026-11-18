using HogwartsRP.Launcher;
using HogwartsRP.Networking.Client;

Console.WriteLine("HogwartsRP Launcher (preview)");
Console.WriteLine("Select the server before starting Hogwarts Legacy.");
Console.WriteLine("For a local development server, use http://127.0.0.1:7777.");
Console.Write("Server name: ");
var name = Console.ReadLine()?.Trim();
Console.Write("Server address (HTTPS/WSS; local HTTP/WS is allowed): ");
var address = Console.ReadLine()?.Trim();
if (string.IsNullOrWhiteSpace(name) || !Uri.TryCreate(address, UriKind.Absolute, out var endpoint))
{
    Console.Error.WriteLine("A server name and valid server address are required.");
    return 2;
}

var server = new ServerSelection(Guid.NewGuid().ToString("N"), name, endpoint);
await using var networkClient = new WebSocketPlayerNetworkClient();
var connection = new ServerConnectionService(networkClient, new ConsoleJoinCredentialProvider());
connection.StatusMessage += message => Console.WriteLine($"[network] {message}");
connection.ServerEventReceived += networkEvent =>
    Console.WriteLine($"[server event] {networkEvent.Name} ({networkEvent.Arguments.Count} arguments)");

try
{
    var session = await connection.ConnectAsync(server);
    Console.WriteLine($"Authenticated session established: {session.DisplayName} ({session.Id}).");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Unable to connect to the selected server: {exception.Message}");
    return 1;
}

var launcher = new LauncherService(new WindowsGameInstallationDetector(), new SteamGameLauncher());
launcher.StatusChanged += status => Console.WriteLine($"[{status.State}] {status.Message}");
var result = await launcher.PlayAsync(server);
if (result.State == LaunchState.Failed)
{
    await connection.DisconnectAsync();
    return 1;
}

Console.WriteLine("The server session belongs to this launcher preview; Hogwarts Legacy is not connected to it yet.");
Console.WriteLine("Keep this window open for the session. Type 'quit' to disconnect and exit.");
while (true)
{
    var line = await Console.In.ReadLineAsync();
    if (line is null || line.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
    Console.WriteLine("Type 'quit' to disconnect.");
}

await connection.DisconnectAsync();
return 0;

sealed class ConsoleJoinCredentialProvider : IJoinCredentialProvider
{
    public ValueTask<string> GetJoinCredentialAsync(ServerSelection server, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"Enter a one-time development ticket issued by {server.DisplayName}.");
        Console.Write("Join ticket: ");
        return ValueTask.FromResult(Console.ReadLine()?.Trim() ?? string.Empty);
    }
}
