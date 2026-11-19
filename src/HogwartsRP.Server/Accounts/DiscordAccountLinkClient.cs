using System.Net.Http.Headers;
using System.Text.Json;

namespace HogwartsRP.Server.Accounts;

/// <summary>Server-only Discord OAuth client settings. Never expose these values to the browser.</summary>
public sealed record DiscordAccountLinkOptions(string ClientId, string ClientSecret);

/// <summary>
/// Exchanges a callback authorization code with Discord, retrieves the authenticated Discord
/// user from Discord's API, and links that verified subject to the central HogwartsRP account.
/// The caller must first consume the matching AccountLinkAuthorizationStore transaction.
/// </summary>
public sealed class DiscordAccountLinkClient
{
    private static readonly Uri TokenEndpoint = new("https://discord.com/api/oauth2/token");
    private static readonly Uri CurrentUserEndpoint = new("https://discord.com/api/users/@me");
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = 8,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    private readonly HttpClient _httpClient;
    private readonly JsonAccountIdentityStore _identityStore;
    private readonly DiscordAccountLinkOptions _options;

    public DiscordAccountLinkClient(
        HttpClient httpClient,
        JsonAccountIdentityStore identityStore,
        DiscordAccountLinkOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ClientId) || options.ClientId.Length > 256
            || string.IsNullOrWhiteSpace(options.ClientSecret) || options.ClientSecret.Length > 512)
            throw new ArgumentException("Discord OAuth client credentials are required.", nameof(options));
    }

    public async ValueTask<ExternalAccountLink> ExchangeAndLinkAsync(
        AccountLinkAuthorizationTransaction transaction,
        string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Provider != ExternalIdentityProvider.Discord)
            throw new ArgumentException("The authorization transaction is not for Discord.", nameof(transaction));
        if (transaction.AccountId == Guid.Empty)
            throw new ArgumentException("The authorization transaction has no account ID.", nameof(transaction));
        if (transaction.CodeVerifier is not { Length: >= 43 and <= 128 } verifier)
            throw new ArgumentException("A valid Discord PKCE verifier is required.", nameof(transaction));
        if (string.IsNullOrWhiteSpace(authorizationCode) || authorizationCode.Length > 2048
            || authorizationCode.Any(char.IsControl))
            throw new ArgumentException("The Discord authorization code is invalid.", nameof(authorizationCode));
        if (!transaction.CallbackUri.IsAbsoluteUri
            || transaction.CallbackUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(transaction.CallbackUri.UserInfo)
            || !string.IsNullOrEmpty(transaction.CallbackUri.Fragment))
            throw new ArgumentException("The Discord callback URI is invalid.", nameof(transaction));

        var accessToken = await ExchangeCodeAsync(authorizationCode, transaction.CallbackUri, verifier, cancellationToken);
        var identity = await FetchCurrentUserAsync(accessToken, cancellationToken);

        // The only subject passed to persistence is obtained from Discord's authenticated /users/@me response.
        return await _identityStore.LinkVerifiedIdentityAsync(
            transaction.AccountId,
            ExternalIdentityProvider.Discord,
            identity.Subject,
            identity.DisplayName,
            cancellationToken);
    }

    private async Task<string> ExchangeCodeAsync(
        string authorizationCode,
        Uri callbackUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["redirect_uri"] = callbackUri.AbsoluteUri,
                ["code_verifier"] = codeVerifier
            })
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response.Content, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("access_token", out var accessTokenElement)
            || accessTokenElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Discord returned an invalid token response.");

        var accessToken = accessTokenElement.GetString();
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 8192
            || !root.TryGetProperty("token_type", out var tokenType)
            || tokenType.ValueKind != JsonValueKind.String
            || !string.Equals(tokenType.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Discord returned an invalid bearer token response.");

        return accessToken;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        const int maximumResponseBytes = 64 * 1024;
        if (content.Headers.ContentLength is > maximumResponseBytes)
            throw new InvalidDataException("Discord returned an oversized response.");

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maximumResponseBytes)
                throw new InvalidDataException("Discord returned an oversized response.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return JsonDocument.Parse(buffer.ToArray(), JsonOptions);
    }

    private async Task<DiscordIdentity> FetchCurrentUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CurrentUserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, JsonOptions, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("username", out var usernameElement)
            || usernameElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Discord returned an invalid current-user response.");

        var subject = idElement.GetString();
        var username = usernameElement.GetString();
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 256
            || string.IsNullOrWhiteSpace(username) || username.Length > 128
            || subject.Any(char.IsControl) || username.Any(char.IsControl))
            throw new InvalidDataException("Discord returned an invalid current-user identity.");

        string? globalName = null;
        if (root.TryGetProperty("global_name", out var globalNameElement)
            && globalNameElement.ValueKind == JsonValueKind.String)
            globalName = globalNameElement.GetString();

        var displayName = string.IsNullOrWhiteSpace(globalName) ? username : globalName;
        if (displayName!.Length > 128 || displayName.Any(char.IsControl))
            displayName = username;

        return new DiscordIdentity(subject, displayName);
    }

    private sealed record DiscordIdentity(string Subject, string DisplayName);
}
