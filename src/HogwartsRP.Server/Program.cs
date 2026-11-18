using HogwartsRP.Core;
using HogwartsRP.Resources;
using HogwartsRP.Scripting;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(o => o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "));
var logger = loggerFactory.CreateLogger("HogwartsRP.Server");
var permissions = new PermissionService();
var commands = new CommandRegistry(permissions);
var principal = new PermissionPrincipal("console", new HashSet<string>(["*"]));
commands.Register("status", "server.status", (_, _) => { Console.WriteLine("HogwartsRP framework ready"); return ValueTask.FromResult<string?>("ok"); });
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
    resourceWorkers.TryAdd(resourceName, worker);
    return worker;
});

async Task RelayEventAsync(LuaWorkerProcessRuntime target, string eventName, object?[] arguments)
{
    try { await target.DispatchEventAsync(eventName, arguments); }
    catch (Exception ex) { logger.LogWarning("Cross-resource Lua event {EventName} failed in worker {WorkerId}: {Message}", eventName, target.WorkerProcessId, ex.Message); }
}

try
{
    await resources.StartAllAsync();
    logger.LogInformation("HogwartsRP server started. Loaded {Count} resources from {Path}", resources.Status.Count(x => x.State == ResourceState.Started), root);
    logger.LogInformation("Type 'status' or 'quit'. This host has no game/network adapter.");
    while (true)
    {
        var line = await Console.In.ReadLineAsync();
        if (line is null || line.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
        if (line.Equals("status", StringComparison.OrdinalIgnoreCase))
        { foreach (var resource in resources.Status) Console.WriteLine($"{resource.Manifest.Name}: {resource.State}{(resource.Error is null ? "" : " - " + resource.Error)}"); continue; }
        var split = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length > 0) try { await commands.ExecuteAsync(principal, split[0], split.Skip(1).ToArray()); } catch (Exception ex) { logger.LogWarning("{Message}", ex.Message); }
    }
}
catch (Exception ex) { logger.LogCritical(ex, "Server startup failed"); Environment.ExitCode = 1; }
finally { await resources.StopAllAsync(); logger.LogInformation("HogwartsRP server stopped"); }
