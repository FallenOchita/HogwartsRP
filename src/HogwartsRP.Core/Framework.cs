using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HogwartsRP.Core;

public sealed record ResourceManifest(string Name, string FxVersion, string Game, string? Author, string? Description, string? Version, IReadOnlyList<string> ServerScripts, IReadOnlyList<string> ClientScripts, IReadOnlyList<string> SharedScripts, IReadOnlyList<string> Dependencies)
{
    /// <summary>Precompiled Rust worker executables, relative to the resource directory.</summary>
    public IReadOnlyList<string> RustScripts { get; init; } = [];
}

/// <summary>Parses a deliberately restricted fxmanifest subset. Manifests are data; no Lua code is evaluated.</summary>
public static partial class ManifestParser
{
    [GeneratedRegex("^(?<key>[a-z_]+)\\s+['\\\"](?<value>[^'\\\"]+)['\\\"]\\s*$", RegexOptions.IgnoreCase)] private static partial Regex ScalarLine();
    [GeneratedRegex("^(?<key>server_script|client_script|shared_script|rust_script|dependency|dependencies)\\s+['\\\"](?<value>[^'\\\"]+)['\\\"]\\s*$", RegexOptions.IgnoreCase)] private static partial Regex ListLine();

    public static ResourceManifest Parse(string text, string folderName)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split("--", 2)[0].Trim();
            if (line.Length == 0) continue;
            var match = ListLine().Match(line);
            if (match.Success)
            {
                var key = match.Groups["key"].Value;
                (lists.TryGetValue(key, out var items) ? items : lists[key] = []).Add(match.Groups["value"].Value);
                continue;
            }
            match = ScalarLine().Match(line);
            if (match.Success) values[match.Groups["key"].Value] = match.Groups["value"].Value;
            else if (!line.StartsWith("fx_version", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("game", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"Unsupported manifest syntax: {line}");
        }
        var name = values.GetValueOrDefault("name", folderName);
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9_-]{1,64}$")) throw new FormatException("Resource name must contain only letters, numbers, '_' or '-'.");
        var fx = values.GetValueOrDefault("fx_version") ?? throw new FormatException("fx_version is required.");
        var game = values.GetValueOrDefault("game") ?? throw new FormatException("game is required.");
        List<string> Get(params string[] keys) => keys.SelectMany(k => lists.GetValueOrDefault(k) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new(name, fx, game, values.GetValueOrDefault("author"), values.GetValueOrDefault("description"), values.GetValueOrDefault("version"), Get("server_script"), Get("client_script"), Get("shared_script"), Get("dependency", "dependencies"))
        {
            RustScripts = Get("rust_script")
        };
    }
}

public enum ResourceState { Discovered, Starting, Started, Restarting, Stopping, Stopped, Failed }
public sealed record ResourceInfo(ResourceManifest Manifest, string Directory, ResourceState State, string? Error = null);

public static class DependencyResolver
{
    public static IReadOnlyList<ResourceManifest> Sort(IEnumerable<ResourceManifest> manifests)
    {
        var map = manifests.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var result = new List<ResourceManifest>(); var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(ResourceManifest item)
        {
            if (done.Contains(item.Name)) return;
            if (!active.Add(item.Name)) throw new InvalidOperationException($"Circular resource dependency involving '{item.Name}'.");
            foreach (var dep in item.Dependencies.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!map.TryGetValue(dep, out var target)) throw new InvalidOperationException($"Resource '{item.Name}' requires missing dependency '{dep}'.");
                Visit(target);
            }
            active.Remove(item.Name); done.Add(item.Name); result.Add(item);
        }
        foreach (var item in map.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) Visit(item);
        return result;
    }
}

public sealed record PermissionPrincipal(string Id, IReadOnlySet<string> Permissions);
public sealed class PermissionService
{
    public bool IsAllowed(PermissionPrincipal principal, string permission) => principal.Permissions.Contains("*") || principal.Permissions.Contains(permission);
    public void Demand(PermissionPrincipal principal, string permission) { if (!IsAllowed(principal, permission)) throw new UnauthorizedAccessException($"Permission denied: {permission}"); }
}

public sealed class EventBus
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<object?[], ValueTask>>> _handlers = new(StringComparer.Ordinal);
    public IDisposable Subscribe(string eventName, Func<object?[], ValueTask> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName) || eventName.Length > 128) throw new ArgumentException("Invalid event name.", nameof(eventName));
        var map = _handlers.GetOrAdd(eventName, _ => new()); var id = Guid.NewGuid(); map[id] = handler;
        return new Subscription(() => map.TryRemove(id, out _));
    }
    public async ValueTask PublishAsync(string eventName, params object?[] args)
    {
        if (args.Length > 32) throw new ArgumentException("Event argument limit exceeded.");
        if (_handlers.TryGetValue(eventName, out var handlers)) foreach (var handler in handlers.Values) await handler(args);
    }
    private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}

public sealed class CommandRegistry(PermissionService permissions)
{
    private sealed record Entry(string Permission, Func<PermissionPrincipal, string[], ValueTask<string?>> Handler);
    private readonly ConcurrentDictionary<string, Entry> _commands = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? CommandRegistered;
    public void Register(string name, string permission, Func<PermissionPrincipal, string[], ValueTask<string?>> handler)
    {
        if (!Regex.IsMatch(name, "^[a-zA-Z][a-zA-Z0-9_-]{0,31}$")) throw new ArgumentException("Invalid command name.");
        if (!_commands.TryAdd(name, new(permission, handler))) throw new InvalidOperationException($"Command '{name}' already exists.");
        CommandRegistered?.Invoke(name);
    }
    public async ValueTask<string?> ExecuteAsync(PermissionPrincipal principal, string name, string[] args)
    {
        if (!_commands.TryGetValue(name, out var entry)) throw new KeyNotFoundException($"Unknown command '{name}'.");
        permissions.Demand(principal, entry.Permission);
        return await entry.Handler(principal, args);
    }
}
