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
}
