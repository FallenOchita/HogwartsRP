using System.Globalization;
using HogwartsRP.GameIntegration.Abstractions;

namespace HogwartsRP.Launcher;

/// <summary>
/// Small console interaction layer for the preview launcher. It collects RP metadata and
/// submits it through the caller-provided authenticated server connection.
/// </summary>
public sealed class CharacterCreationConsoleFlow(
    TextReader input,
    TextWriter output,
    Func<CharacterSubmission, CancellationToken, ValueTask<CharacterSubmissionResult>> submit)
{
    public async ValueTask<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(submit);

        await output.WriteLineAsync("Create a HogwartsRP roleplay character now? (y/N)");
        if (!IsYes(await input.ReadLineAsync(cancellationToken))) return false;

        var draft = new CharacterCreationDraft
        {
            Name = await ReadRequiredAsync("Character name: ", cancellationToken),
            House = await ReadRequiredAsync("House (Gryffindor/Hufflepuff/Ravenclaw/Slytherin): ", cancellationToken),
            Background = await ReadOptionalAsync("Background (optional): ", cancellationToken),
            Biography = await ReadOptionalAsync("Biography (optional): ", cancellationToken),
            WandWood = await ReadOptionalAsync("Wand wood (optional): ", cancellationToken),
            WandCore = await ReadOptionalAsync("Wand core (optional): ", cancellationToken),
            WandFlexibility = await ReadOptionalAsync("Wand flexibility (optional): ", cancellationToken)
        };

        var wandLength = await ReadOptionalAsync("Wand length in inches (optional, 4–16): ", cancellationToken);
        if (!string.IsNullOrWhiteSpace(wandLength))
        {
            if (decimal.TryParse(wandLength, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedLength))
                draft.WandLength = parsedLength;
            else
                draft.WandLength = -1m;
        }

        if (!draft.TryBuildSubmission(out var submission, out var errors))
        {
            await output.WriteLineAsync("Character was not submitted:");
            foreach (var error in errors) await output.WriteLineAsync($"- {error}");
            return false;
        }

        try
        {
            var result = await submit(submission!, cancellationToken);
            if (!result.Success)
            {
                await output.WriteLineAsync($"The server rejected the character: {result.Error ?? "No reason was provided."}");
                return false;
            }

            await output.WriteLineAsync($"Character registered: {result.Character?.Name ?? submission!.Name} (ID: {result.Character?.Id.ToString() ?? "unavailable"}).");
            await output.WriteLineAsync("This stores roleplay data on the server; it does not create a character inside Hogwarts Legacy.");
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await output.WriteLineAsync($"Character submission failed: {exception.Message}");
            return false;
        }
    }

    private async ValueTask<string> ReadRequiredAsync(string prompt, CancellationToken cancellationToken)
    {
        await output.WriteAsync(prompt);
        return (await input.ReadLineAsync(cancellationToken) ?? string.Empty).Trim();
    }

    private async ValueTask<string?> ReadOptionalAsync(string prompt, CancellationToken cancellationToken)
    {
        await output.WriteAsync(prompt);
        var value = await input.ReadLineAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsYes(string? value) =>
        string.Equals(value?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
}
