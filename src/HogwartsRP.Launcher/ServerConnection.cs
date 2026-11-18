using HogwartsRP.GameIntegration.Abstractions;
using HogwartsRP.Networking.Abstractions;

namespace HogwartsRP.Launcher;

/// <summary>Obtains a one-time connection credential from an account or ticket provider.</summary>
public interface IJoinCredentialProvider
{
    ValueTask<string> GetJoinCredentialAsync(ServerSelection server, CancellationToken cancellationToken = default);
}

/// <summary>Coordinates server selection, credential acquisition, and the replaceable network client.</summary>
public sealed class ServerConnectionService(IPlayerNetworkClientAdapter client, IJoinCredentialProvider credentialProvider)
{
    public PlayerSession? Session => client.Session;
    public bool IsConnected => client.IsConnected;

    public event Action<ServerNetworkEvent>? ServerEventReceived
    {
        add => client.ServerEventReceived += value;
        remove => client.ServerEventReceived -= value;
    }

    public event Action<string>? StatusMessage
    {
        add => client.StatusMessage += value;
        remove => client.StatusMessage -= value;
    }

    public async ValueTask<PlayerSession> ConnectAsync(ServerSelection server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!IsAllowedEndpoint(server.Endpoint))
            throw new ArgumentException("Server endpoint must use HTTPS/WSS, or HTTP/WS on loopback for local development.", nameof(server));

        var credential = await credentialProvider.GetJoinCredentialAsync(server, cancellationToken);
        if (string.IsNullOrWhiteSpace(credential)) throw new UnauthorizedAccessException("No join credential was supplied.");
        return await client.ConnectAsync(server.Endpoint, credential, cancellationToken);
    }

    public ValueTask SendEventAsync(string eventName, IReadOnlyList<object?> arguments, CancellationToken cancellationToken = default) =>
        client.SendEventAsync(eventName, arguments, cancellationToken);

    /// <summary>Submits a validated roleplay-character draft to the authenticated server session.</summary>
    public ValueTask<CharacterSubmissionResult> SubmitCharacterAsync(CharacterSubmission submission, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Connect to a server before submitting a character.");
        ArgumentNullException.ThrowIfNull(submission);
        return client.SubmitCharacterAsync(submission, cancellationToken);
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) =>
        client.DisconnectAsync(cancellationToken);

    private static bool IsAllowedEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri) return false;
        if (endpoint.Scheme is "https" or "wss") return true;
        return endpoint.IsLoopback && (endpoint.Scheme is "http" or "ws");
    }
}
