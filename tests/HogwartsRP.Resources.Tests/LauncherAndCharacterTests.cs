using HogwartsRP.GameIntegration.Abstractions;

namespace HogwartsRP.Resources.Tests;

public sealed class CharacterSubmissionTests
{
    private sealed class Repository : ICharacterRepository
    {
        public RegisteredCharacter? Saved { get; private set; }

        public ValueTask<RegisteredCharacter> RegisterAsync(
            RegisteredCharacter character,
            CancellationToken cancellationToken = default)
        {
            Saved = character;
            return ValueTask.FromResult(character);
        }
    }

    [Fact]
    public async Task CharacterOwnershipComesFromAuthenticatedSessionAndInvalidDataIsRejected()
    {
        var repository = new Repository();
        var service = new CharacterSubmissionService(repository);
        var result = await service.SubmitAsync(
            "session-player",
            new("Ada", "{}", "ravenclaw",
                new("oak", "phoenix", 11, "supple"),
                "Scholar",
                new("Healer", "A careful student.")));

        Assert.True(result.Success, result.Error);
        Assert.Equal("session-player", result.Character!.OwnerPlayerId);

        var invalid = await service.SubmitAsync(
            "session-player",
            new("", "[]", "NoHouse", null, null, null));
        Assert.False(invalid.Success);
    }
}
