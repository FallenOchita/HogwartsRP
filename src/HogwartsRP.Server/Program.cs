using System.Collections.Concurrent;
using HogwartsRP.Core;
using HogwartsRP.GameIntegration.Abstractions;
using HogwartsRP.Networking.Abstractions;
using HogwartsRP.Resources;
using HogwartsRP.Scripting;
using HogwartsRP.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(o => o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "));
var logger = loggerFactory.CreateLogger("HogwartsRP.Server");
var permissions = new PermissionService();
var commands = new CommandRegistry(permissions);
var consolePrincipal = new PermissionPrincipal("console", new HashSet<string>(["*"]));
commands.Register("status", "server.status", (_, _) =>
{
    Console.WriteLine("HogwartsRP framework ready");
    return ValueTask.FromResult<string?>("ok");
});

var joinTickets = new OneTimeJoinTicketStore();
var characterDataPath = Path.GetFullPath(builder.Configuration["Storage:CharactersPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "characters.json"));
var characterRepository = new JsonCharacterRepository(characterDataPath);
ICharacterSubmissionService characterSubmissions = new CharacterSubmissionService(characterRepository);
var listenUrl = builder.Configuration["Networking:ListenUrl"] ?? "http://127.0.0.1:7777";
if (Uri.TryCreate(listenUrl, UriKind.Absolute, out var configuredListenUri) && configuredListenUri.IsLoopback)
{
    commands.Register("issue-ticket", "server.auth.issue", (_, commandArgs) =>
    {
        if (commandArgs.Length < 2)
        {
            Console.WriteLine("Usage: issue-ticket <account-id> <display name>");
            return ValueTask.FromResult<string?>(null);
        }

        try
        {
            var credential = joinTickets.Issue(new AuthenticatedPlayer(commandArgs[0], string.Join(' ', commandArgs.Skip(1))));
            Console.WriteLine($"One-time development credential (expires in 2 minutes): {credential}");
        }
        catch (ArgumentException exception) { Console.WriteLine(exception.Message); }
        return ValueTask.FromResult<string?>(null);
    });
}
else logger.LogWarning("The issue-ticket development command is disabled because the server listener is not loopback.");

var oidcAuthority = builder.Configuration["Authentication:Oidc:Authority"];
var oidcClientId = builder.Configuration["Authentication:Oidc:ClientId"];
if (string.IsNullOrWhiteSpace(oidcAuthority) != string.IsNullOrWhiteSpace(oidcClientId))
    throw new InvalidOperationException("Configure both Authentication:Oidc:Authority and Authentication:Oidc:ClientId to enable OIDC sign-in.");
using var oidcTicketExchange = string.IsNullOrWhiteSpace(oidcAuthority)
    ? null
    : new OidcTicketExchange(oidcAuthority, oidcClientId!, joinTickets);

var eventPolicy = new NetworkEventPolicy();
var sessions = new PlayerSessionRegistry();
var serverName = builder.Configuration["Server:Name"] ?? "HogwartsRP Server";
await using var networking = new WebSocketNetworkingAdapter(listenUrl, joinTickets, eventPolicy, sessions, loggerFactory, oidcTicketExchange, characterSubmissions, serverName);
networking.PlayerConnected += session =>
    logger.LogInformation("Player {DisplayName} connected with session {SessionId}", session.DisplayName, session.Id);
networking.PlayerDisconnected += session =>
    logger.LogInformation("Player {DisplayName} disconnected", session.DisplayName);

var root = Path.GetFullPath(builder.Configuration["Resources:Path"] ?? Path.Combine(AppContext.BaseDirectory, "resources"));
var workerAssembly = typeof(WorkerAssemblyMarker).Assembly.Location;
var resourceWorkers = new ConcurrentDictionary<string, LuaWorkerProcessRuntime>(StringComparer.OrdinalIgnoreCase);
var resources = new ResourceManager(root, resourceName =>
{
    var worker = new LuaWorkerProcessRuntime(workerAssembly);
    worker.CommandRegistered += name => commands.Register(name, "command." + name,
        async (_, commandArgs) => await worker.InvokeCommandAsync(name, commandArgs));
    worker.EventRaised += (sourceWorker, eventName, eventArguments) =>
    {
        foreach (var target in resourceWorkers.Values)
        {
            if (ReferenceEquals(sourceWorker, target)) continue;
            _ = RelayEventAsync(target, eventName, eventArguments);
        }
    };
    worker.NetworkEventRegistered += name => eventPolicy.RegisterClientEvent(resourceName, name);
    worker.ClientEventRequested += (sourceWorker, playerId, eventName, eventArguments) =>
    {
        _ = sourceWorker;
        _ = SendClientEventAsync(playerId, eventName, eventArguments);
    };
    resourceWorkers.TryAdd(resourceName, worker);
    return worker;
});

var registeredRustCommands = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
resources.RustCommandRegistered += (resourceName, commandName) =>
{
    var permissionKey = $"Resources:RustCommandPermissions:{resourceName}:{commandName}";
    var permission = builder.Configuration[permissionKey]
        ?? $"resource.{resourceName}.command.{commandName}";
    try
    {
        if (!registeredRustCommands.TryAdd(commandName, 0))
            throw new InvalidOperationException($"A command named '{commandName}' is already reserved.");
        commands.Register(commandName, permission,
            (_, commandArgs) => resources.InvokeRustCommandAsync(resourceName, commandName, commandArgs, CancellationToken.None));
        logger.LogInformation("Registered Rust command {CommandName} for resource {ResourceName} with permission {Permission}",
            commandName, resourceName, permission);
    }
    catch (Exception ex)
    {
        logger.LogWarning("Rejected Rust command {CommandName} from resource {ResourceName}: {Message}",
            commandName, resourceName, ex.Message);
    }
};
resources.RustNetworkEventRegistered += (resourceName, eventName) =>
{
    try
    {
        eventPolicy.RegisterClientEvent(resourceName, eventName);
        logger.LogInformation("Allowlisted client event {EventName} for Rust resource {ResourceName}", eventName, resourceName);
    }
    catch (Exception ex)
    {
        logger.LogWarning("Rejected Rust network event {EventName} from resource {ResourceName}: {Message}",
            eventName, resourceName, ex.Message);
    }
};

networking.ServerEventReceived += async networkEvent =>
{
    foreach (var resourceName in eventPolicy.GetOwners(networkEvent.Name))
    {
        var arguments = new object?[] { networkEvent.Player.Id, networkEvent.Player.DisplayName }
            .Concat(networkEvent.Arguments).ToArray();
        if (resourceWorkers.TryGetValue(resourceName, out var target))
        {
            try { await target.DispatchEventAsync(networkEvent.Name, arguments); }
            catch (Exception ex)
            {
                logger.LogWarning("Client event {EventName} failed in Lua resource {ResourceName}: {Message}", networkEvent.Name, resourceName, ex.Message);
            }
        }
        try { await resources.DispatchRustEventToResourceAsync(resourceName, networkEvent.Name, arguments); }
        catch (Exception ex)
        {
            logger.LogWarning("Client event {EventName} failed in Rust resource {ResourceName}: {Message}", networkEvent.Name, resourceName, ex.Message);
        }
    }
};

async Task SendClientEventAsync(string playerId, string eventName, object?[] arguments)
{
    try { await networking.SendToClientAsync(playerId, eventName, arguments); }
    catch (Exception ex)
    {
        logger.LogWarning("Server event {EventName} could not be sent to player {PlayerId}: {Message}", eventName, playerId, ex.Message);
    }
}

async Task RelayEventAsync(LuaWorkerProcessRuntime target, string eventName, object?[] arguments)
{
    try { await target.DispatchEventAsync(eventName, arguments); }
    catch (Exception ex)
    {
        logger.LogWarning("Cross-resource Lua event {EventName} failed in worker {WorkerId}: {Message}", eventName, target.WorkerProcessId, ex.Message);
    }
}

try
{
    await resources.StartAllAsync();
    await networking.StartAsync();
    logger.LogInformation("HogwartsRP server started. Loaded {Count} resources from {Path}", resources.Status.Count(x => x.State == ResourceState.Started), root);
    logger.LogInformation("WebSocket endpoint is {Endpoint}; type 'status' or 'quit'.", listenUrl.TrimEnd('/') + "/connect");
    while (true)
    {
        var line = await Console.In.ReadLineAsync();
        if (line is null || line.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
        if (line.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var resource in resources.Status)
                Console.WriteLine($"{resource.Manifest.Name}: {resource.State}{(resource.Error is null ? "" : " - " + resource.Error)}");
            continue;
        }

        var split = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length > 0)
        {
            try { await commands.ExecuteAsync(consolePrincipal, split[0], split.Skip(1).ToArray()); }
            catch (Exception ex) { logger.LogWarning("{Message}", ex.Message); }
        }
    }
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Server startup failed");
    Environment.ExitCode = 1;
}
finally
{
    await networking.StopAsync();
    await resources.StopAllAsync();
    logger.LogInformation("HogwartsRP server stopped");
}
