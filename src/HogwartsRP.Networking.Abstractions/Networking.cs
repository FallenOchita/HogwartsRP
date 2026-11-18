namespace HogwartsRP.Networking.Abstractions;

/// <summary>An account identity established by a trusted authentication provider.</summary>
public sealed record AuthenticatedPlayer(string AccountId, string DisplayName);

/// <summary>A server-created identity for one live connection.</summary>
public sealed record PlayerSession(string Id, string DisplayName)
{
    public string AccountId { get; init; } = string.Empty;
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A client event accepted by the server's event policy and tied to its authenticated session.</summary>
public sealed record ValidatedNetworkEvent(string Name, PlayerSession Player, IReadOnlyList<object?> Arguments);

/// <summary>Validates short-lived credentials issued after an upstream identity check.</summary>
public interface IPlayerCredentialValidator
{
    ValueTask<AuthenticatedPlayer?> ValidateCredentialAsync(string credential, CancellationToken cancellationToken = default);
}

/// <summary>Transport-independent multiplayer contract owned by HogwartsRP Core.</summary>
public interface INetworkingAdapter : IAsyncDisposable
{
    IReadOnlyCollection<PlayerSession> ConnectedPlayers { get; }
    ValueTask StartAsync(CancellationToken cancellationToken = default);
    ValueTask StopAsync(CancellationToken cancellationToken = default);
    ValueTask SendToClientAsync(string playerId, string eventName, IReadOnlyList<object?> arguments, CancellationToken cancellationToken = default);

    event Func<ValidatedNetworkEvent, ValueTask>? ServerEventReceived;
    event Action<PlayerSession>? PlayerConnected;
    event Action<PlayerSession>? PlayerDisconnected;
}
