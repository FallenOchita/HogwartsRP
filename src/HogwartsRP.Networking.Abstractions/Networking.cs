namespace HogwartsRP.Networking.Abstractions;

public sealed record PlayerSession(string Id, string DisplayName);
public sealed record ValidatedNetworkEvent(string Name, PlayerSession Player, IReadOnlyList<object?> Arguments);
public interface INetworkingAdapter
{
    ValueTask SendToClientAsync(string playerId, string eventName, IReadOnlyList<object?> arguments, CancellationToken cancellationToken = default);
    event Func<ValidatedNetworkEvent, ValueTask>? ServerEventReceived;
}
