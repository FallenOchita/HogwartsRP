using HogwartsRP.Core;
using MoonSharp.Interpreter;

namespace HogwartsRP.Scripting;

public interface ILuaRuntime : IDisposable
{
    void Register(string name, Delegate callback);
    void Execute(string source, string chunkName);
}

/// <summary>Restricted MoonSharp state. MoonSharp implements Lua 5.2, not Lua 5.4; this is an interim adapter.</summary>
public sealed class MoonSharpRuntime : ILuaRuntime
{
    private readonly Script _script = new(CoreModules.Basic | CoreModules.Table | CoreModules.String | CoreModules.Math);
    public MoonSharpRuntime()
    {
        _script.Options.DebugPrint = _ => { };
        _script.Options.CheckThreadAccess = true;
        _script.Options.ScriptLoader = null;
        _script.Globals["dofile"] = DynValue.Nil;
        _script.Globals["loadfile"] = DynValue.Nil;
        _script.Globals["load"] = DynValue.Nil;
        _script.Globals["require"] = DynValue.Nil;
    }
    public void Register(string name, Delegate callback) => _script.Globals[name] = callback;
    public void Execute(string source, string chunkName) => _script.DoString(source, null, chunkName);
    public void Dispose() => _script.Options.ScriptLoader = null;
}

public sealed class ResourceApi(EventBus events, CommandRegistry commands, PermissionPrincipal principal)
{
    public void RegisterInto(ILuaRuntime runtime)
    {
        runtime.Register("AddEventHandler", (Action<string, Action<DynValue>>)((name, callback) => events.Subscribe(name, args =>
        { callback(DynValue.Nil); return ValueTask.CompletedTask; })));
        runtime.Register("TriggerEvent", (Action<string>)(name => events.PublishAsync(name).AsTask().GetAwaiter().GetResult()));
        runtime.Register("RegisterCommand", (Action<string, DynValue, bool>)((name, callback, _) => commands.Register(name, "command." + name, async (_, args) =>
        { callback.Function.Call(DynValue.NewString(string.Join(' ', args))); await ValueTask.CompletedTask; return null; })));
        runtime.Register("RegisterNetEvent", (Action<string>)(_ => { }));
        runtime.Register("TriggerClientEvent", (Action<string, string>)((_, _) => throw new InvalidOperationException("No networking adapter is installed.")));
        runtime.Register("TriggerServerEvent", (Action<string>)((_) => throw new InvalidOperationException("No networking adapter is installed.")));
        runtime.Register("GetPlayerName", (Func<string, string>)(id => id == principal.Id ? principal.Id : ""));
    }
}
