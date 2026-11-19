using HogwartsRP.Scripting;

namespace HogwartsRP.Scripting.Tests;

public sealed class RustWorkerProcessRuntimeTests
{
    [Fact]
    public async Task CompiledWorker_RegistersAndDispatchesCommandsAndEvents()
    {
        var executable = GetWorkerPath();
        using var worker = await RustWorkerProcessRuntime.StartAsync(executable, Path.GetDirectoryName(executable)!);

        Assert.Contains("hello", worker.RegisteredCommands);
        Assert.Contains("player:joined", worker.RegisteredNetworkEvents);
        Assert.True(worker.IsRunning);

        var result = await worker.InvokeCommandAsync("hello", ["Merlin"]);
        Assert.Equal("Hello, Merlin!", result);

        var eventReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.WorkerOutput += line => eventReceived.TrySetResult(line);
        worker.WorkerOutput += _ => throw new InvalidOperationException("Observer failure must be isolated.");
        await worker.DispatchEventAsync("player:joined", ["account-123"]);
        var output = await eventReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("player:joined received", output);
        Assert.True(worker.IsRunning);
    }

    [Fact]
    public async Task UnexpectedWorkerExit_IsReportedAndPendingRequestFails()
    {
        var executable = GetWorkerPath();
        using var worker = await RustWorkerProcessRuntime.StartAsync(executable, Path.GetDirectoryName(executable)!);
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.WorkerExited += code => exited.TrySetResult(code);

        await Assert.ThrowsAnyAsync<Exception>(async () => await worker.InvokeCommandAsync("exit", [23]));
        Assert.Equal(23, await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(worker.IsRunning);
    }

    private static string GetWorkerPath()
    {
        var path = Environment.GetEnvironmentVariable("HOGWARTSRP_TEST_WORKER_PATH");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("CI must set HOGWARTSRP_TEST_WORKER_PATH after building the Rust integration-test worker.");
        if (!File.Exists(path)) throw new FileNotFoundException("Rust integration-test worker was not built.", path);
        return Path.GetFullPath(path);
    }
}
