using HogwartsRP.Core;
using HogwartsRP.Scripting;

namespace HogwartsRP.Resources;

public sealed class ResourceManager(string resourcesDirectory, Func<string, ILuaRuntime> runtimeFactory, Action<ILuaRuntime, string>? configure = null)
{
    private sealed class Running(ResourceInfo info, List<ILuaRuntime> runtimes) { public ResourceInfo Info = info; public List<ILuaRuntime> Runtimes = runtimes; }
    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ResourceInfo> Status => _running.Values.Select(x => x.Info).OrderBy(x => x.Manifest.Name).ToArray();

    public IReadOnlyList<ResourceManifest> Discover()
    {
        Directory.CreateDirectory(resourcesDirectory);
        return Directory.GetDirectories(resourcesDirectory).Order().Select(dir =>
        {
            var path = Path.Combine(dir, "fxmanifest.lua");
            if (!File.Exists(path)) throw new FileNotFoundException("Resource manifest is missing.", path);
            return ManifestParser.Parse(File.ReadAllText(path), Path.GetFileName(dir));
        }).ToArray();
    }

    public async ValueTask StartAllAsync()
    {
        foreach (var manifest in DependencyResolver.Sort(Discover()))
        {
            var dir = Path.Combine(resourcesDirectory, manifest.Name);
            var runtimes = new List<ILuaRuntime>();
            var info = new ResourceInfo(manifest, dir, ResourceState.Starting);
            try
            {
                var scripts = manifest.ServerScripts.Concat(manifest.SharedScripts).ToArray();
                if (scripts.Length > 0)
                {
                    var runtime = runtimeFactory(manifest.Name);
                    runtimes.Add(runtime);
                    configure?.Invoke(runtime, manifest.Name);
                    foreach (var script in scripts)
                    {
                        var fullPath = Path.GetFullPath(Path.Combine(dir, script));
                        if (!fullPath.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Script path escapes the resource directory.");
                        runtime.Execute(await File.ReadAllTextAsync(fullPath), script);
                    }
                }
                _running[manifest.Name] = new(new(manifest, dir, ResourceState.Started), runtimes);
            }
            catch (Exception ex)
            {
                foreach (var runtime in runtimes) runtime.Dispose();
                _running[manifest.Name] = new(new(manifest, dir, ResourceState.Failed, ex.Message), []);
                throw new InvalidOperationException($"Resource '{manifest.Name}' failed to start.", ex);
            }
        }
    }

    public ValueTask StopAllAsync()
    {
        foreach (var resource in _running.Values.Reverse())
        { resource.Info = resource.Info with { State = ResourceState.Stopping }; foreach (var runtime in resource.Runtimes) runtime.Dispose(); resource.Info = resource.Info with { State = ResourceState.Stopped }; }
        return ValueTask.CompletedTask;
    }
}
