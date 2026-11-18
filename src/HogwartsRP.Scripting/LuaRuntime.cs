using HogwartsRP.Core;
using NLua;

namespace HogwartsRP.Scripting;

public sealed record LuaRuntimeLimits(int MaxInstructions = 2_000_000, int MaxSourceCharacters = 262_144);

public interface ILuaRuntime : IDisposable
{
    void Register(string name, Delegate callback);
    void Execute(string source, string chunkName);
    void CallFunction(object callback, params object?[] arguments);
    void Track(IDisposable subscription);
}

/// <summary>Lua 5.4 state using NLua/KeraLua. Each script gets a distinct state with restricted libraries and an instruction budget.</summary>
public sealed class NativeLua54Runtime : ILuaRuntime
{
    private const int HookInterval = 1_000;
    private readonly object _sync = new();
    private readonly Lua _lua = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly LuaFunction _resetBudget;
    private bool _disposed;
    private readonly LuaRuntimeLimits _limits;

    public NativeLua54Runtime(LuaRuntimeLimits? limits = null)
    {
        _limits = limits ?? new();
        if (_limits.MaxInstructions < HookInterval || _limits.MaxSourceCharacters < 1)
            throw new ArgumentOutOfRangeException(nameof(limits), "Lua limits must be positive and allow at least one hook interval.");

        _lua.State.Encoding = System.Text.Encoding.UTF8;
        // NLua initializes a CLR bridge internally; remove it before any resource script can run.
        _lua.DoString("luanet = nil");
        foreach (var global in new[] { "os", "io", "package", "coroutine", "dofile", "loadfile", "require", "collectgarbage", "pcall", "xpcall" })
            _lua[global] = null;

        var hookCalls = _limits.MaxInstructions / HookInterval;
        var bootstrap = $"local remaining = 0\nlocal function reset() remaining = {hookCalls} end\nlocal fail = error\ndebug.sethook(function() remaining = remaining - 1; if remaining <= 0 then fail('resource instruction budget exceeded', 0) end end, '', {HookInterval})\ndebug = nil\nreturn reset";
        _resetBudget = (LuaFunction)_lua.DoString(bootstrap, "@hogwartsrp/runtime-bootstrap")[0];
    }

    public void Register(string name, Delegate callback)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _lua.RegisterFunction(name, callback.Target, callback.Method);
        }
    }

    public void Execute(string source, string chunkName)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > _limits.MaxSourceCharacters) throw new InvalidOperationException($"Lua source exceeds {_limits.MaxSourceCharacters} characters.");
        lock (_sync)
        {
            ThrowIfDisposed();
            _resetBudget.Call();
            _lua.DoString(source, "@" + chunkName);
        }
    }

    public void CallFunction(object callback, params object?[] arguments)
    {
        if (callback is not LuaFunction function) throw new ArgumentException("Callback is not a Lua function.", nameof(callback));
        lock (_sync)
        {
            ThrowIfDisposed();
            _resetBudget.Call();
            function.Call(arguments);
        }
    }

    public void Track(IDisposable subscription)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _subscriptions.Add(subscription);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            _resetBudget.Dispose();
            _lua.Dispose();
        }
    }
}

public sealed class ResourceApi(EventBus events, CommandRegistry commands, PermissionPrincipal principal)
{
    public void RegisterInto(ILuaRuntime runtime)
    {
        runtime.Register("AddEventHandler", (Action<string, LuaFunction>)((name, callback) =>
            runtime.Track(events.Subscribe(name, args =>
            {
                runtime.CallFunction(callback, args);
                return ValueTask.CompletedTask;
            }))));
        runtime.Register("TriggerEvent", new Action<string, object[]>(TriggerLocalEvent));
        runtime.Register("RegisterCommand", (Action<string, LuaFunction, bool>)((name, callback, _) => commands.Register(name, "command." + name, async (_, args) =>
        {
            runtime.CallFunction(callback, string.Join(' ', args));
            await ValueTask.CompletedTask;
            return null;
        })));
        runtime.Register("RegisterNetEvent", (Action<string>)(_ => { }));
        runtime.Register("TriggerClientEvent", (Action<string, string>)((_, _) => throw new InvalidOperationException("No networking adapter is installed.")));
        runtime.Register("TriggerServerEvent", (Action<string>)((_) => throw new InvalidOperationException("No networking adapter is installed.")));
        runtime.Register("GetPlayerName", (Func<string, string>)(id => id == principal.Id ? principal.Id : ""));
    }

    private void TriggerLocalEvent(string name, params object?[] arguments) =>
        events.PublishAsync(name, arguments).AsTask().GetAwaiter().GetResult();
}
