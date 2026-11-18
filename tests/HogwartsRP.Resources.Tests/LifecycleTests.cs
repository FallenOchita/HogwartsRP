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
}
