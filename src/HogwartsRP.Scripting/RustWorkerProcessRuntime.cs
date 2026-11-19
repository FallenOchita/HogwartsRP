using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HogwartsRP.Scripting;

/// <summary>
/// Host-side supervisor for one compiled Rust resource worker.
/// Resource binaries are trusted code and run with the server account's OS permissions.
/// </summary>
public sealed class RustWorkerProcessRuntime : IDisposable
{
    private const int MaxFrameBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Process _process;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WorkerResponse>> _pending = new();
    private readonly ConcurrentDictionary<string, byte> _registeredCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _registeredNetworkEvents = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private readonly TimeSpan _requestTimeout;
    private bool _disposed;

    private sealed record WorkerRequest(string Type, string? Id = null, string? Name = null, string? EventName = null, object?[]? Arguments = null);
    private sealed record WorkerResponse(string Type, string? Id = null, string? Name = null, string? EventName = null, JsonElement[]? Arguments = null, string? Error = null);

    public string ExecutablePath { get; }
    public int ProcessId => _process.Id;
    public bool IsRunning { get { try { return !_process.HasExited; } catch (InvalidOperationException) { return false; } } }
    public int? ExitCode { get { try { return _process.HasExited ? _process.ExitCode : null; } catch (InvalidOperationException) { return null; } } }
    public IReadOnlyCollection<string> RegisteredCommands => _registeredCommands.Keys.ToArray();
    public IReadOnlyCollection<string> RegisteredNetworkEvents => _registeredNetworkEvents.Keys.ToArray();
    public event Action<string>? CommandRegistered;
    public event Action<string>? NetworkEventRegistered;
    public event Action<string>? WorkerOutput;
    public event Action<int?>? WorkerExited;

    private RustWorkerProcessRuntime(Process process, string executablePath, TimeSpan requestTimeout)
    {
        _process = process;
        ExecutablePath = executablePath;
        _requestTimeout = requestTimeout;
        _stdoutPump = PumpOutputAsync();
        _stderrPump = PumpErrorAsync();
    }

    public static async Task<RustWorkerProcessRuntime> StartAsync(
        string executablePath,
        string workingDirectory,
        TimeSpan? startupTimeout = null,
        TimeSpan? requestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var executable = Path.GetFullPath(executablePath);
        var directory = Path.GetFullPath(workingDirectory);
        if (!File.Exists(executable)) throw new FileNotFoundException("Compiled Rust resource executable was not found.", executable);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Unable to start Rust resource worker.");
        }

        var worker = new RustWorkerProcessRuntime(process, executable, requestTimeout ?? TimeSpan.FromSeconds(15));
        try
        {
            await worker._ready.Task.WaitAsync(startupTimeout ?? TimeSpan.FromSeconds(10), cancellationToken);
            return worker;
        }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    public async ValueTask<string?> InvokeCommandAsync(string command, object?[] arguments, CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(new("invoke_command", Guid.NewGuid().ToString("N"), Name: command, Arguments: arguments), cancellationToken);
        if (response.Arguments is not { Length: > 0 } values) return null;
        return values[0].ValueKind == JsonValueKind.String ? values[0].GetString() : values[0].GetRawText();
    }

    public async ValueTask DispatchEventAsync(string eventName, object?[] arguments, CancellationToken cancellationToken = default)
    {
        await RequestAsync(new("dispatch_event", Guid.NewGuid().ToString("N"), EventName: eventName, Arguments: arguments), cancellationToken);
    }

    private async Task<WorkerResponse> RequestAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = request.Id ?? throw new InvalidOperationException("Worker request ID is required.");
        var completion = new TaskCompletionSource<WorkerResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate worker request ID.");
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > MaxFrameBytes) throw new InvalidDataException("Rust worker request exceeds the 32 KiB protocol limit.");
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _process.StandardInput.WriteLineAsync(json);
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            finally { _writeLock.Release(); }

            WorkerResponse response;
            try { response = await completion.Task.WaitAsync(_requestTimeout, cancellationToken); }
            catch (TimeoutException)
            {
                Terminate();
                FailPending(new TimeoutException($"Rust worker exceeded its {_requestTimeout.TotalSeconds:0.#} second request limit."));
                throw;
            }
            if (response.Type == "request_error") throw new InvalidOperationException(response.Error ?? "Rust resource request failed.");
            if (response.Type != "request_result") throw new InvalidDataException($"Unexpected Rust worker response type: {response.Type}.");
            return response;
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task PumpOutputAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync(_lifetime.Token) is { } line)
            {
                if (Encoding.UTF8.GetByteCount(line) > MaxFrameBytes)
                {
                    FailPending(new InvalidDataException("Rust worker emitted a protocol frame larger than 32 KiB."));
                    Terminate();
                    return;
                }

                WorkerResponse? response;
                try { response = JsonSerializer.Deserialize<WorkerResponse>(line, JsonOptions); }
                catch (JsonException ex)
                {
                    FailPending(new InvalidDataException("Rust worker emitted invalid JSON protocol output.", ex));
                    Terminate();
                    return;
                }
                if (response is null) continue;
                if (response.Type == "ready") { _ready.TrySetResult(); continue; }
                if (response.Type == "command_registered" && response.Name is not null)
                {
                    _registeredCommands.TryAdd(response.Name, 0);
                    Notify(CommandRegistered, response.Name);
                    continue;
                }
                if (response.Type == "network_event_registered" && response.Name is not null)
                {
                    _registeredNetworkEvents.TryAdd(response.Name, 0);
                    Notify(NetworkEventRegistered, response.Name);
                    continue;
                }
                if (response.Id is not null && _pending.TryGetValue(response.Id, out var completion))
                    completion.TrySetResult(response);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { FailPending(ex); }
        finally
        {
            if (!_ready.Task.IsCompleted)
                _ready.TrySetException(new InvalidOperationException("Rust resource exited before its ready message."));

            // stdout can reach EOF just before the process handle reports termination.
            // Wait for the process itself before reading ExitCode or notifying observers.
            try { await _process.WaitForExitAsync(); }
            catch (InvalidOperationException) { }

            var exitCode = -1;
            try { if (_process.HasExited) exitCode = _process.ExitCode; } catch (InvalidOperationException) { }
            FailPending(new InvalidOperationException($"Rust resource exited (code {exitCode})."));
            Notify(WorkerExited, exitCode < 0 ? null : exitCode);
        }
    }

    private async Task PumpErrorAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync(_lifetime.Token) is { } line)
                Notify(WorkerOutput, line);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static void Notify<T>(Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try { handler(value); }
            catch { /* A host observer must not be able to break the worker protocol pump. */ }
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var pending in _pending.Values) pending.TrySetException(exception);
    }

    private void Terminate()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            var stop = JsonSerializer.Serialize(new WorkerRequest("stop"), JsonOptions);
            _process.StandardInput.WriteLine(stop);
            _process.StandardInput.Flush();
            if (!_process.WaitForExit(2_000)) Terminate();
        }
        catch { Terminate(); }
        _lifetime.Cancel();
        try { _stdoutPump.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _stderrPump.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _process.Dispose();
        _writeLock.Dispose();
        _lifetime.Dispose();
    }
}
