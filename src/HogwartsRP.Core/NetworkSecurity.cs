using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HogwartsRP.Networking.Abstractions;

namespace HogwartsRP.Core;

/// <summary>
/// Issues single-use connection tickets only for identities already trusted by the host.
/// The ticket itself is opaque and only its SHA-256 digest is retained in memory.
/// </summary>
public sealed class OneTimeJoinTicketStore : IPlayerCredentialValidator
{
    private sealed record Ticket(AuthenticatedPlayer Player, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public OneTimeJoinTicketStore(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public string Issue(AuthenticatedPlayer player, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        var identity = NetworkIdentityRules.Validate(player);
        var duration = lifetime ?? TimeSpan.FromMinutes(2);
        if (duration < TimeSpan.FromSeconds(5) || duration > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Join tickets must expire within five minutes.");

        while (true)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var digest = Digest(token);
            if (_tickets.TryAdd(digest, new(identity, _timeProvider.GetUtcNow() + duration))) return token;
        }
    }

    public ValueTask<AuthenticatedPlayer?> ValidateCredentialAsync(string credential, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 256)
            return ValueTask.FromResult<AuthenticatedPlayer?>(null);

        if (!_tickets.TryRemove(Digest(credential), out var ticket) || ticket.ExpiresAt <= _timeProvider.GetUtcNow())
            return ValueTask.FromResult<AuthenticatedPlayer?>(null);
        return ValueTask.FromResult<AuthenticatedPlayer?>(ticket.Player);
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Server-side allowlist for client-to-server events registered by Lua resources.</summary>
public sealed class NetworkEventPolicy
{
    private static readonly Regex EventNamePattern = new("^[a-zA-Z][a-zA-Z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex ResourceNamePattern = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _owners = new(StringComparer.Ordinal);

    public void RegisterClientEvent(string resourceName, string eventName)
    {
        if (!ResourceNamePattern.IsMatch(resourceName ?? string.Empty))
            throw new ArgumentException("Invalid resource name.", nameof(resourceName));
        ValidateEventName(eventName);
        lock (_gate)
        {
            if (!_owners.TryGetValue(eventName, out var owners))
                _owners[eventName] = owners = new(StringComparer.OrdinalIgnoreCase);
            owners.Add(resourceName!);
        }
    }

    public IReadOnlyList<string> GetOwners(string eventName)
    {
        lock (_gate)
            return _owners.TryGetValue(eventName, out var owners) ? owners.ToArray() : [];
    }

    public bool IsClientEventAllowed(string eventName)
    {
        lock (_gate) return _owners.TryGetValue(eventName, out var owners) && owners.Count > 0;
    }

    public static void ValidateEventName(string eventName)
    {
        if (!EventNamePattern.IsMatch(eventName ?? string.Empty))
            throw new ArgumentException("Event names must start with a letter and contain only letters, numbers, '.', '_', ':' or '-'.", nameof(eventName));
    }

    public static object?[] ValidateArguments(IEnumerable<object?> arguments)
    {
        var values = arguments.ToArray();
        if (values.Length > 32) throw new ArgumentException("Network events may contain at most 32 arguments.", nameof(arguments));

        foreach (var value in values)
        {
            var valid = value switch
            {
                null or bool or byte or short or int or long or float or double or decimal => true,
                string text => text.Length <= 8_192,
                _ => false
            };
            if (!valid) throw new ArgumentException("Network event arguments must be primitive values; strings are limited to 8192 characters.", nameof(arguments));
            if (value is float single && !float.IsFinite(single) || value is double number && !double.IsFinite(number))
                throw new ArgumentException("Network event numbers must be finite.", nameof(arguments));
        }

        return values;
    }
}

internal static class NetworkIdentityRules
{
    public static AuthenticatedPlayer Validate(AuthenticatedPlayer player)
    {
        var accountId = player.AccountId?.Trim();
        var displayName = player.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 128 || accountId.Any(char.IsControl))
            throw new ArgumentException("Authenticated account IDs must be 1 to 128 printable characters.", nameof(player));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 64 || displayName.Any(char.IsControl))
            throw new ArgumentException("Display names must be 1 to 64 printable characters.", nameof(player));
        return new(accountId, displayName);
    }
}

public enum SessionCreateFailure
{
    None,
    ServerFull,
    AccountAlreadyConnected
}

/// <summary>Owns live sessions and assigns session IDs; transports cannot choose player IDs.</summary>
public sealed class PlayerSessionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PlayerSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _accountSessions = new(StringComparer.Ordinal);
    private readonly int _capacity;

    public PlayerSessionRegistry(int capacity = 128)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public IReadOnlyCollection<PlayerSession> ConnectedPlayers
    {
        get { lock (_gate) return _sessions.Values.ToArray(); }
    }

    public bool TryCreate(AuthenticatedPlayer authenticatedPlayer, out PlayerSession? session, out SessionCreateFailure failure)
    {
        var identity = NetworkIdentityRules.Validate(authenticatedPlayer);
        lock (_gate)
        {
            if (_sessions.Count >= _capacity)
            {
                session = null;
                failure = SessionCreateFailure.ServerFull;
                return false;
            }
            if (_accountSessions.ContainsKey(identity.AccountId))
            {
                session = null;
                failure = SessionCreateFailure.AccountAlreadyConnected;
                return false;
            }

            session = new PlayerSession(Guid.NewGuid().ToString("N"), identity.DisplayName)
            {
                AccountId = identity.AccountId,
                ConnectedAt = DateTimeOffset.UtcNow
            };
            _sessions.Add(session.Id, session);
            _accountSessions.Add(identity.AccountId, session.Id);
            failure = SessionCreateFailure.None;
            return true;
        }
    }

    public bool Remove(string sessionId)
    {
        lock (_gate)
        {
            if (!_sessions.Remove(sessionId, out var session)) return false;
            _accountSessions.Remove(session.AccountId);
            return true;
        }
    }
}
