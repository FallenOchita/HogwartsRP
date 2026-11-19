using HogwartsRP.Core;
using HogwartsRP.Scripting;
using System.Collections.Concurrent;

namespace HogwartsRP.Resources;

public sealed class ResourceManager
{
    private sealed class Running(ResourceInfo info, List<IDisposable> runtimes, List<RustWorkerProcessRuntime> rustWorkers)
    {
        private readonly object _sync = new();
        private bool _stopping;
        private int _restartAttempts;
        private readonly HashSet<RustWorkerProcessRuntime> _handledExits = [];

        public ResourceInfo Info = info;
        public List<IDisposable> Runtimes = runtimes;
        public List<RustWorkerProcessRuntime> RustWorkers = rustWorkers;
        public bool IsStopping { get { lock (_sync) return _stopping; } }

        public ResourceInfo Snapshot { get { lock (_sync) return Info; } }

        public void MarkStarted()
        {
            lock (_sync)
            {
                if (!_stopping && Info.State == ResourceState.Starting)
                    Info = Info with { State = ResourceState.Started, Error = null };
            }
        }

        public bool TryHandleExit(RustWorkerProcessRuntime worker)
        {
            lock (_sync) return _handledExits.Add(worker);
        }

        public IReadOnlyList<RustWorkerProcessRuntime> WorkerSnapshot()
        {
            lock (_sync) return RustWorkers.ToArray();
        }

        public RustWorkerProcessRuntime GetSingleWorker()
        {
            lock (_sync)
            {
                if (RustWorkers.Count == 0) throw new KeyNotFoundException();
                if (RustWorkers.Count != 1) throw new InvalidOperationException("Resource has multiple Rust workers; command routing is ambiguous.");
                return RustWorkers[0];
            }
        }

        public bool MarkWorkerExited(int? exitCode, bool restartEnabled)
        {
            lock (_sync)
            {
                if (_stopping) return false;
                var message = exitCode is null
                    ? "A Rust worker exited unexpectedly (exit code unavailable)."
                    : $"A Rust worker exited unexpectedly with code {exitCode}.";
                Info = Info with { State = restartEnabled ? ResourceState.Restarting : ResourceState.Failed, Error = message };
                return restartEnabled;
            }
        }

        public int NextRestartAttempt(int maxRestarts)
        {
            lock (_sync)
            {
                if (_stopping || _restartAttempts >= maxRestarts) return 0;
                return ++_restartAttempts;
            }
        }

        public bool ReplaceWorker(RustWorkerProcessRuntime previous, RustWorkerProcessRuntime replacement)
        {
            lock (_sync)
            {
                if (_stopping) return false;
                var index = RustWorkers.IndexOf(previous);
                if (index < 0) return false;
                RustWorkers[index] = replacement;
                var runtimeIndex = Runtimes.IndexOf(previous);
                if (runtimeIndex >= 0) Runtimes[runtimeIndex] = replacement;
                Info = Info with { State = ResourceState.Started, Error = null };
                return true;
            }
        }

        public void SetRestartFailed(string error)
        {
            lock (_sync)
            {
                if (!_stopping) Info = Info with { State = ResourceState.Failed, Error = error };
            }
        }

        public void BeginStopping()
        {
            lock (_sync)
            {
                _stopping = true;
                Info = Info with { State = ResourceState.Stopping };
            }
        }

        public void MarkStopped()
        {
            lock (_sync) Info = Info with { State = ResourceState.Stopped };
        }
    }

    private readonly string _resourcesDirectory;
    private readonly Func<string, ILuaRuntime> _runtimeFactory;
    private readonly Action<ILuaRuntime, string>? _configure;
    private readonly RustWorkerRestartPolicy _restartPolicy;
    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<Task, byte> _restartTasks = new();

