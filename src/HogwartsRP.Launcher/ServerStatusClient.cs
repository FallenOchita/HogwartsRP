using System.Net.Http.Json;
using System.Text.Json;

namespace HogwartsRP.Launcher;

public sealed record ServerStatusSnapshot(
    string Status,
    string ServerName,
    int OnlinePlayers,
    int MaxPlayers,
    DateTimeOffset CheckedAtUtc);

/// <summary>Reads the unauthenticated, read-only public status endpoint for a saved server.</summary>
public sealed class ServerStatusClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public ServerStatusClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<ServerStatusSnapshot> GetStatusAsync(Uri serverAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        if (!serverAddress.IsAbsoluteUri || serverAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Server status checks require an absolute HTTP or HTTPS address.", nameof(serverAddress));

        var statusUri = new UriBuilder(serverAddress) { Path = "/status", Query = string.Empty, Fragment = string.Empty }.Uri;
        using var response = await _httpClient.GetAsync(statusUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<ServerStatusSnapshot>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The server returned an empty status response.");

        if (!string.Equals(snapshot.Status, "online", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(snapshot.ServerName) || snapshot.ServerName.Length > 64 ||
            snapshot.OnlinePlayers < 0 || snapshot.MaxPlayers < 1 || snapshot.OnlinePlayers > snapshot.MaxPlayers ||
            snapshot.CheckedAtUtc == default)
            throw new InvalidDataException("The server returned an invalid status response.");

        return snapshot;
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}
