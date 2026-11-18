using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace HogwartsRP.Scripting;

/// <summary>Server-side proxy for one out-of-process Lua worker.</summary>
public sealed class LuaWorkerProcessRuntime : ILuaRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly WindowsWorkerJob? _windowsJob;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<LuaWorkerResponse>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _stdoutPump;
    private readonly Task _memoryMonitor;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _requestTimeout;
    private readonly long _maxPrivateMemoryBytes;
    private bool _disposed;

    public int WorkerProcessId => _process.Id;
    public event Action<string>? CommandRegistered;
    public event Action<LuaWorkerProcessRuntime, string, object?[]>? EventRaised;
    public event Action<string>? WorkerOutput;

    public LuaWorkerProcessRuntime(string workerAssemblyPath, TimeSpan? requestTimeout = null, long maxPrivateMemoryBytes = 512L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerAssemblyPath);
        if (!File.Exists(workerAssemblyPath)) throw new FileNotFoundException("Lua worker assembly was not found.", workerAssemblyPath);
        if (maxPrivateMemoryBytes < 64L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxPrivateMemoryBytes));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _maxPrivateMemoryBytes = maxPrivateMemoryBytes;
        _windowsJob = OperatingSystem.IsWindows() ? WindowsWorkerJob.Create(maxPrivateMemoryBytes) : null;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        start.ArgumentList.Add(workerAssemblyPath);
        start.ArgumentList.Add("--worker");
        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            WorkerOutput?.Invoke(e.Data);
            Console.Error.WriteLine($"[LuaWorker:{_process.Id}] {e.Data}");
        };
        try
        {
            if (!_process.Start()) throw new InvalidOperationException("Unable to start Lua worker process.");
            _windowsJob?.Assign(_process);
        }
        catch
        {
            TerminateWorker();
            _windowsJob?.Dispose();
            _process.Dispose();
            throw;
        }
        _process.BeginErrorReadLine();
        _stdoutPump = PumpOutputAsync();
        _memoryMonitor = MonitorMemoryAsync(_lifetime.Token);
        try { _ready.Task.WaitAsync(_requestTimeout).GetAwaiter().GetResult(); }
        catch { TerminateWorker(); throw; }
    }

    public void Register(string name, Delegate callback) => throw new NotSupportedException("Register APIs inside the worker runtime, not in the server process.");
    public void CallFunction(object callback, params object?[] arguments) => throw new NotSupportedException("Lua callbacks are owned by the worker process.");
    public void Track(IDisposable subscription) => throw new NotSupportedException("Lua subscriptions are owned by the worker process.");

    public void Execute(string source, string chunkName) => RequestAsync(new("execute", Guid.NewGuid().ToString("N"), source, chunkName)).GetAwaiter().GetResult();

    public async ValueTask<string?> InvokeCommandAsync(string command, string[] arguments, CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(new("invoke_command", Guid.NewGuid().ToString("N"), Name: command, Arguments: arguments), cancellationToken);
        return response.Result;
    }

    public async ValueTask DispatchEventAsync(string eventName, object?[] arguments, CancellationToken cancellationToken = default)
    {
        await RequestAsync(new("dispatch_event", Guid.NewGuid().ToString("N"), EventName: eventName, EventArguments: arguments), cancellationToken);
    }

    private async Task<LuaWorkerResponse> RequestAsync(LuaWorkerRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion = new TaskCompletionSource<LuaWorkerResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.Id!, completion)) throw new InvalidOperationException("Duplicate worker request ID.");
        try
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            finally { _writeLock.Release(); }

            LuaWorkerResponse response;
            try { response = await completion.Task.WaitAsync(_requestTimeout, cancellationToken); }
            catch (TimeoutException)
            {
                TerminateWorker();
                FailPending(new TimeoutException($"Lua worker exceeded its {_requestTimeout.TotalSeconds:0.#} second request limit."));
                throw;
            }
            if (response.Type == "request_error") throw new InvalidOperationException(response.Error ?? "Lua worker request failed.");
            if (response.Type == "protocol_error") throw new InvalidOperationException(response.Error ?? "Lua worker protocol error.");
            return response;
        }
        finally { _pending.TryRemove(request.Id!, out _); }
    }

    private async Task PumpOutputAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
            {
                LuaWorkerResponse? response;
                try { response = JsonSerializer.Deserialize<LuaWorkerResponse>(line, JsonOptions); }
                catch (JsonException ex) { FailPending(new InvalidDataException("Worker emitted invalid protocol output.", ex)); continue; }
                if (response is null) continue;
                if (response.Type == "ready") { _ready.TrySetResult(); continue; }
                if (response.Type == "command_registered" && response.Name is not null) { CommandRegistered?.Invoke(response.Name); continue; }
                if (response.Type == "event_raised" && response.EventName is not null)
                {
                    try { EventRaised?.Invoke(this, response.EventName, (response.EventArguments ?? []).Select(ConvertJsonValue).ToArray()); }
                    catch (Exception ex) { Console.Error.WriteLine($"[LuaWorker:{_process.Id}] Event relay failed: {ex.Message}"); }
                    continue;
                }
                if (response.Id is not null && _pending.TryGetValue(response.Id, out var completion)) completion.TrySetResult(response);
            }
        }
        catch (Exception ex) { FailPending(ex); }
        finally
        {
            if (!_ready.Task.IsCompleted) _ready.TrySetException(new InvalidOperationException("Lua worker exited before becoming ready."));
            FailPending(new InvalidOperationException($"Lua worker process exited with code {_process.ExitCode}."));
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values) completion.TrySetException(exception);
    }

    private static object? ConvertJsonValue(object? value)
    {
        if (value is not JsonElement element) return value;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.Null => null,
            _ => throw new InvalidDataException("Worker event arguments must be primitive JSON values.")
        };
    }

    private async Task MonitorMemoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
                if (_process.HasExited) return;
                if (_process.PrivateMemorySize64 <= _maxPrivateMemoryBytes) continue;
                var ex = new InvalidOperationException($"Lua worker exceeded its {_maxPrivateMemoryBytes / (1024 * 1024)} MiB private-memory limit.");
                FailPending(ex);
                TerminateWorker();
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private void TerminateWorker()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        try
        {
            _process.StandardInput.WriteLine(JsonSerializer.Serialize(new LuaWorkerRequest("stop"), JsonOptions));
            _process.StandardInput.Flush();
            if (!_process.WaitForExit(2_000)) TerminateWorker();
        }
        catch { TerminateWorker(); }
        try { _stdoutPump.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _memoryMonitor.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _process.Dispose();
        _windowsJob?.Dispose();
        _lifetime.Dispose();
        _writeLock.Dispose();
    }
}
