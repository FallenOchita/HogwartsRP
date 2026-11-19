using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HogwartsRP.Server.Accounts;

/// <summary>Non-secret information about an authenticated browser account session.</summary>
public sealed record BrowserAccountSessionInfo(
    Guid AccountId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// A newly issued browser session. The raw session and CSRF tokens are returned only once
/// to the trusted sign-in completion handler so it can set the cookie and provide the CSRF token.
/// </summary>
public sealed record IssuedBrowserAccountSession(
    string SessionToken,
    string CsrfToken,
    BrowserAccountSessionInfo Info);

/// <summary>
/// Process-local browser session registry. Only trusted server-side authentication code should
/// issue sessions; browser requests must never choose an account ID.
/// Raw bearer tokens are not retained in the registry.
/// </summary>
public sealed class BrowserAccountSessionStore
{
    private sealed record StoredSession(
        Guid AccountId,
        byte[] CsrfTokenHash,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, StoredSession> _sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;
    private readonly int _maximumSessions;

    public BrowserAccountSessionStore(
        TimeProvider? timeProvider = null,
        TimeSpan? lifetime = null,
        int maximumSessions = 100_000)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lifetime = lifetime ?? TimeSpan.FromHours(12);
        if (_lifetime < TimeSpan.FromMinutes(5) || _lifetime > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Browser sessions must last between 5 minutes and 24 hours.");
        if (maximumSessions < 1 || maximumSessions > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maximumSessions));
        _maximumSessions = maximumSessions;
    }

    /// <summary>Creates a session for an account already authenticated by trusted server-side code.</summary>
    public IssuedBrowserAccountSession Issue(Guid accountId)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account ID must not be empty.", nameof(accountId));
        RemoveExpired();
        if (_sessions.Count >= _maximumSessions)
            throw new InvalidOperationException("The browser session capacity has been reached.");

        var now = _timeProvider.GetUtcNow();
        var sessionToken = CreateToken();
        var csrfToken = CreateToken();
        var key = HashToken(sessionToken);
        var info = new BrowserAccountSessionInfo(accountId, now, now + _lifetime);
        var stored = new StoredSession(accountId, SHA256.HashData(Encoding.UTF8.GetBytes(csrfToken)), info.CreatedAtUtc, info.ExpiresAtUtc);
        if (!_sessions.TryAdd(key, stored))
            throw new CryptographicException("Unable to allocate a unique browser session.");
        return new IssuedBrowserAccountSession(sessionToken, csrfToken, info);
    }

    /// <summary>Resolves a browser bearer token to its server-owned account session.</summary>
    public bool TryGet(string? sessionToken, out BrowserAccountSessionInfo? info)
    {
        info = null;
        if (!IsTokenShapeValid(sessionToken)) return false;
        var key = HashToken(sessionToken!);
        if (!_sessions.TryGetValue(key, out var stored)) return false;
        if (stored.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _sessions.TryRemove(key, out _);
            return false;
        }

        info = new BrowserAccountSessionInfo(stored.AccountId, stored.CreatedAtUtc, stored.ExpiresAtUtc);
        return true;
    }

    /// <summary>Validates the anti-CSRF token paired with the authenticated browser session.</summary>
    public bool ValidateCsrf(string? sessionToken, string? csrfToken)
    {
        if (!IsTokenShapeValid(sessionToken) || !IsTokenShapeValid(csrfToken)) return false;
        var key = HashToken(sessionToken!);
        if (!_sessions.TryGetValue(key, out var stored) || stored.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _sessions.TryRemove(key, out _);
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(csrfToken!));
        return CryptographicOperations.FixedTimeEquals(stored.CsrfTokenHash, suppliedHash);
    }

    /// <summary>Revokes only the session represented by the supplied bearer token.</summary>
    public bool Revoke(string? sessionToken)
    {
        if (!IsTokenShapeValid(sessionToken)) return false;
        return _sessions.TryRemove(HashToken(sessionToken!), out _);
    }

    public int RemoveExpired()
    {
        var now = _timeProvider.GetUtcNow();
        var removed = 0;
        foreach (var pair in _sessions)
        {
            if (pair.Value.ExpiresAtUtc <= now && _sessions.TryRemove(pair.Key, out _))
                removed++;
        }
        return removed;
    }

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool IsTokenShapeValid(string? token) =>
        token is { Length: 43 } && token.All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
}
