using System.Text.RegularExpressions;
using HogwartsRP.GameIntegration.Abstractions;

namespace HogwartsRP.Launcher;

/// <summary>
/// Client-side draft model for the roleplay character form. The server remains authoritative
/// and repeats all validation before accepting a submission.
/// </summary>
public sealed class CharacterCreationDraft
{
    private static readonly HashSet<string> Houses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Gryffindor", "Hufflepuff", "Ravenclaw", "Slytherin"
    };

    public string Name { get; set; } = string.Empty;
    public string House { get; set; } = string.Empty;
    public string? Background { get; set; }
    public string? Biography { get; set; }
    public string? WandWood { get; set; }
    public string? WandCore { get; set; }
    public decimal? WandLength { get; set; }
    public string? WandFlexibility { get; set; }

    public bool TryBuildSubmission(out CharacterSubmission? submission, out IReadOnlyList<string> errors)
    {
        var validationErrors = new List<string>();
        var name = Name.Trim();
        if (name.Length is < 1 or > 32 || !Regex.IsMatch(name, @"^[\p{L}\p{M} '’-]+$"))
            validationErrors.Add("Name must be 1–32 letters, spaces, apostrophes, or hyphens.");

        var house = Houses.FirstOrDefault(candidate => string.Equals(candidate, House.Trim(), StringComparison.OrdinalIgnoreCase));
        if (house is null)
            validationErrors.Add("Choose Gryffindor, Hufflepuff, Ravenclaw, or Slytherin.");

        var background = Background?.Trim();
        var biography = Biography?.Trim();
        if (background?.Length > 2000)
            validationErrors.Add("Background must be 2,000 characters or fewer.");
        if (biography?.Length > 4000)
            validationErrors.Add("Biography must be 4,000 characters or fewer.");

        var hasWandDetails = !string.IsNullOrWhiteSpace(WandWood)
            || !string.IsNullOrWhiteSpace(WandCore)
            || WandLength.HasValue
            || !string.IsNullOrWhiteSpace(WandFlexibility);
        if (WandLength is < 4 or > 16)
            validationErrors.Add("Wand length must be between 4 and 16 inches.");

        if (validationErrors.Count > 0)
        {
            submission = null;
            errors = validationErrors;
            return false;
        }

        var wand = hasWandDetails
            ? new WandData(EmptyToNull(WandWood), EmptyToNull(WandCore), WandLength, EmptyToNull(WandFlexibility))
            : null;
        var roleplay = background is not null || biography is not null
            ? new RoleplayProfile(background, biography)
            : null;

        submission = new CharacterSubmission(name, null, house!, wand, background, roleplay);
        errors = Array.Empty<string>();
        return true;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
