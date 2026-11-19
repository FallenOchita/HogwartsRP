using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HogwartsRP.Server.Accounts;

/// <summary>Server-only Epic OAuth client settings. Never expose the client secret to the browser.</summary>
public sealed record EpicGamesAccountLinkOptions(
    string ClientId,
    string ClientSecret,
    string DeploymentId);

/// <summary>
/// Exchanges an Epic authorization code and verifies the resulting bearer token through Epic's
/// token-introspection endpoint before linking the returned Epic account ID.
/// </summary>
public sealed class EpicGamesAccountLinkClient
{
    private static readonly Uri TokenEndpoint = new("https://api.epicgames.dev/epic/oauth/v2/token");
    private static readonly Uri TokenInfoEndpoint = new("https://api.epicgames.dev/epic/oauth/v2/tokenInfo");
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = 8,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    private readonly HttpClient _httpClient;
    private readonly JsonAccountIdentityStore _identityStore;
    private readonly EpicGamesAccountLinkOptions _options;

    public EpicGamesAccountLinkClient(
        HttpClient httpClient,
        JsonAccountIdentityStore identityStore,
        EpicGamesAccountLinkOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ClientId) || options.ClientId.Length > 256
            || string.IsNullOrWhiteSpace(options.ClientSecret) || options.ClientSecret.Length > 512
            || string.IsNullOrWhiteSpace(options.DeploymentId) || options.DeploymentId.Length > 256
            || options.ClientId.Any(char.IsControl)
            || options.ClientSecret.Any(char.IsControl)
            || options.DeploymentId.Any(char.IsControl))
            throw new ArgumentException("Epic OAuth client credentials and deployment ID are required.", nameof(options));
    }

    /// <summary>
    /// Exchanges a callback code using the server-held Epic client credentials, introspects the
    /// access token with Epic, and persists only the account ID returned by Epic's token-info API.
    /// Epic's documented web authorization-code flow does not specify PKCE parameters; this
    /// adapter therefore requires the Epic transaction to have no PKCE verifier.
    /// </summary>
    public async ValueTask<ExternalAccountLink> ExchangeAndLinkAsync(
        AccountLinkAuthorizationTransaction transaction,
        string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Provider != ExternalIdentityProvider.EpicGames)
            throw new ArgumentException("The authorization transaction is not for Epic Games.", nameof(transaction));
        if (transaction.AccountId == Guid.Empty)
            throw new ArgumentException("The authorization transaction has no account ID.", nameof(transaction));
        if (transaction.CodeVerifier is not null)
            throw new ArgumentException("Epic web authorization transactions must not contain a PKCE verifier.", nameof(transaction));
        if (string.IsNullOrWhiteSpace(authorizationCode) || authorizationCode.Length > 2048
            || authorizationCode.Any(char.IsControl))
            throw new ArgumentException("The Epic authorization code is invalid.", nameof(authorizationCode));
        if (!transaction.CallbackUri.IsAbsoluteUri
            || transaction.CallbackUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(transaction.CallbackUri.UserInfo)
            || !string.IsNullOrEmpty(transaction.CallbackUri.Fragment))
            throw new ArgumentException("The Epic callback URI is invalid.", nameof(transaction));

        var accessToken = await ExchangeCodeAsync(authorizationCode, transaction.CallbackUri, cancellationToken);
        var identity = await VerifyTokenAsync(accessToken, cancellationToken);

        return await _identityStore.LinkVerifiedIdentityAsync(
            transaction.AccountId,
            ExternalIdentityProvider.EpicGames,
            identity.AccountId,
            null,
            cancellationToken);
    }

    private async Task<string> ExchangeCodeAsync(
        string authorizationCode,
        Uri callbackUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["deployment_id"] = _options.DeploymentId,
                ["scope"] = "basic_profile",
                ["code"] = authorizationCode,
                ["redirect_uri"] = callbackUri.AbsoluteUri
            })
        };
        request.Headers.Authorization = CreateClientAuthorization();
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response.Content, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("access_token", out var accessTokenElement)
            || accessTokenElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("token_type", out var tokenTypeElement)
            || tokenTypeElement.ValueKind != JsonValueKind.String
            || !string.Equals(tokenTypeElement.GetString(), "bearer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Epic returned an invalid token response.");

        var accessToken = accessTokenElement.GetString();
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 8192
            || accessToken.Any(char.IsControl))
            throw new InvalidDataException("Epic returned an invalid access token.");

        return accessToken;
    }

    private async Task<EpicIdentity> VerifyTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenInfoEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = accessToken
            })
        };
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response.Content, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("active", out var activeElement)
            || activeElement.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("token_type", out var tokenTypeElement)
            || tokenTypeElement.ValueKind != JsonValueKind.String
            || !string.Equals(tokenTypeElement.GetString(), "bearer", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("client_id", out var clientIdElement)
            || clientIdElement.ValueKind != JsonValueKind.String
            || !string.Equals(clientIdElement.GetString(), _options.ClientId, StringComparison.Ordinal)
            || !root.TryGetProperty("account_id", out var accountIdElement)
            || accountIdElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Epic did not return an active token for this OAuth client.");

        var accountId = accountIdElement.GetString();
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 128
            || accountId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidDataException("Epic returned an invalid account identity.");

        return new EpicIdentity(accountId);
    }

    private AuthenticationHeaderValue CreateClientAuthorization()
    {
        var credentials = Encoding.UTF8.GetBytes(_options.ClientId + ":" + _options.ClientSecret);
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentials));
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        const int maximumResponseBytes = 64 * 1024;
        if (content.Headers.ContentLength is > maximumResponseBytes)
            throw new InvalidDataException("Epic returned an oversized response.");

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maximumResponseBytes)
                throw new InvalidDataException("Epic returned an oversized response.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return JsonDocument.Parse(buffer.ToArray(), JsonOptions);
    }

    private sealed record EpicIdentity(string AccountId);
}
