using System.Text.Json;
using HogwartsRP.Core;

namespace HogwartsRP.Scripting;

public sealed record LuaWorkerRequest(string Type, string? Id = null, string? Script = null, string? ChunkName = null, string? Name = null, string[]? Arguments = null, string? EventName = null, object?[]? EventArguments = null);
public sealed record LuaWorkerResponse(string Type, string? Id = null, string? Name = null, string? Result = null, string? Error = null, string? EventName = null, object?[]? EventArguments = null, string? PlayerId = null);

/// <summary>Worker-side line protocol. Lua executes only in the worker; stdout is reserved for protocol messages.</summary>
public static class LuaWorkerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var runtime = new NativeLua54Runtime();
        var events = new EventBus();
        var commands = new CommandRegistry(new PermissionService());
        var console = new PermissionPrincipal("console", new HashSet<string> { "*" });
        runtime.Register("print", new Action<object?[]>(WritePrint));
        new ResourceApi(
            events,
            commands,
            console,
            (name, args) => Write(new LuaWorkerResponse("event_raised", EventName: name, EventArguments: ValidateEventArguments(args))),
            name => Write(new LuaWorkerResponse("network_event_registered", Name: name)),
            (name, playerId, args) => Write(new LuaWorkerResponse("client_event_requested", EventName: name, PlayerId: playerId, EventArguments: ValidateEventArguments(args))))
            .RegisterInto(runtime);
        commands.CommandRegistered += name => Write(new LuaWorkerResponse("command_registered", Name: name));
        Write(new LuaWorkerResponse("ready"));

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken);
            if (line is null) break;
            LuaWorkerRequest? request;
            try { request = JsonSerializer.Deserialize<LuaWorkerRequest>(line, JsonOptions); }
            catch (JsonException ex) { Write(new LuaWorkerResponse("protocol_error", Error: ex.Message)); continue; }
            if (request is null) continue;
            if (request.Type == "stop") break;
            try
            {
                LinuxWorkerLimits.ResetCpuBudgetForRequest();
                switch (request.Type)
                {
                    case "execute":
                        runtime.Execute(request.Script ?? string.Empty, request.ChunkName ?? "resource.lua");
                        Write(new LuaWorkerResponse("executed", request.Id));
                        break;
                    case "invoke_command":
                        await commands.ExecuteAsync(console, request.Name ?? string.Empty, request.Arguments ?? []);
                        Write(new LuaWorkerResponse("command_result", request.Id));
                        break;
                    case "dispatch_event":
                        await events.PublishAsync(request.EventName ?? string.Empty, ConvertEventArguments(request.EventArguments));
                        Write(new LuaWorkerResponse("event_dispatched", request.Id));
                        break;
                    default:
                        Write(new LuaWorkerResponse("protocol_error", request.Id, Error: $"Unsupported worker request '{request.Type}'."));
                        break;
                }
            }
            catch (Exception ex) { Write(new LuaWorkerResponse("request_error", request.Id, Error: ex.ToString())); }
        }
        return 0;
    }

    private static void Write(LuaWorkerResponse response) => Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
    private static void WritePrint(params object?[] values) => Console.Error.WriteLine(string.Join("\t", values.Select(v => v?.ToString() ?? "nil")));

    private static object?[] ValidateEventArguments(object?[] arguments)
    {
        if (arguments.Length > 32) throw new InvalidOperationException("Network-safe local event argument limit exceeded.");
        return arguments.Select(value => value switch
        {
            null or string or bool or byte or short or int or long or float or double or decimal => value,
            _ => throw new InvalidOperationException("Cross-resource events support only strings, numbers, booleans, and nil arguments.")
        }).ToArray();
    }

    private static object?[] ConvertEventArguments(object?[]? arguments) => (arguments ?? []).Select(value =>
    {
        if (value is not JsonElement element) return value;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException("Cross-resource event values must be primitive JSON values.")
        };
    }).ToArray();
}
