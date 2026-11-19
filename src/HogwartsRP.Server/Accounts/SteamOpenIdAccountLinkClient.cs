using System.Globalization;
using System.Net.Http;
using System.Text;

namespace HogwartsRP.Server.Accounts;

/// <summary>
/// Verifies Steam OpenID 2.0 assertions server-to-server and links the verified SteamID64.
/// The caller must first consume the matching authenticated account-link transaction.
/// </summary>
public sealed class SteamOpenIdAccountLinkClient
{
    private static readonly Uri ProviderEndpoint = new("https://steamcommunity.com/openid/");
    private const string OpenIdNamespace = "http://specs.openid.net/auth/2.0";
    private const string ClaimedIdPrefix = "http://steamcommunity.com/openid/id/";
    private readonly HttpClient _httpClient;
    private readonly JsonAccountIdentityStore _identityStore;
    private readonly TimeProvider _timeProvider;

    public SteamOpenIdAccountLinkClient(
        HttpClient httpClient,
        JsonAccountIdentityStore identityStore,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Validates the returned OpenID assertion with Steam's fixed endpoint. The dictionary must
    /// contain the decoded openid.* callback parameters; browser-supplied account IDs are ignored.
    /// </summary>
    public async ValueTask<ExternalAccountLink> VerifyAndLinkAsync(
        AccountLinkAuthorizationTransaction transaction,
        string state,
        IReadOnlyDictionary<string, string> assertion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(assertion);
        if (transaction.Provider != ExternalIdentityProvider.Steam)
            throw new ArgumentException("The authorization transaction is not for Steam.", nameof(transaction));
        if (transaction.AccountId == Guid.Empty)
            throw new ArgumentException("The authorization transaction has no account ID.", nameof(transaction));
        if (transaction.CodeVerifier is not null)
            throw new ArgumentException("Steam OpenID transactions must not contain a PKCE verifier.", nameof(transaction));
        if (state is not { Length: 43 } || state.Any(character =>
            character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new ArgumentException("The Steam account-link state is invalid.", nameof(state));
        if (assertion.Count is < 8 or > 64)
            throw new InvalidDataException("The Steam OpenID assertion has an invalid number of fields.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in assertion)
        {
            if (!pair.Key.StartsWith("openid.", StringComparison.Ordinal)
                || pair.Key.Length > 64
                || string.IsNullOrEmpty(pair.Value)
                || pair.Value.Length > 4096
                || pair.Value.Any(char.IsControl))
                throw new InvalidDataException("The Steam OpenID assertion contains an invalid field.");
            values.Add(pair.Key, pair.Value);
        }

        Require(values, "openid.ns", OpenIdNamespace);
        Require(values, "openid.mode", "id_res");
        Require(values, "openid.op_endpoint", ProviderEndpoint.AbsoluteUri);
        Require(values, "openid.identity", values.GetValueOrDefault("openid.claimed_id") ?? string.Empty);

        var claimedId = values["openid.claimed_id"];
        if (!claimedId.StartsWith(ClaimedIdPrefix, StringComparison.Ordinal)
            || claimedId.Length != ClaimedIdPrefix.Length + 17)
            throw new InvalidDataException("Steam returned an invalid claimed identity.");

        var steamId = claimedId[ClaimedIdPrefix.Length..];
        if (!steamId.All(character => character is >= '0' and <= '9')
            || !ulong.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId)
            || numericId == 0)
            throw new InvalidDataException("Steam returned an invalid SteamID64.");

        var expectedReturnTo = BuildExpectedReturnTo(transaction.CallbackUri, state);
        Require(values, "openid.return_to", expectedReturnTo);
        ValidateNonce(values.GetValueOrDefault("openid.response_nonce"));

        foreach (var required in new[] { "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle" })
        {
            if (!values.TryGetValue("openid.signed", out var signed)
                || !signed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(required, StringComparer.Ordinal))
                throw new InvalidDataException($"Steam did not sign the required OpenID field: {required}.");
        }

        RequireNonEmpty(values, "openid.assoc_handle");
        RequireNonEmpty(values, "openid.sig");
        RequireNonEmpty(values, "openid.signed");

        var verified = await VerifyWithSteamAsync(values, cancellationToken);
        if (!verified)
            throw new InvalidDataException("Steam did not validate the OpenID assertion.");

        return await _identityStore.LinkVerifiedIdentityAsync(
            transaction.AccountId,
            ExternalIdentityProvider.Steam,
            steamId,
            null,
            cancellationToken);
    }

    private async Task<bool> VerifyWithSteamAsync(
        IReadOnlyDictionary<string, string> assertion,
        CancellationToken cancellationToken)
    {
        var form = assertion.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        form["openid.mode"] = "check_authentication";
        using var request = new HttpRequestMessage(HttpMethod.Post, ProviderEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        const int maximumResponseBytes = 4096;
        if (response.Content.Headers.ContentLength is > maximumResponseBytes)
            throw new InvalidDataException("Steam returned an oversized OpenID verification response.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maximumResponseBytes)
                throw new InvalidDataException("Steam returned an oversized OpenID verification response.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var body = Encoding.UTF8.GetString(buffer.ToArray());
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Contains("ns:" + OpenIdNamespace, StringComparer.Ordinal)
            && lines.Contains("is_valid:true", StringComparer.Ordinal);
    }

    private void ValidateNonce(string? nonce)
    {
        if (nonce is not { Length: >= 20 and <= 256 } || nonce.Any(char.IsControl))
            throw new InvalidDataException("Steam returned an invalid OpenID response nonce.");

        var timestampText = nonce[..20];
        if (!DateTimeOffset.TryParseExact(
            timestampText,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp))
            throw new InvalidDataException("Steam returned an invalid OpenID response nonce timestamp.");

        var age = _timeProvider.GetUtcNow() - timestamp;
        if (age < TimeSpan.FromMinutes(-2) || age > TimeSpan.FromMinutes(10))
            throw new InvalidDataException("The Steam OpenID response nonce is outside the accepted time window.");
    }

    private static string BuildExpectedReturnTo(Uri callbackUri, string state)
    {
        if (!callbackUri.IsAbsoluteUri
            || callbackUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(callbackUri.UserInfo)
            || !string.IsNullOrEmpty(callbackUri.Fragment))
            throw new ArgumentException("Steam callback URIs must be absolute HTTPS URLs.", nameof(callbackUri));

        var builder = new UriBuilder(callbackUri);
        var query = callbackUri.Query.TrimStart('?');
        if (query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part.Split('=', 2)[0], "state", StringComparison.Ordinal)))
            throw new ArgumentException("The registered Steam callback URI must not already contain state.", nameof(callbackUri));
        builder.Query = string.IsNullOrEmpty(query)
            ? "state=" + Uri.EscapeDataString(state)
            : query + "&state=" + Uri.EscapeDataString(state);
        return builder.Uri.AbsoluteUri;
    }

    private static void Require(
        IReadOnlyDictionary<string, string> values,
        string key,
        string expected)
    {
        if (!values.TryGetValue(key, out var actual)
            || !string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException("The Steam OpenID assertion is missing or has an unexpected " + key + " value.");
    }

    private static void RequireNonEmpty(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The Steam OpenID assertion is missing " + key + ".");
    }
}
