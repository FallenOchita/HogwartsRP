using HogwartsRP.Launcher;

namespace HogwartsRP.Launcher.Tests;

public sealed class JsonServerProfileStoreTests
{
    [Fact]
    public void SaveAndLoad_PersistsProfilesInNameOrder()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonServerProfileStore(Path.Combine(directory.Path, "servers.json"));

        store.Save("Zulu RP", "https://zulu.example:7777");
        store.Save("Academy RP", "https://academy.example:7777");

        var loaded = store.Load();
        Assert.Equal(["Academy RP", "Zulu RP"], loaded.Select(profile => profile.Name));
    }

    [Fact]
    public void Save_UpdatesExistingAddressWithoutCreatingDuplicate()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonServerProfileStore(Path.Combine(directory.Path, "servers.json"));
        var first = store.Save("Old Name", "https://example.test:7777/");

        var updated = store.Save("New Name", "https://EXAMPLE.test:7777");

        var only = Assert.Single(store.Load());
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal("New Name", only.Name);
    }

    [Fact]
    public void Delete_RemovesOnlyMatchingProfile()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonServerProfileStore(Path.Combine(directory.Path, "servers.json"));
        var first = store.Save("First", "https://first.example");
        store.Save("Second", "https://second.example");

        Assert.True(store.Delete(first.Id));
        Assert.False(store.Delete(first.Id));
        Assert.Equal("Second", Assert.Single(store.Load()).Name);
    }

    [Theory]
    [InlineData("", "https://example.test")]
    [InlineData("Valid", "not a url")]
    [InlineData("Valid", "ftp://example.test")]
    public void Save_RejectsInvalidNameOrAddress(string name, string address)
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonServerProfileStore(Path.Combine(directory.Path, "servers.json"));

        Assert.Throws<ArgumentException>(() => store.Save(name, address));
    }

    [Fact]
    public void Load_RejectsCorruptJsonWithoutOverwritingIt()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "servers.json");
        File.WriteAllText(path, "{broken");
        var store = new JsonServerProfileStore(path);

        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
