using HogwartsRP.Core;
using HogwartsRP.Scripting;

namespace HogwartsRP.Resources;

public sealed class ResourceManager
{
    private sealed class Running(ResourceInfo info, List<IDisposable> runtimes, List<RustWorkerProcessRuntime> rustWorkers)
    {
        private readonly object _sync = new();
        private bool _workerFailed;

        public ResourceInfo Info = info;
        public List<IDisposable> Runtimes = runtimes;
        public List<RustWorkerProcessRuntime> RustWorkers = rustWorkers;

        public void MarkStarted()
        {
            lock (_sync)
            {
                if (!_workerFailed) Info = Info with { State = ResourceState.Started, Error = null };
            }
        }

        public void MarkWorkerExited(int? exitCode)
        {
            lock (_sync)
            {
                _workerFailed = true;
                if (Info.State is ResourceState.Starting or ResourceState.Started)
                    Info = Info with { State = ResourceState.Failed, Error = exitCode is null
                        ? "A Rust worker exited unexpectedly (exit code unavailable)."
                        : $"A Rust worker exited unexpectedly with code {exitCode}." };
            }
        }
    }

    private readonly string _resourcesDirectory;
    private readonly Func<string, ILuaRuntime> _runtimeFactory;
    private readonly Action<ILuaRuntime, string>? _configure;
    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase);

    public ResourceManager(string resourcesDirectory, Func<string, ILuaRuntime> runtimeFactory, Action<ILuaRuntime, string>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcesDirectory);
        _resourcesDirectory = Path.GetFullPath(resourcesDirectory);
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        _configure = configure;
    }

    public IReadOnlyList<ResourceInfo> Status => _running.Values.Select(x => x.Info).OrderBy(x => x.Manifest.Name).ToArray();

    /// <summary>Raised when a Rust worker declares an inbound network event. The server must still authorize it.</summary>
    public event Action<string, string>? RustNetworkEventRegistered;

    /// <summary>Raised when a Rust worker registers a command.</summary>
    public event Action<string, string>? RustCommandRegistered;

    /// <summary>Raised when a Rust worker exits; resource status is marked Failed before observers are notified.</summary>
    public event Action<string, int?>? RustWorkerExited;

    public IReadOnlyList<ResourceManifest> Discover()
    {
        Directory.CreateDirectory(_resourcesDirectory);
        return Directory.GetDirectories(_resourcesDirectory).Order().Select(dir =>
        {
            var path = Path.Combine(dir, "fxmanifest.lua");
            if (!File.Exists(path)) throw new FileNotFoundException("Resource manifest is missing.", path);
            return ManifestParser.Parse(File.ReadAllText(path), Path.GetFileName(dir));
        }).ToArray();
    }

    public async ValueTask StartAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var manifest in DependencyResolver.Sort(Discover()))
        {
            var dir = Path.Combine(_resourcesDirectory, manifest.Name);
            var runtimes = new List<IDisposable>();
            var rustWorkers = new List<RustWorkerProcessRuntime>();
            var info = new ResourceInfo(manifest, dir, ResourceState.Starting);
            var running = new Running(info, runtimes, rustWorkers);
            try
            {
                var scripts = manifest.ServerScripts.Concat(manifest.SharedScripts).ToArray();
                if (scripts.Length > 0)
                {
                    var runtime = _runtimeFactory(manifest.Name);
                    runtimes.Add(runtime);
                    _configure?.Invoke(runtime, manifest.Name);
                    foreach (var script in scripts)
                    {
                        var fullPath = ResolveResourcePath(dir, script);
                        runtime.Execute(await File.ReadAllTextAsync(fullPath, cancellationToken), script);
                    }
                }

                foreach (var rustScript in manifest.RustScripts)
                {
                    var executable = ResolveResourcePath(dir, rustScript);
                    var worker = await RustWorkerProcessRuntime.StartAsync(executable, dir, cancellationToken: cancellationToken);
                    rustWorkers.Add(worker);
                    runtimes.Add(worker);
                    worker.CommandRegistered += command => RustCommandRegistered?.Invoke(manifest.Name, command);
                    worker.NetworkEventRegistered += eventName => RustNetworkEventRegistered?.Invoke(manifest.Name, eventName);
                    worker.WorkerExited += exitCode =>
                    {
                        running.MarkWorkerExited(exitCode);
                        NotifyRustWorkerExited(manifest.Name, exitCode);
                    };
                    foreach (var command in worker.RegisteredCommands) RustCommandRegistered?.Invoke(manifest.Name, command);
                    foreach (var eventName in worker.RegisteredNetworkEvents) RustNetworkEventRegistered?.Invoke(manifest.Name, eventName);
                }

                running.MarkStarted();
                _running[manifest.Name] = running;
            }
            catch (Exception ex)
            {
                foreach (var runtime in runtimes.AsEnumerable().Reverse()) runtime.Dispose();
                _running[manifest.Name] = new(new(manifest, dir, ResourceState.Failed, ex.Message), [], []);
                throw new InvalidOperationException($"Resource '{manifest.Name}' failed to start.", ex);
            }
        }
    }

    private void NotifyRustWorkerExited(string resourceName, int? exitCode)
    {
        var handlers = RustWorkerExited;
        if (handlers is null) return;
        foreach (Action<string, int?> handler in handlers.GetInvocationList())
        {
            try { handler(resourceName, exitCode); }
            catch { /* Host observers must not disrupt worker supervision. */ }
        }
    }

    public async ValueTask DispatchRustEventAsync(string eventName, object?[] arguments, CancellationToken cancellationToken = default)
    {
        foreach (var worker in _running.Values.SelectMany(x => x.RustWorkers))
            await worker.DispatchEventAsync(eventName, arguments, cancellationToken);
    }

    public ValueTask<string?> InvokeRustCommandAsync(string resourceName, string command, object?[] arguments, CancellationToken cancellationToken = default)
    {
        if (!_running.TryGetValue(resourceName, out var resource) || resource.RustWorkers.Count == 0)
            throw new KeyNotFoundException($"No running Rust worker exists for resource '{resourceName}'.");
        if (resource.RustWorkers.Count != 1)
            throw new InvalidOperationException($"Resource '{resourceName}' has multiple Rust workers; command routing is ambiguous.");
        return resource.RustWorkers[0].InvokeCommandAsync(command, arguments, cancellationToken);
    }

    private static string ResolveResourcePath(string resourceDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("Resource script path cannot be empty.");
        var root = Path.GetFullPath(resourceDirectory) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(resourceDirectory, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resource script path escapes the resource directory.");
        return fullPath;
    }

    public ValueTask StopAllAsync()
    {
        foreach (var resource in _running.Values.Reverse())
        {
            resource.Info = resource.Info with { State = ResourceState.Stopping };
            foreach (var runtime in resource.Runtimes.AsEnumerable().Reverse()) runtime.Dispose();
            resource.Info = resource.Info with { State = ResourceState.Stopped };
        }
        return ValueTask.CompletedTask;
    }
}
