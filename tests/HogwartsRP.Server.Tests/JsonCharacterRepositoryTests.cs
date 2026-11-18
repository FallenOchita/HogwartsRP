using HogwartsRP.GameIntegration.Abstractions;
using HogwartsRP.Server;

namespace HogwartsRP.Server.Tests;

public sealed class JsonCharacterRepositoryTests
{
    [Fact]
    public async Task CharactersPersistAcrossRepositoryInstancesAndAreFilteredByOwner()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "data", "characters.json");
            var repository = new JsonCharacterRepository(path);
            var owned = Character(Guid.NewGuid(), "player-one", "Ada");
            var other = Character(Guid.NewGuid(), "player-two", "Rowan");

            await repository.RegisterAsync(owned);
            await repository.RegisterAsync(other);

            var restartedRepository = new JsonCharacterRepository(path);
            var results = await restartedRepository.GetByOwnerAsync("player-one");

            var character = Assert.Single(results);
            Assert.Equal(owned.Id, character.Id);
            Assert.Equal("Ada", character.Name);
            Assert.Equal("player-one", character.OwnerPlayerId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateCharacterIdsAreRejectedWithoutReplacingExistingRecord()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "characters.json");
            var repository = new JsonCharacterRepository(path);
            var id = Guid.NewGuid();
            await repository.RegisterAsync(Character(id, "player-one", "Ada"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await repository.RegisterAsync(Character(id, "player-two", "Different")));

            Assert.Contains("already registered", exception.Message);
            var stored = Assert.Single(await new JsonCharacterRepository(path).GetByOwnerAsync("player-one"));
            Assert.Equal("Ada", stored.Name);
            Assert.Empty(await new JsonCharacterRepository(path).GetByOwnerAsync("player-two"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptStoreIsNotSilentlyOverwritten()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "characters.json");
            const string corrupt = "{ not valid json";
            await File.WriteAllTextAsync(path, corrupt);
            var repository = new JsonCharacterRepository(path);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await repository.GetByOwnerAsync("player-one"));

            Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RegisteredCharacter Character(Guid id, string owner, string name) =>
        new(id, owner, name, "{\"hair\":\"brown\"}", "Ravenclaw",
            new WandData("Oak", "Phoenix feather", 11, "Supple"),
            "Scholar", new RoleplayProfile("Student", "A curious student."));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HogwartsRP-Characters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
