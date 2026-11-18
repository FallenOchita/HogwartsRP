using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HogwartsRP.Launcher;

/// <summary>Performs an OIDC authorization-code flow in the system browser and exchanges its ID token with a selected server.</summary>
public sealed class OidcJoinCredentialProvider : IJoinCredentialProvider, IDisposable
{
    private const string CallbackPath = "/callback/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _issuer;
    private readonly string _clientId;
    private readonly IProgress<string>? _progress;
    private readonly HttpClient _httpClient;

    private sealed record ProviderMetadata(string Issuer, Uri AuthorizationEndpoint, Uri TokenEndpoint);
    private sealed record ChallengeResponse(string ChallengeId, string Nonce);
    private sealed record TokenResponse(string AccessToken, string TokenType, string IdToken);
    private sealed record TicketRequest(string ChallengeId, string IdToken);
    private sealed record TicketResponse(string Credential, DateTimeOffset ExpiresAt);

    public OidcJoinCredentialProvider(string issuer, string clientId, IProgress<string>? progress = null)
    {
        _issuer = ValidateIssuer(issuer);
        _clientId = string.IsNullOrWhiteSpace(clientId) || clientId.Trim().Length > 512 || clientId.Any(char.IsControl)
            ? throw new ArgumentException("A valid OIDC client ID is required.", nameof(clientId))
            : clientId.Trim();
        _progress = progress;
        _httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async ValueTask<string> GetJoinCredentialAsync(ServerSelection server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        var serverBase = BuildServerHttpUri(server.Endpoint);
        _progress?.Report($"Requesting a sign-in challenge from {server.DisplayName}.");
        var challenge = await GetChallengeAsync(serverBase, cancellationToken);
        var metadata = await DiscoverProviderAsync(cancellationToken);

        var state = CreateRandomToken(32);
        var verifier = CreateRandomToken(48);
        var challengeHash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = new Uri($"http://127.0.0.1:{port}{CallbackPath}", UriKind.Absolute);
        var authorizationUri = BuildAuthorizationUri(metadata.AuthorizationEndpoint, redirectUri, state, challenge.Nonce, challengeHash);

        using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        authTimeout.CancelAfter(TimeSpan.FromMinutes(5));
        _progress?.Report("Opening the identity provider in your default browser.");
        try
        {
            _ = Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri) { UseShellExecute = true })
                ?? throw new InvalidOperationException("The system browser did not accept the sign-in request.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("Unable to open the system browser for sign-in.", exception);
        }

        _progress?.Report("Waiting for browser sign-in to finish.");
        var authorizationCode = await ReceiveAuthorizationCodeAsync(listener, redirectUri, state, authTimeout.Token);
        _progress?.Report("Exchanging the authorization code securely.");
        var idToken = await RedeemAuthorizationCodeAsync(metadata, authorizationCode, redirectUri, verifier, cancellationToken);

        _progress?.Report("Requesting a server-validated join ticket.");
        return await ExchangeForJoinTicketAsync(serverBase, challenge.ChallengeId, idToken, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async ValueTask<ChallengeResponse> GetChallengeAsync(Uri serverBase, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(serverBase, "/auth/oidc/challenge"));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("The selected server is not configured for OIDC sign-in or could not create a sign-in challenge.");
        var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>(JsonOptions, cancellationToken);
        if (challenge is null || string.IsNullOrWhiteSpace(challenge.ChallengeId) || string.IsNullOrWhiteSpace(challenge.Nonce))
            throw new InvalidDataException("The server returned an invalid sign-in challenge.");
        return challenge;
    }

    private async ValueTask<ProviderMetadata> DiscoverProviderAsync(CancellationToken cancellationToken)
    {
        var discoveryUri = new Uri(_issuer.TrimEnd('/') + "/.well-known/openid-configuration", UriKind.Absolute);
        using var response = await _httpClient.GetAsync(discoveryUri, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("OIDC provider discovery failed.");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        var metadataIssuer = GetString(root, "issuer");
        var authorizationEndpoint = GetEndpoint(root, "authorization_endpoint");
        var tokenEndpoint = GetEndpoint(root, "token_endpoint");
        if (metadataIssuer != _issuer || authorizationEndpoint is null || tokenEndpoint is null)
            throw new InvalidDataException("The OIDC provider metadata is incomplete or does not match the configured issuer.");
        return new(metadataIssuer, authorizationEndpoint, tokenEndpoint);
    }

    private Uri BuildAuthorizationUri(Uri endpoint, Uri redirectUri, string state, string nonce, string codeChallenge)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };
        var builder = new UriBuilder(endpoint);
        var existing = builder.Query.TrimStart('?');
        var added = string.Join('&', parameters.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        builder.Query = string.IsNullOrEmpty(existing) ? added : existing + "&" + added;
        return builder.Uri;
    }

    private async ValueTask<string> RedeemAuthorizationCodeAsync(
        ProviderMetadata metadata,
        string authorizationCode,
        Uri redirectUri,
        string verifier,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["client_id"] = _clientId,
            ["code_verifier"] = verifier
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, metadata.TokenEndpoint) { Content = new FormUrlEncodedContent(fields) };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("The identity provider rejected the sign-in response.");
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
        if (tokens is null || !string.Equals(tokens.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(tokens.AccessToken) || string.IsNullOrWhiteSpace(tokens.IdToken))
            throw new InvalidDataException("The identity provider returned an incomplete OIDC token response.");
        return tokens.IdToken;
    }

    private async ValueTask<string> ExchangeForJoinTicketAsync(Uri serverBase, string challengeId, string idToken, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            new Uri(serverBase, "/auth/tickets"), new TicketRequest(challengeId, idToken), JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("The selected server rejected the OIDC sign-in or could not issue a join ticket.");
        var ticket = await response.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions, cancellationToken);
        if (ticket is null || string.IsNullOrWhiteSpace(ticket.Credential) || ticket.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("The server returned an invalid join ticket.");
        _progress?.Report("Sign-in complete; connecting to the selected server.");
        return ticket.Credential;
    }

    private static async ValueTask<string> ReceiveAuthorizationCodeAsync(
        TcpListener listener,
        Uri redirectUri,
        string expectedState,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            if (client.Client.RemoteEndPoint is not IPEndPoint peer || !IPAddress.IsLoopback(peer.Address))
            {
                await WriteBrowserResponseAsync(client.GetStream(), 403, "This sign-in callback is local to the launcher.", cancellationToken);
                continue;
            }

            var stream = client.GetStream();
            try
            {
                var requestLine = await ReadHttpLineAsync(stream, 4096, cancellationToken);
                if (requestLine is null) continue;
                var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var headerBytes = 0;
                while (true)
                {
                    var line = await ReadHttpLineAsync(stream, 4096, cancellationToken);
                    if (line is null) throw new InvalidDataException("The browser sent an incomplete callback.");
                    headerBytes += line.Length;
                    if (headerBytes > 8192) throw new InvalidDataException("The browser callback headers are too large.");
                    if (line.Length == 0) break;
                    var separator = line.IndexOf(':');
                    if (separator <= 0 || !headers.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim()))
                        throw new InvalidDataException("The browser sent an invalid callback header.");
                }

                if (requestParts.Length != 3 || requestParts[0] != "GET" || !headers.TryGetValue("Host", out var host) ||
                    !host.Equals(redirectUri.Authority, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteBrowserResponseAsync(stream, 400, "Invalid sign-in callback. Return to the launcher.", cancellationToken);
                    continue;
                }

                var callbackUri = new Uri(redirectUri, requestParts[1]);
                if (callbackUri.GetLeftPart(UriPartial.Authority) != redirectUri.GetLeftPart(UriPartial.Authority) || callbackUri.AbsolutePath != CallbackPath)
                {
                    await WriteBrowserResponseAsync(stream, 404, "Callback not found.", cancellationToken);
                    continue;
                }

                var query = ParseQuery(callbackUri.Query);
                if (!query.TryGetValue("state", out var returnedState) || !FixedTimeEquals(expectedState, returnedState))
                {
                    await WriteBrowserResponseAsync(stream, 400, "Sign-in response did not match. Return to the launcher.", cancellationToken);
                    continue;
                }
                if (query.ContainsKey("error"))
                {
                    await WriteBrowserResponseAsync(stream, 400, "Sign-in was not completed. You may close this page.", cancellationToken);
                    throw new UnauthorizedAccessException("The identity provider did not complete sign-in.");
                }
                if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                {
                    await WriteBrowserResponseAsync(stream, 400, "Sign-in response did not include an authorization code.", cancellationToken);
                    throw new UnauthorizedAccessException("The identity provider returned no authorization code.");
                }

                await WriteBrowserResponseAsync(stream, 200, "Sign-in received. You may return to the HogwartsRP launcher and close this page.", cancellationToken);
                return code;
            }
            catch (InvalidDataException)
            {
                await WriteBrowserResponseAsync(stream, 400, "Invalid sign-in callback. Return to the launcher.", cancellationToken);
            }
        }
    }

