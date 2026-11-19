using HogwartsRP.Server.Accounts;

namespace HogwartsRP.Server.Tests;

public sealed class JsonAccountIdentityStoreTests
{
    [Fact]
    public async Task AccountsAndLinksPersistAcrossStoreInstances()
    {
        var path = NewStorePath();
        try
        {
            var first = new JsonAccountIdentityStore(path);
            var account = await first.CreateAccountAsync("  Rowan  ");
            await first.LinkVerifiedIdentityAsync(account.Id, ExternalIdentityProvider.Discord, "discord-123", "Rowan");

            var reopened = new JsonAccountIdentityStore(path);
            var links = await reopened.GetLinksAsync(account.Id);

            Assert.Single(links);
            Assert.Equal(ExternalIdentityProvider.Discord, links[0].Provider);
            Assert.Equal("discord-123", links[0].ProviderSubject);
            Assert.Equal("Rowan", links[0].ProviderDisplayName);
        }
        finally { DeleteStore(path); }
    }

    [Fact]
    public async Task SameProviderIdentityCannotBeClaimedByAnotherAccount()
    {
        var path = NewStorePath();
        try
        {
            var store = new JsonAccountIdentityStore(path);
            var first = await store.CreateAccountAsync("First");
            var second = await store.CreateAccountAsync("Second");
            await store.LinkVerifiedIdentityAsync(first.Id, ExternalIdentityProvider.Steam, "76561198000000000");

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.LinkVerifiedIdentityAsync(second.Id, ExternalIdentityProvider.Steam, "76561198000000000"));
            Assert.Empty(await store.GetLinksAsync(second.Id));
        }
        finally { DeleteStore(path); }
    }

    [Fact]
    public async Task AccountCanHaveOnlyOneIdentityPerProvider()
    {
        var path = NewStorePath();
        try
        {
            var store = new JsonAccountIdentityStore(path);
            var account = await store.CreateAccountAsync("Player");
            await store.LinkVerifiedIdentityAsync(account.Id, ExternalIdentityProvider.EpicGames, "epic-a");

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.LinkVerifiedIdentityAsync(account.Id, ExternalIdentityProvider.EpicGames, "epic-b"));
        }
        finally { DeleteStore(path); }
    }

    [Fact]
    public async Task RepeatingSameLinkForSameAccountIsIdempotent()
    {
        var path = NewStorePath();
        try
        {
            var store = new JsonAccountIdentityStore(path);
            var account = await store.CreateAccountAsync("Player");
            var first = await store.LinkVerifiedIdentityAsync(account.Id, ExternalIdentityProvider.Discord, "discord-a");
            var second = await store.LinkVerifiedIdentityAsync(account.Id, ExternalIdentityProvider.Discord, "discord-a");

            Assert.Equal(first, second);
            Assert.Single(await store.GetLinksAsync(account.Id));
        }
        finally { DeleteStore(path); }
    }

    [Fact]
    public async Task UnlinkRemovesOnlyTheRequestedAccountsProviderLink()
    {
        var path = NewStorePath();
        try
        {
            var store = new JsonAccountIdentityStore(path);
            var first = await store.CreateAccountAsync("First");
            var second = await store.CreateAccountAsync("Second");
            await store.LinkVerifiedIdentityAsync(first.Id, ExternalIdentityProvider.Discord, "discord-first");
            await store.LinkVerifiedIdentityAsync(second.Id, ExternalIdentityProvider.Discord, "discord-second");

            Assert.True(await store.UnlinkAsync(first.Id, ExternalIdentityProvider.Discord));
            Assert.Empty(await store.GetLinksAsync(first.Id));
            Assert.Single(await store.GetLinksAsync(second.Id));
            Assert.False(await store.UnlinkAsync(first.Id, ExternalIdentityProvider.Discord));
        }
        finally { DeleteStore(path); }
    }

    [Fact]
    public async Task UnknownAccountCannotReceiveExternalLink()
    {
        var path = NewStorePath();
        try
        {
            var store = new JsonAccountIdentityStore(path);
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await store.LinkVerifiedIdentityAsync(Guid.NewGuid(), ExternalIdentityProvider.Discord, "discord-id"));
        }
        finally { DeleteStore(path); }
    }

    [Fact]
    public async Task InvalidProviderSubjectsAreRejected()
    {
        var path = NewStorePath();
        try
        {
            var store = new JsonAccountIdentityStore(path);
            var account = await store.CreateAccountAsync("Player");
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.LinkVerifiedIdentityAsync(account.Id, ExternalIdentityProvider.Discord, "  "));
        }
        finally { DeleteStore(path); }
    }

    [Fact]
    public async Task CorruptStoreIsNotSilentlyOverwritten()
    {
        var path = NewStorePath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{not-json");
            var store = new JsonAccountIdentityStore(path);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.CreateAccountAsync("Player"));
            Assert.Equal("{not-json", await File.ReadAllTextAsync(path));
        }
        finally { DeleteStore(path); }
    }

    private static string NewStorePath() =>
        Path.Combine(Path.GetTempPath(), "HogwartsRP.Tests", Guid.NewGuid().ToString("N"), "accounts.json");

    private static void DeleteStore(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
