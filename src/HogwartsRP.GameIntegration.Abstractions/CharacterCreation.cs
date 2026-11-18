using System.Text.Json;
using System.Text.RegularExpressions;

namespace HogwartsRP.GameIntegration.Abstractions;

public sealed record WandData(string? Wood, string? Core, decimal? Length, string? Flexibility);
public sealed record RoleplayProfile(string? Background, string? Biography);
public sealed record CharacterSubmission(string Name, string? AppearanceJson, string House, WandData? Wand, string? Background, RoleplayProfile? RoleplayProfile);
public sealed record RegisteredCharacter(Guid Id, string OwnerPlayerId, string Name, string? AppearanceJson, string House, WandData? Wand, string? Background, RoleplayProfile? RoleplayProfile);
public sealed record CharacterSubmissionResult(bool Success, RegisteredCharacter? Character = null, string? Error = null);

public interface ICharacterCreationIntegration
{
    ValueTask<bool> OpenAsync(string playerId, CancellationToken cancellationToken = default);
}
public interface ICharacterRepository
{
    ValueTask<RegisteredCharacter> RegisterAsync(RegisteredCharacter character, CancellationToken cancellationToken = default);
}
public interface ICharacterSubmissionService
{
    ValueTask<CharacterSubmissionResult> SubmitAsync(string authenticatedPlayerId, CharacterSubmission submission, CancellationToken cancellationToken = default);
}

/// <summary>Server-side validation. Owner is always derived from the authenticated session, never from submitted JSON.</summary>
public sealed class CharacterSubmissionService(ICharacterRepository repository) : ICharacterSubmissionService
{
    private static readonly HashSet<string> Houses = new(StringComparer.OrdinalIgnoreCase) { "Gryffindor", "Hufflepuff", "Ravenclaw", "Slytherin" };
    public async ValueTask<CharacterSubmissionResult> SubmitAsync(string authenticatedPlayerId, CharacterSubmission submission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authenticatedPlayerId) || authenticatedPlayerId.Length > 128) return new(false, Error: "Invalid authenticated player session.");
        var name = submission.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 32 || !Regex.IsMatch(name, @"^[\p{L}\p{M} '’-]+$")) return new(false, Error: "Character name must be 1–32 letters, spaces, apostrophes, or hyphens.");
        var house = Houses.FirstOrDefault(h => string.Equals(h, submission.House, StringComparison.OrdinalIgnoreCase));
        if (house is null) return new(false, Error: "House must be one of the four Hogwarts houses.");
        if (submission.AppearanceJson is { Length: > 8192 }) return new(false, Error: "Appearance data exceeds 8 KB.");
        if (submission.AppearanceJson is { } json) try { using var parsed = JsonDocument.Parse(json); if (parsed.RootElement.ValueKind != JsonValueKind.Object) return new(false, Error: "Appearance data must be a JSON object."); } catch (JsonException) { return new(false, Error: "Appearance data is invalid JSON."); }
        if (submission.Background?.Length > 2000 || submission.RoleplayProfile?.Biography?.Length > 4000 || submission.RoleplayProfile?.Background?.Length > 2000) return new(false, Error: "Roleplay text exceeds the allowed length.");
        if (submission.Wand?.Length is < 4 or > 16) return new(false, Error: "Wand length must be between 4 and 16 inches.");
        var candidate = new RegisteredCharacter(Guid.NewGuid(), authenticatedPlayerId, name, submission.AppearanceJson, house, submission.Wand, submission.Background?.Trim(), submission.RoleplayProfile);
        try { return new(true, await repository.RegisterAsync(candidate, cancellationToken)); }
        catch (InvalidOperationException ex) { return new(false, Error: ex.Message); }
    }
}