    private static async ValueTask<string?> ReadHttpLineAsync(NetworkStream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var single = new byte[1];
        while (bytes.Count <= maximumBytes)
        {
            var read = await stream.ReadAsync(single.AsMemory(0, 1), cancellationToken);
            if (read == 0) return bytes.Count == 0 ? null : throw new InvalidDataException("The browser sent an incomplete HTTP line.");
            if (single[0] == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r') bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
            bytes.Add(single[0]);
        }
        throw new InvalidDataException("The browser callback line is too long.");
    }

    private static async ValueTask WriteBrowserResponseAsync(NetworkStream stream, int statusCode, string message, CancellationToken cancellationToken)
    {
        var body = "<!doctype html><meta charset=\"utf-8\"><title>HogwartsRP sign-in</title><p>" + WebUtility.HtmlEncode(message) + "</p>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var status = statusCode switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", _ => "Bad Request" };
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusCode} {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nCache-Control: no-store\r\nPragma: no-cache\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? part : part[..separator].Replace('+', ' '));
            var value = Uri.UnescapeDataString(separator < 0 ? "" : part[(separator + 1)..].Replace('+', ' '));
            if (!result.TryAdd(key, value)) throw new InvalidDataException("The browser callback contains a duplicate parameter.");
        }
        return result;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static Uri BuildServerHttpUri(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath is not ("/" or "/connect"))
            throw new ArgumentException("Server address must be an absolute server root or /connect endpoint without a query or fragment.", nameof(endpoint));
        var scheme = endpoint.Scheme switch
        {
            "https" or "wss" => "https",
            "http" when endpoint.IsLoopback => "http",
            "ws" when endpoint.IsLoopback => "http",
            _ => throw new ArgumentException("Remote authentication requires an HTTPS/WSS server endpoint.", nameof(endpoint))
        };
        return new UriBuilder(endpoint) { Scheme = scheme, Path = "/", Query = "", Fragment = "" }.Uri;
    }

    private static Uri? GetEndpoint(JsonElement root, string name)
    {
        var value = GetString(root, name);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsSecureEndpoint(uri)) return null;
        return uri;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string CreateRandomToken(int byteCount) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ValidateIssuer(string issuer)
    {
        var configuredIssuer = issuer.Trim();
        if (!Uri.TryCreate(configuredIssuer, UriKind.Absolute, out var uri) || !IsSecureEndpoint(uri) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("OIDC issuer must be an HTTPS URL (HTTP is allowed only on loopback for local development).", nameof(issuer));
        return configuredIssuer!;
    }

    private static bool IsSecureEndpoint(Uri uri) =>
        uri.IsAbsoluteUri && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
