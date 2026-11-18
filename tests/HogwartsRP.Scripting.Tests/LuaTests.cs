using HogwartsRP.Scripting;
using HogwartsRP.Core;
using NLua.Exceptions;
namespace HogwartsRP.Scripting.Tests;

public class LuaTests
{
    [Fact]
    public void ExecutesLua54WithoutHostLibraries()
    {
        using var lua = new NativeLua54Runtime();
        lua.Execute("local number <const> = 6 * 7; assert(_VERSION == 'Lua 5.4' and number == 42)", "test.lua");
        lua.Execute("assert(os == nil and io == nil and package == nil and debug == nil and luanet == nil)", "sandbox.lua");
        Assert.Throws<LuaScriptException>(() => lua.Execute("os.execute('whoami')", "blocked.lua"));
    }

    [Fact]
    public void StopsScriptsThatExceedInstructionBudget()
    {
        using var lua = new NativeLua54Runtime(new LuaRuntimeLimits(MaxInstructions: 50_000));
        Assert.Throws<LuaScriptException>(() => lua.Execute("while true do end", "loop.lua"));
    }

    [Fact]
    public async Task ResourceEventsAndCommandsRunInsideTheLuaState()
    {
        var events = new EventBus();
        var permissions = new PermissionService();
        var commands = new CommandRegistry(permissions);
        var console = new PermissionPrincipal("console", new HashSet<string> { "*" });
        using var lua = new NativeLua54Runtime();
        new ResourceApi(events, commands, console).RegisterInto(lua);
        lua.Execute("received = ''", "api.lua");
        lua.Execute("AddEventHandler('probe', function(value) received = value end)", "event-register.lua");
        lua.Execute("TriggerEvent('probe', 'event-ok')", "event-trigger.lua");
        lua.Execute("RegisterCommand('echo', function(value) received = value end, false)", "command-register.lua");
        lua.Execute("assert(received == 'event-ok')", "event-check.lua");
        await commands.ExecuteAsync(console, "echo", ["command", "ok"]);
        lua.Execute("assert(received == 'command ok')", "command-check.lua");
    }

    [Fact]
    public async Task WorkerExecutesScriptsInASeparateProcessAndRoutesCommands()
    {
        using var worker = new LuaWorkerProcessRuntime(typeof(WorkerAssemblyMarker).Assembly.Location);
        Assert.NotEqual(Environment.ProcessId, worker.WorkerProcessId);
        var permissions = new PermissionService();
        var commands = new CommandRegistry(permissions);
        var console = new PermissionPrincipal("console", new HashSet<string> { "*" });
        worker.CommandRegistered += name => commands.Register(name, "command." + name, async (_, args) => await worker.InvokeCommandAsync(name, args));
        worker.Execute("shared_resource_value = 42", "first.lua");
        worker.Execute("assert(shared_resource_value == 42)", "second.lua");
        worker.Execute("RegisterCommand('worker_ping', function(args) print('handled ' .. args) end, false)", "worker.lua");
        await commands.ExecuteAsync(console, "worker_ping", ["through", "worker"]);
    }

    [Fact]
    public async Task CrossResourceEventIsRelayedBetweenWorkers()
    {
        using var source = new LuaWorkerProcessRuntime(typeof(WorkerAssemblyMarker).Assembly.Location);
        using var target = new LuaWorkerProcessRuntime(typeof(WorkerAssemblyMarker).Assembly.Location);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        target.WorkerOutput += line => { if (line.Contains("received:ping", StringComparison.Ordinal)) received.TrySetResult(); };
        target.Execute("AddEventHandler('global_event', function(value) print('received:' .. value) end)", "listener.lua");
        source.EventRaised += (sourceWorker, name, args) => _ = target.DispatchEventAsync(name, args).AsTask();
        source.Execute("TriggerEvent('global_event', 'ping')", "publisher.lua");
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
