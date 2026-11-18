using HogwartsRP.Core;

namespace HogwartsRP.Core.Tests;

public class FrameworkTests
{
    [Fact]
    public void ParsesManifestAndDependencies()
    {
        var m = ManifestParser.Parse("fx_version 'cerulean'\ngame 'hogwartslegacy'\nserver_script 'server.lua'\ndependency 'base'", "demo");
        Assert.Equal("demo", m.Name); Assert.Equal("server.lua", Assert.Single(m.ServerScripts)); Assert.Equal("base", Assert.Single(m.Dependencies));
    }
    [Fact]
    public void SortsAndRejectsCircularDependencies()
    {
        ResourceManifest A(string n, params string[] d) => new(n, "cerulean", "game", null, null, null, [], [], [], d);
        Assert.Equal(new[] { "a", "b" }, DependencyResolver.Sort([A("b", "a"), A("a")]).Select(x => x.Name));
        Assert.Throws<InvalidOperationException>(() => DependencyResolver.Sort([A("a", "b"), A("b", "a")]));
    }
    [Fact]
    public async Task PermissionChecksDenyByDefault()
    {
        var registry = new CommandRegistry(new PermissionService()); registry.Register("admin", "admin", (_, _) => ValueTask.FromResult<string?>(null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await registry.ExecuteAsync(new("user", new HashSet<string>()), "admin", []));
    }
    [Fact]
    public async Task EventSubscriptionCanBeDisposed()
    {
        var bus = new EventBus(); var count = 0; using (bus.Subscribe("ping", _ => { count++; return ValueTask.CompletedTask; })) await bus.PublishAsync("ping");
        await bus.PublishAsync("ping"); Assert.Equal(1, count);
    }
}
