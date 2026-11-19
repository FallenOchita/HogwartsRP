using System.Text.Json;

namespace HogwartsRP.Launcher;

public sealed record ServerProfile(string Id, string Name, string Address)
{
    public override string ToString() => Name;
}

/// <summary>Stores the launcher's saved server list in a local JSON file.</summary>
public sealed class JsonServerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _sync = new();

    public JsonServerProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HogwartsRP", "server-profiles.json");
    }

    public IReadOnlyList<ServerProfile> Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath)) return Array.Empty<ServerProfile>();
            try
            {
                var profiles = JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(_filePath), JsonOptions)
                    ?? throw new InvalidDataException("The saved server profile file is empty or invalid.");
                ValidateProfiles(profiles);
                return profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The saved server profile file contains invalid JSON.", ex);
            }
        }
    }

    public ServerProfile Save(string name, string address)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedAddress = address?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 64)
            throw new ArgumentException("Server names must contain between 1 and 64 characters.", nameof(name));
        if (!Uri.TryCreate(normalizedAddress, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Server address must be an absolute HTTP or HTTPS URL.", nameof(address));

        lock (_sync)
        {
            var profiles = File.Exists(_filePath)
                ? JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(_filePath), JsonOptions) ?? []
                : [];
            ValidateProfiles(profiles);
            var existing = profiles.FirstOrDefault(profile =>
                string.Equals(profile.Address.TrimEnd('/'), normalizedAddress.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            var saved = new ServerProfile(existing?.Id ?? Guid.NewGuid().ToString("N"), normalizedName, normalizedAddress);
            if (existing is not null) profiles.Remove(existing);
            profiles.Add(saved);
            WriteAtomically(profiles);
            return saved;
        }
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_sync)
        {
            if (!File.Exists(_filePath)) return false;
            var profiles = JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(_filePath), JsonOptions) ?? [];
            ValidateProfiles(profiles);
            var removed = profiles.RemoveAll(profile => profile.Id == id) > 0;
            if (removed) WriteAtomically(profiles);
            return removed;
        }
    }

    private void WriteAtomically(List<ServerProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profiles, JsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static void ValidateProfiles(List<ServerProfile> profiles)
    {
        if (profiles.Any(profile => profile is null || string.IsNullOrWhiteSpace(profile.Id) ||
            string.IsNullOrWhiteSpace(profile.Name) || !Uri.TryCreate(profile.Address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            throw new InvalidDataException("The saved server profile file contains an invalid profile.");
        if (profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count)
            throw new InvalidDataException("The saved server profile file contains duplicate profile IDs.");
    }
}
