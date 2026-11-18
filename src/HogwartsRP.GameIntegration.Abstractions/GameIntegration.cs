namespace HogwartsRP.GameIntegration.Abstractions;

public sealed record IntegrationResult(bool Available, string? Message = null);
public interface IGameIntegration
{
    IntegrationResult GetPlayerCharacter(string playerId, out string? characterId);
    IntegrationResult GetPlayerHouse(string playerId, out string? house);
}
public interface ICharacterService
{
    ValueTask<IntegrationResult> CreateCharacterAsync(string playerId, string name, CancellationToken cancellationToken = default);
    ValueTask<IntegrationResult> SaveCharacterAsync(string playerId, CancellationToken cancellationToken = default);
}
