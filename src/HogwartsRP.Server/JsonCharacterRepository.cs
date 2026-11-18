using System.Text.Json;
using HogwartsRP.GameIntegration.Abstractions;

namespace HogwartsRP.Server;

/// <summary>
/// File-backed character repository for single-server deployments.
/// Writes are serialized and committed by replacing a temporary file in the same directory.
/// </summary>
public sealed class JsonCharacterRepository : ICharacterRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RegisteredCharacter>? _characters;

    public JsonCharacterRepository(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async ValueTask<RegisteredCharacter> RegisterAsync(
        RegisteredCharacter character,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.Id == Guid.Empty)
            throw new ArgumentException("Character ID must not be empty.", nameof(character));
        if (string.IsNullOrWhiteSpace(character.OwnerPlayerId))
            throw new ArgumentException("Character owner must not be empty.", nameof(character));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_characters!.Any(existing => existing.Id == character.Id))
                throw new InvalidOperationException("A character with this ID is already registered.");

            _characters.Add(character);
            try
            {
                await SaveAsync(cancellationToken);
            }
            catch
            {
                _characters.RemoveAt(_characters.Count - 1);
                throw;
            }

            return character;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns only characters owned by the requested authenticated account.</summary>
    public async ValueTask<IReadOnlyList<RegisteredCharacter>> GetByOwnerAsync(
        string ownerPlayerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPlayerId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _characters!
                .Where(character => string.Equals(character.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal))
                .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_characters is not null)
            return;

        if (!File.Exists(_filePath))
        {
            _characters = [];
            return;
        }

        try
        {
            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            _characters = await JsonSerializer.DeserializeAsync<List<RegisteredCharacter>>(
                stream, JsonOptions, cancellationToken) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The character data file contains invalid JSON. It was not overwritten.", exception);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, _characters, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