    public ResourceManager(string resourcesDirectory, Func<string, ILuaRuntime> runtimeFactory,
        Action<ILuaRuntime, string>? configure = null, RustWorkerRestartPolicy? restartPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcesDirectory);
        _resourcesDirectory = Path.GetFullPath(resourcesDirectory);
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        _configure = configure;
        _restartPolicy = restartPolicy ?? new RustWorkerRestartPolicy();
    }

    public IReadOnlyList<ResourceInfo> Status => _running.Values.Select(x => x.Snapshot).OrderBy(x => x.Manifest.Name).ToArray();

    /// <summary>Raised when a Rust worker declares an inbound network event. The server must still authorize it.</summary>
    public event Action<string, string>? RustNetworkEventRegistered;
    /// <summary>Raised when a Rust worker registers a command.</summary>
    public event Action<string, string>? RustCommandRegistered;
    /// <summary>Raised when a Rust worker exits; observers are isolated from worker supervision.</summary>
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
            var running = new Running(new(manifest, dir, ResourceState.Starting), runtimes, rustWorkers);
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
                    AttachWorker(manifest, running, worker);
                    foreach (var command in worker.RegisteredCommands) Notify(RustCommandRegistered, manifest.Name, command);
                    foreach (var eventName in worker.RegisteredNetworkEvents) Notify(RustNetworkEventRegistered, manifest.Name, eventName);
                    if (!worker.IsRunning) HandleWorkerExit(manifest, running, worker, worker.ExitCode);
                }

                running.MarkStarted();
                _running[manifest.Name] = running;
            }
            catch (Exception ex)
            {
                running.BeginStopping();
                foreach (var runtime in runtimes.AsEnumerable().Reverse()) runtime.Dispose();
                _running[manifest.Name] = new(new(manifest, dir, ResourceState.Failed, ex.Message), [], []);
                throw new InvalidOperationException($"Resource '{manifest.Name}' failed to start.", ex);
            }
        }
    }

    private void AttachWorker(ResourceManifest manifest, Running running, RustWorkerProcessRuntime worker)
    {
        worker.CommandRegistered += command => Notify(RustCommandRegistered, manifest.Name, command);
        worker.NetworkEventRegistered += eventName => Notify(RustNetworkEventRegistered, manifest.Name, eventName);
        worker.WorkerExited += exitCode => HandleWorkerExit(manifest, running, worker, exitCode);
    }

    private void HandleWorkerExit(ResourceManifest manifest, Running running, RustWorkerProcessRuntime worker, int? exitCode)
    {
        if (!running.TryHandleExit(worker)) return;
        var restart = running.MarkWorkerExited(exitCode, _restartPolicy.MaxRestarts > 0);
        Notify(RustWorkerExited, manifest.Name, exitCode);
        if (!restart) return;

        var task = RestartWorkerAsync(manifest, running, worker);
        _restartTasks.TryAdd(task, 0);
        _ = task.ContinueWith(completed => _restartTasks.TryRemove(completed, out _),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RestartWorkerAsync(ResourceManifest manifest, Running running, RustWorkerProcessRuntime previous)
    {
        Exception? lastError = null;
        while (!running.IsStopping && !_shutdown.IsCancellationRequested)
        {
            var attempt = running.NextRestartAttempt(_restartPolicy.MaxRestarts);
            if (attempt == 0) break;

            try
            {
                await Task.Delay(_restartPolicy.GetDelay(attempt), _shutdown.Token);
                if (running.IsStopping) return;

                var replacement = await RustWorkerProcessRuntime.StartAsync(previous.ExecutablePath,
                    Path.Combine(_resourcesDirectory, manifest.Name), cancellationToken: _shutdown.Token);
                AttachWorker(manifest, running, replacement);

                if (!running.ReplaceWorker(previous, replacement))
                {
                    replacement.Dispose();
                    return;
                }
                previous.Dispose();

                foreach (var command in replacement.RegisteredCommands) Notify(RustCommandRegistered, manifest.Name, command);
                foreach (var eventName in replacement.RegisteredNetworkEvents) Notify(RustNetworkEventRegistered, manifest.Name, eventName);
                if (!replacement.IsRunning) HandleWorkerExit(manifest, running, replacement, replacement.ExitCode);
                return;
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (!running.IsStopping)
            running.SetRestartFailed($"Rust worker restart budget exhausted after {_restartPolicy.MaxRestarts} attempt(s). {lastError?.Message ?? "The worker exited and no retries remain."}");
    }

    private static void Notify<T1, T2>(Action<T1, T2>? handlers, T1 first, T2 second)
    {
        if (handlers is null) return;
        foreach (Action<T1, T2> handler in handlers.GetInvocationList())
        {
            try { handler(first, second); }
            catch { /* Host observers must not disrupt worker supervision. */ }
        }
    }

    public async ValueTask DispatchRustEventAsync(string eventName, object?[] arguments, CancellationToken cancellationToken = default)
    {
        foreach (var worker in _running.Values.SelectMany(x => x.WorkerSnapshot()).ToArray())
            await worker.DispatchEventAsync(eventName, arguments, cancellationToken);
    }

    /// <summary>Invokes the unique worker that declared the named command. A resource may host multiple workers.</summary>
    public ValueTask<string?> InvokeRustCommandAsync(string resourceName, string command, object?[] arguments, CancellationToken cancellationToken = default)
    {
        if (!_running.TryGetValue(resourceName, out var resource))
            throw new KeyNotFoundException($"No running Rust resource exists for '{resourceName}'.");

        var matches = resource.WorkerSnapshot()
            .Where(worker => worker.RegisteredCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
            throw new KeyNotFoundException($"Rust command '{command}' is not registered by resource '{resourceName}'.");
        if (matches.Length > 1)
            throw new InvalidOperationException($"Rust command '{command}' is registered by multiple workers in resource '{resourceName}'.");
        return matches[0].InvokeCommandAsync(command, arguments, cancellationToken);
    }

    /// <summary>Dispatches an approved client event only to workers in the owning resource that declared it.</summary>
    public async ValueTask DispatchRustEventToResourceAsync(string resourceName, string eventName, object?[] arguments, CancellationToken cancellationToken = default)
    {
        if (!_running.TryGetValue(resourceName, out var resource)) return;
        var targets = resource.WorkerSnapshot()
            .Where(worker => worker.RegisteredNetworkEvents.Contains(eventName, StringComparer.Ordinal))
            .ToArray();
        foreach (var worker in targets)
            await worker.DispatchEventAsync(eventName, arguments, cancellationToken);
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

    public async ValueTask StopAllAsync()
    {
        _shutdown.Cancel();
        foreach (var resource in _running.Values.Reverse()) resource.BeginStopping();
        var pending = _restartTasks.Keys.ToArray();
        if (pending.Length > 0)
        {
            try { await Task.WhenAll(pending); }
            catch { /* Restart failures are represented in resource status. */ }
        }

        foreach (var resource in _running.Values.Reverse())
        {
            foreach (var runtime in resource.Runtimes.AsEnumerable().Reverse()) runtime.Dispose();
            resource.MarkStopped();
        }
    }
}
