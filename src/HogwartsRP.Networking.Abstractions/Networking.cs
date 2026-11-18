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
public sealed record ServerNetworkEvent(string Name, IReadOnlyList<object?> Arguments);

/// <summary>Validates short-lived credentials issued after an upstream identity check.</summary>
public interface IPlayerCredentialValidator
{
    ValueTask<AuthenticatedPlayer?> ValidateCredentialAsync(string credential, CancellationToken cancellationToken = default);
}

/// <summary>Client-side protocol contract implemented by a replaceable transport adapter.</summary>
public interface IPlayerNetworkClientAdapter : IAsyncDisposable
{
    PlayerSession? Session { get; }
    bool IsConnected { get; }
    ValueTask<PlayerSession> ConnectAsync(Uri serverEndpoint, string credential, CancellationToken cancellationToken = default);
    ValueTask SendEventAsync(string eventName, IReadOnlyList<object?> arguments, CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

    event Action<ServerNetworkEvent>? ServerEventReceived;
    event Action<string>? StatusMessage;
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
