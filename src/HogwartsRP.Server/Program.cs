using HogwartsRP.Core;
using HogwartsRP.Resources;
using HogwartsRP.Scripting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(o => o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "));
var logger = loggerFactory.CreateLogger("HogwartsRP.Server");
var permissions = new PermissionService();
var commands = new CommandRegistry(permissions);
var events = new EventBus();
var principal = new PermissionPrincipal("console", new HashSet<string>(["*"]));
commands.Register("status", "server.status", (_, _) => { Console.WriteLine("HogwartsRP framework ready"); return ValueTask.FromResult<string?>("ok"); });
var root = Path.GetFullPath(builder.Configuration["Resources:Path"] ?? Path.Combine(AppContext.BaseDirectory, "resources"));
var resources = new ResourceManager(root, _ => new MoonSharpRuntime(), (runtime, _) => new ResourceApi(events, commands, principal).RegisterInto(runtime));

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
