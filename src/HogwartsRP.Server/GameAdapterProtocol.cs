namespace HogwartsRP.Server;

/// <summary>
/// Validation rules for the first authenticated game-adapter protocol slice.
/// Capability names are descriptive claims from the client, not server authorization.
/// </summary>
public static class GameAdapterProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumCapabilities = 16;

    public static bool TryValidateHello(
        int? protocolVersion,
        string? adapterVersion,
        string? gameBuild,
        string? ue4ssVersion,
        IReadOnlyCollection<string>? capabilities,
        out string? error)
    {
        error = null;
        if (protocolVersion != CurrentVersion)
            error = $"Unsupported game-adapter protocol version. Expected {CurrentVersion}.";
        else if (!IsVersionToken(adapterVersion, 32))
            error = "A valid adapter version is required.";
        else if (!IsOptionalToken(gameBuild, 64))
            error = "The game build identifier is invalid.";
        else if (!IsOptionalToken(ue4ssVersion, 32))
            error = "The UE4SS version identifier is invalid.";
        else if (capabilities is { Count: > MaximumCapabilities })
            error = $"At most {MaximumCapabilities} capability names may be reported.";
        else if (capabilities is not null &&
                 (capabilities.Any(value => !IsCapabilityToken(value)) ||
                  capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Count))
            error = "Capability names must be unique lowercase identifier tokens.";

        return error is null;
    }

    public static bool IsValidHeartbeatSequence(long? sequence, long previousSequence) =>
        sequence is >= 0 && sequence.Value > previousSequence;

    private static bool IsVersionToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool IsOptionalToken(string? value, int maximumLength) =>
        value is null ||
        (value.Length > 0 &&
         value.Length <= maximumLength &&
         value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+'));

    private static bool IsCapabilityToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 48 &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.');
}
