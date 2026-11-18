using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HogwartsRP.Core;
using HogwartsRP.Networking.Abstractions;

namespace HogwartsRP.Server;

public sealed record OidcChallengeResponse(string ChallengeId, string Nonce);
public sealed record OidcJoinTicketRequest(string ChallengeId, string IdToken);
public sealed record OidcJoinTicketResponse(string Credential, DateTimeOffset ExpiresAt);

/// <summary>Exchanges a provider-signed OIDC ID token for a server-owned, single-use join ticket.</summary>
public interface IJoinTicketExchange
{
    ValueTask<OidcChallengeResponse> CreateChallengeAsync(CancellationToken cancellationToken = default);
    ValueTask<OidcJoinTicketResponse?> ExchangeAsync(OidcJoinTicketRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates OIDC ID tokens using the configured issuer's published RS256 signing keys.
/// It accepts only a token bound to a short-lived challenge created by this server.
/// </summary>
public sealed class OidcTicketExchange : IJoinTicketExchange, IDisposable
{
    private sealed record Challenge(string Nonce, DateTimeOffset ExpiresAt);
    private const int MaximumChallenges = 10_000;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, Challenge> _challenges = new(StringComparer.Ordinal);
    private readonly object _challengeGate = new();
    private readonly string _issuer;
    private readonly string _clientId;
    private readonly OneTimeJoinTicketStore _tickets;
    private readonly TimeProvider _timeProvider;
    private readonly HttpClient _httpClient;

    public OidcTicketExchange(
        string issuer,
        string clientId,
        OneTimeJoinTicketStore tickets,
        TimeProvider? timeProvider = null,
        HttpMessageHandler? httpMessageHandler = null)
    {
        _issuer = ValidateIssuer(issuer);
        _clientId = string.IsNullOrWhiteSpace(clientId) || clientId.Trim().Length > 512 || clientId.Any(char.IsControl)
            ? throw new ArgumentException("A valid OIDC client ID is required.", nameof(clientId))
            : clientId.Trim();
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _httpClient = new HttpClient(httpMessageHandler ?? new SocketsHttpHandler { AllowAutoRedirect = false });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public ValueTask<OidcChallengeResponse> CreateChallengeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_challengeGate)
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var pair in _challenges)
                if (pair.Value.ExpiresAt <= now) _challenges.TryRemove(pair.Key, out _);
            if (_challenges.Count >= MaximumChallenges)
                throw new InvalidOperationException("The server has too many pending sign-in challenges.");

            while (true)
            {
                var challengeId = CreateRandomToken();
                var nonce = CreateRandomToken();
                if (_challenges.TryAdd(challengeId, new(nonce, now + ChallengeLifetime)))
                    return ValueTask.FromResult(new OidcChallengeResponse(challengeId, nonce));
            }
        }
    }

    public async ValueTask<OidcJoinTicketResponse?> ExchangeAsync(
        OidcJoinTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ChallengeId) || request.ChallengeId.Length is < 16 or > 128 ||
            string.IsNullOrWhiteSpace(request.IdToken) || request.IdToken.Length is < 20 or > 16_384 ||
            !_challenges.TryRemove(request.ChallengeId, out var challenge) || challenge.ExpiresAt <= _timeProvider.GetUtcNow())
            return null;

        var identity = await ValidateIdentityAsync(request.IdToken, challenge.Nonce, cancellationToken);
        if (identity is null) return null;

        var credential = _tickets.Issue(identity, TicketLifetime);
        return new(credential, _timeProvider.GetUtcNow() + TicketLifetime);
    }

    public void Dispose() => _httpClient.Dispose();

    private async ValueTask<AuthenticatedPlayer?> ValidateIdentityAsync(string idToken, string expectedNonce, CancellationToken cancellationToken)
    {
        var metadataUri = new Uri(_issuer.TrimEnd('/') + "/.well-known/openid-configuration", UriKind.Absolute);
        using var metadataResponse = await _httpClient.GetAsync(metadataUri, cancellationToken);
        if (!metadataResponse.IsSuccessStatusCode) throw new HttpRequestException("OIDC discovery request failed.");
        using var metadata = await JsonDocument.ParseAsync(await metadataResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var metadataRoot = metadata.RootElement;
        if (!metadataRoot.TryGetProperty("issuer", out var issuerClaim) || issuerClaim.GetString() != _issuer ||
            !metadataRoot.TryGetProperty("jwks_uri", out var jwksClaim) || !Uri.TryCreate(jwksClaim.GetString(), UriKind.Absolute, out var jwksUri) || !IsSecureEndpoint(jwksUri))
            throw new InvalidOperationException("The OIDC issuer metadata is invalid or does not match the configured issuer.");

        using var keysResponse = await _httpClient.GetAsync(jwksUri, cancellationToken);
        if (!keysResponse.IsSuccessStatusCode) throw new HttpRequestException("OIDC signing-key request failed.");
        using var keyDocument = await JsonDocument.ParseAsync(await keysResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        try { return ValidateSignedToken(idToken, expectedNonce, keyDocument.RootElement); }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private AuthenticatedPlayer? ValidateSignedToken(string token, string expectedNonce, JsonElement keySet)
    {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0)) return null;
        using var header = JsonDocument.Parse(DecodeBase64Url(parts[0]));
        using var claims = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        var headerRoot = header.RootElement;
        var claimsRoot = claims.RootElement;
        if (GetString(headerRoot, "alg") != "RS256" || headerRoot.TryGetProperty("crit", out _) ||
            headerRoot.TryGetProperty("b64", out var encodedPayload) && encodedPayload.ValueKind != JsonValueKind.True ||
            !headerRoot.TryGetProperty("kid", out var keyIdClaim)) return null;
        var keyId = keyIdClaim.GetString();
        if (string.IsNullOrWhiteSpace(keyId) || !keySet.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array) return null;
        var jwk = keys.EnumerateArray().FirstOrDefault(key =>
            GetString(key, "kid") == keyId && GetString(key, "kty") == "RSA" &&
            (GetString(key, "use") is null or "sig") && (GetString(key, "alg") is null or "RS256"));
        if (jwk.ValueKind == JsonValueKind.Undefined || !jwk.TryGetProperty("n", out var modulus) || !jwk.TryGetProperty("e", out var exponent)) return null;

        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = DecodeBase64Url(modulus.GetString() ?? ""), Exponent = DecodeBase64Url(exponent.GetString() ?? "") });
        var signedBytes = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        if (!rsa.VerifyData(signedBytes, DecodeBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;

        if (GetString(claimsRoot, "iss") != _issuer || GetString(claimsRoot, "sub") is not { Length: > 0 and <= 256 } subject ||
            GetString(claimsRoot, "nonce") != expectedNonce || !HasExpectedAudience(claimsRoot) || !IsTimeValid(claimsRoot)) return null;

        var now = _timeProvider.GetUtcNow();
        if (!TryGetUnixTime(claimsRoot, "exp", out var expiresAt) || expiresAt <= now ||
            !TryGetUnixTime(claimsRoot, "iat", out var issuedAt) || issuedAt > now + TimeSpan.FromMinutes(5)) return null;
        if (claimsRoot.TryGetProperty("nbf", out _) && (!TryGetUnixTime(claimsRoot, "nbf", out var notBefore) || notBefore > now + TimeSpan.FromMinutes(5))) return null;

        var displayName = GetString(claimsRoot, "name") ?? GetString(claimsRoot, "preferred_username") ?? subject;
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 64 || displayName.Any(char.IsControl))
            displayName = subject.Length <= 64 ? subject : "HogwartsRP Player";
        var accountHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_issuer + "\n" + subject)));
        return new AuthenticatedPlayer("oidc:" + accountHash, displayName.Trim());
    }

    private bool HasExpectedAudience(JsonElement claims)
    {
        if (!claims.TryGetProperty("aud", out var audiences)) return false;
        var matches = audiences.ValueKind switch
        {
            JsonValueKind.String => audiences.GetString() == _clientId,
            JsonValueKind.Array => audiences.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == _clientId),
            _ => false
        };
        if (!matches) return false;
        if (audiences.ValueKind == JsonValueKind.Array && audiences.GetArrayLength() > 1 && GetString(claims, "azp") != _clientId) return false;
        return GetString(claims, "azp") is not { } authorizedParty || authorizedParty == _clientId;
    }

    private static bool IsTimeValid(JsonElement claims)
    {
        if (!claims.TryGetProperty("exp", out var expiry) || expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt64(out _)) return false;
        if (!claims.TryGetProperty("iat", out var issuedAt) || issuedAt.ValueKind != JsonValueKind.Number || !issuedAt.TryGetInt64(out _)) return false;
        return !claims.TryGetProperty("nbf", out var notBefore) || (notBefore.ValueKind == JsonValueKind.Number && notBefore.TryGetInt64(out _));
    }

    private static bool TryGetUnixTime(JsonElement claims, string name, out DateTimeOffset result)
    {
        result = default;
        return claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) &&
            TryConvertUnixTime(seconds, out result);
    }

    private static bool TryConvertUnixTime(long seconds, out DateTimeOffset result)
    {
        try { result = DateTimeOffset.FromUnixTimeSeconds(seconds); return true; }
        catch (ArgumentOutOfRangeException) { result = default; return false; }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static byte[] DecodeBase64Url(string text)
    {
        if (text.Length > 12_000) throw new FormatException("OIDC token segment is too large.");
        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException("Invalid base64url value.") };
        return Convert.FromBase64String(base64);
    }

    private static string CreateRandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
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
