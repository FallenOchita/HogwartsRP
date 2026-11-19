using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HogwartsRP.Server.Accounts;

/// <summary>
/// Public values needed to redirect a browser to a provider. The PKCE verifier is deliberately
/// not included; only the S256 challenge is safe to send to the provider.
/// </summary>
public sealed record AccountLinkAuthorizationStart(
    string State,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Server-only data recovered after a matching provider callback consumes its one-time state.
/// The code verifier must be used only for the matching provider code exchange.
/// </summary>
public sealed record AccountLinkAuthorizationTransaction(
    Guid AccountId,
    ExternalIdentityProvider Provider,
    Uri CallbackUri,
    string? CodeVerifier,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Short-lived, process-local OAuth/OpenID account-link transaction registry.
/// Transactions are bound to an authenticated browser session, central account, provider,
/// and exact registered callback URI. State values are stored only as SHA-256 hashes.
/// Discord and Epic authorization-code flows receive PKCE S256; Steam OpenID does not use PKCE.
/// </summary>
public sealed class AccountLinkAuthorizationStore
{
    private sealed record StoredTransaction(
        Guid AccountId,
        ExternalIdentityProvider Provider,
        string CallbackUri,
        byte[] SessionTokenHash,
        string? CodeVerifier,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, StoredTransaction> _transactions = new(StringComparer.Ordinal);
    private readonly object _issueGate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly int _maximumTransactions;

    public AccountLinkAuthorizationStore(
        TimeProvider? timeProvider = null,
        TimeSpan? lifetime = null,
        int maximumTransactions = 10_000)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? TimeSpan.FromMinutes(10);
        if (_lifetime < TimeSpan.FromMinutes(1) || _lifetime > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Account-link transactions must last between 1 and 15 minutes.");
        if (maximumTransactions < 1 || maximumTransactions > 100_000)
            throw new ArgumentOutOfRangeException(nameof(maximumTransactions));
        _maximumTransactions = maximumTransactions;
    }

    /// <summary>
    /// Starts a link transaction for the account authenticated by the supplied browser session.
    /// The account ID is checked against the server-owned session and is never accepted from browser input.
    /// </summary>
    public AccountLinkAuthorizationStart Begin(
        Guid accountId,
        string? sessionToken,
        ExternalIdentityProvider provider,
        Uri callbackUri,
        BrowserAccountSessionStore sessions)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account ID must not be empty.", nameof(accountId));
        if (!Enum.IsDefined(provider)) throw new ArgumentOutOfRangeException(nameof(provider));
        ArgumentNullException.ThrowIfNull(sessions);
        ValidateCallbackUri(callbackUri);

        if (!sessions.TryGet(sessionToken, out var session) || session is null || session.AccountId != accountId)
            throw new UnauthorizedAccessException("A valid browser session for this account is required.");

        lock (_issueGate)
        {
            RemoveExpired();
            if (_transactions.Count >= _maximumTransactions)
                throw new InvalidOperationException("The account-link transaction capacity has been reached.");

            var now = _timeProvider.GetUtcNow();
            var expiresAt = Min(now + _lifetime, session.ExpiresAtUtc);
            var state = CreateToken();
            var usePkce = provider is ExternalIdentityProvider.Discord or ExternalIdentityProvider.EpicGames;
            var verifier = usePkce ? CreateToken() : null;
            var challenge = verifier is null ? null : Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var key = HashToken(state);
            var stored = new StoredTransaction(
                accountId,
                provider,
                callbackUri.AbsoluteUri,
                HashTokenBytes(sessionToken!),
                verifier,
                now,
                expiresAt);

            if (!_transactions.TryAdd(key, stored))
                throw new CryptographicException("Unable to allocate a unique account-link state value.");

            return new AccountLinkAuthorizationStart(
                state,
                challenge,
                challenge is null ? null : "S256",
                expiresAt);
        }
    }

    /// <summary>
    /// Validates and atomically consumes a callback transaction. Invalid provider, callback,
    /// session, or expired state values do not consume a still-valid transaction.
    /// </summary>
    public bool TryConsume(
        string? state,
        ExternalIdentityProvider provider,
        Uri callbackUri,
        string? sessionToken,
        BrowserAccountSessionStore sessions,
        out AccountLinkAuthorizationTransaction? transaction)
    {
        transaction = null;
        if (!IsTokenShapeValid(state) || !Enum.IsDefined(provider)) return false;
        ArgumentNullException.ThrowIfNull(sessions);
        try { ValidateCallbackUri(callbackUri); }
        catch (ArgumentException) { return false; }

        if (!sessions.TryGet(sessionToken, out var session) || session is null) return false;

        var key = HashToken(state!);
        if (!_transactions.TryGetValue(key, out var stored)) return false;
        var now = _timeProvider.GetUtcNow();
        if (stored.ExpiresAtUtc <= now)
        {
            _transactions.TryRemove(key, out _);
            return false;
        }

        if (stored.Provider != provider
            || !string.Equals(stored.CallbackUri, callbackUri.AbsoluteUri, StringComparison.Ordinal)
            || stored.AccountId != session.AccountId
            || !CryptographicOperations.FixedTimeEquals(stored.SessionTokenHash, HashTokenBytes(sessionToken!)))
            return false;

        if (!_transactions.TryRemove(key, out var consumed)) return false;
        transaction = new AccountLinkAuthorizationTransaction(
            consumed.AccountId,
            consumed.Provider,
            new Uri(consumed.CallbackUri, UriKind.Absolute),
            consumed.CodeVerifier,
            consumed.CreatedAtUtc,
            consumed.ExpiresAtUtc);
        return true;
    }

    public int RemoveExpired()
    {
        var now = _timeProvider.GetUtcNow();
        var removed = 0;
        foreach (var pair in _transactions)
        {
            if (pair.Value.ExpiresAtUtc <= now && _transactions.TryRemove(pair.Key, out _))
                removed++;
        }
        return removed;
    }

    private static void ValidateCallbackUri(Uri callbackUri)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);
        if (!callbackUri.IsAbsoluteUri
            || !string.Equals(callbackUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(callbackUri.UserInfo)
            || !string.IsNullOrEmpty(callbackUri.Fragment))
            throw new ArgumentException("Provider callback URIs must be absolute HTTPS URLs without user info or fragments.", nameof(callbackUri));
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private static string CreateToken() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string HashToken(string token) =>
        Convert.ToHexString(HashTokenBytes(token));

    private static byte[] HashTokenBytes(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static bool IsTokenShapeValid(string? token) =>
        token is { Length: 43 } && token.All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
}
