using HogwartsRP.Core;
using HogwartsRP.Resources;
using HogwartsRP.Scripting;

namespace HogwartsRP.Resources.Tests;

public class LifecycleTests
{
    [Fact]
    public async Task LoadsAndStopsLocalResource()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var dir = Path.Combine(root, "sample"); Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "fxmanifest.lua"), "fx_version 'cerulean'\ngame 'hogwartslegacy'\nserver_script 'main.lua'");
            await File.WriteAllTextAsync(Path.Combine(dir, "main.lua"), "loaded = true");
            var manager = new ResourceManager(root, _ => new NativeLua54Runtime()); await manager.StartAllAsync();
            Assert.Equal(ResourceState.Started, Assert.Single(manager.Status).State); await manager.StopAllAsync(); Assert.Equal(ResourceState.Stopped, Assert.Single(manager.Status).State);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnexpectedRustWorkerExitMarksResourceFailed()
    {
        var workerPath = Environment.GetEnvironmentVariable("HOGWARTSRP_TEST_WORKER_PATH");
        Assert.False(string.IsNullOrWhiteSpace(workerPath), "CI must provide the compiled Rust test worker path.");
        Assert.True(File.Exists(workerPath));

        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var resourceDirectory = Path.Combine(root, "rust_sample");
        var binaryDirectory = Path.Combine(resourceDirectory, "bin");
        Directory.CreateDirectory(binaryDirectory);
        var executable = Path.Combine(binaryDirectory, OperatingSystem.IsWindows() ? "test-worker.exe" : "test-worker");
        File.Copy(workerPath!, executable);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(resourceDirectory, "fxmanifest.lua"),
                "fx_version 'cerulean'\ngame 'hogwartslegacy'\nrust_script 'bin/" + Path.GetFileName(executable) + "'");
            var manager = new ResourceManager(root, _ => new NativeLua54Runtime());
            var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.RustWorkerExited += (_, code) => exited.TrySetResult(code);

            await manager.StartAllAsync();
            Assert.Equal(ResourceState.Started, Assert.Single(manager.Status).State);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await manager.InvokeRustCommandAsync("rust_sample", "exit", [23]));

            Assert.Equal(23, await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            var status = Assert.Single(manager.Status);
            Assert.Equal(ResourceState.Failed, status.State);
            Assert.Contains("23", status.Error);
            await manager.StopAllAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }


    [Fact]
    public void RustWorkerRestartPolicyUsesCappedExponentialBackoff()
    {
        var policy = new RustWorkerRestartPolicy(4, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250));
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(250), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromMilliseconds(250), policy.GetDelay(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(5));
    }

    [Fact]
    public void RustWorkerRestartPolicyRejectsUnsafeRetryBudgets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RustWorkerRestartPolicy(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RustWorkerRestartPolicy(1, TimeSpan.FromSeconds(31)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RustWorkerRestartPolicy(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ManifestParserRecognizesPrecompiledRustWorkers()
    {
        const string manifest = "fx_version 'cerulean'\ngame 'hogwartslegacy'\nrust_script 'bin/rust_greeter.exe'\nrust_script 'bin/house_events.exe'";
        var parsed = ManifestParser.Parse(manifest, "sample");

        Assert.Equal(new[] { "bin/rust_greeter.exe", "bin/house_events.exe" }, parsed.RustScripts);
        Assert.Empty(parsed.ServerScripts);
    }
}
