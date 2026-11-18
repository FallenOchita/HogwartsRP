using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using HogwartsRP.Core;
using HogwartsRP.Networking.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HogwartsRP.Server;

/// <summary>
/// Initial server transport. Remote listeners require HTTPS; plain HTTP is allowed only on loopback.
/// Authentication is delegated to a trusted credential validator and client events are allowlisted.
/// </summary>
public sealed class WebSocketNetworkingAdapter : INetworkingAdapter
{
    private const int MaxMessageBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _listenUrl;
    private readonly IPlayerCredentialValidator _credentials;
    private readonly IJoinTicketExchange? _ticketExchange;
    private readonly NetworkEventPolicy _eventPolicy;
    private readonly PlayerSessionRegistry _sessions;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new(StringComparer.Ordinal);
    private WebApplication? _application;

    public WebSocketNetworkingAdapter(
        string listenUrl,
        IPlayerCredentialValidator credentials,
        NetworkEventPolicy eventPolicy,
        PlayerSessionRegistry sessions,
        ILoggerFactory loggerFactory,
        IJoinTicketExchange? ticketExchange = null)
    {
        _listenUrl = ValidateListenUrl(listenUrl);
        _credentials = credentials;
        _ticketExchange = ticketExchange;
        _eventPolicy = eventPolicy;
        _sessions = sessions;
        _logger = loggerFactory.CreateLogger<WebSocketNetworkingAdapter>();
    }

    public IReadOnlyCollection<PlayerSession> ConnectedPlayers => _sessions.ConnectedPlayers;

    public event Func<ValidatedNetworkEvent, ValueTask>? ServerEventReceived;
    public event Action<PlayerSession>? PlayerConnected;
    public event Action<PlayerSession>? PlayerDisconnected;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null) throw new InvalidOperationException("The networking adapter has already been started.");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(_listenUrl);
        var application = builder.Build();
        application.UseWebSockets();
        application.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        application.MapGet("/auth/oidc/challenge", (Delegate)HandleOidcChallengeAsync);
        application.MapPost("/auth/tickets", (Delegate)HandleOidcTicketExchangeAsync);
        application.MapGet("/connect", HandleConnectionAsync);
        _application = application;
        try
        {
            await application.StartAsync(cancellationToken);
            _logger.LogInformation("HogwartsRP WebSocket endpoint listening at {ListenUrl}/connect", _listenUrl.TrimEnd('/'));
        }
        catch
        {
            _application = null;
            await application.DisposeAsync();
            throw;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        var application = _application;
        if (application is null) return;
        _application = null;
        await application.StopAsync(cancellationToken);
        await application.DisposeAsync();
    }

    public async ValueTask SendToClientAsync(
        string playerId,
        string eventName,
        IReadOnlyList<object?> arguments,
        CancellationToken cancellationToken = default)
    {
        NetworkEventPolicy.ValidateEventName(eventName);
        var safeArguments = NetworkEventPolicy.ValidateArguments(arguments);
        if (!_clients.TryGetValue(playerId, out var client))
            throw new KeyNotFoundException("No authenticated player with that server session ID is connected.");
        await SendMessageAsync(client, new WireMessage("event", Name: eventName, Arguments: safeArguments), cancellationToken);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task<IResult> HandleOidcChallengeAsync(HttpContext context)
    {
        SetNoStoreHeaders(context);
        if (_ticketExchange is null) return Results.Problem("OIDC sign-in is not configured on this server.", statusCode: StatusCodes.Status503ServiceUnavailable);
        try
        {
            var challenge = await _ticketExchange.CreateChallengeAsync(context.RequestAborted);
            return Results.Json(challenge);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning("Could not create an OIDC challenge: {Message}", exception.Message);
            return Results.Problem("The server cannot create a sign-in challenge right now.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private async Task<IResult> HandleOidcTicketExchangeAsync(HttpContext context)
    {
        SetNoStoreHeaders(context);
        if (_ticketExchange is null) return Results.Problem("OIDC sign-in is not configured on this server.", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (context.Request.ContentLength is > 20_000)
            return Results.Problem("The sign-in request is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);

        OidcJoinTicketRequest? request;
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
                if (read == 0) break;
                if (body.Length + read > 20_000)
                    return Results.Problem("The sign-in request is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);
                body.Write(buffer, 0, read);
            }
            request = JsonSerializer.Deserialize<OidcJoinTicketRequest>(body.ToArray(), JsonOptions);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid sign-in request." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.IdToken))
            return Results.BadRequest(new { error = "A challenge ID and OIDC ID token are required." });

        try
        {
            var ticket = await _ticketExchange.ExchangeAsync(request, context.RequestAborted);
            return ticket is null
                ? Results.Unauthorized()
                : Results.Json(ticket);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            _logger.LogWarning("OIDC provider validation is unavailable: {Message}", exception.Message);
            return Results.Problem("The identity provider could not be reached to validate sign-in.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning("OIDC provider configuration is invalid: {Message}", exception.Message);
            return Results.Problem("The server's identity provider configuration is invalid.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static void SetNoStoreHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    private async Task HandleConnectionAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.", context.RequestAborted);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        ClientConnection? client = null;
        var sessionRegistered = false;
        try
        {
            var firstMessage = await ReceiveTextMessageAsync(socket, context.RequestAborted);
            if (firstMessage is null) return;
            var authentication = JsonSerializer.Deserialize<WireMessage>(firstMessage, JsonOptions);
            if (authentication?.Type != "authenticate" || string.IsNullOrWhiteSpace(authentication.Credential))
            {
                await RejectAsync(socket, "Authentication must be the first WebSocket message.", WebSocketCloseStatus.PolicyViolation);
                return;
            }

            var identity = await _credentials.ValidateCredentialAsync(authentication.Credential, context.RequestAborted);
            if (identity is null)
            {
                await RejectAsync(socket, "Join credential is invalid, expired, or already used.", WebSocketCloseStatus.PolicyViolation);
                return;
            }

            if (!_sessions.TryCreate(identity, out var session, out var failure) || session is null)
            {
                var reason = failure switch
                {
                    SessionCreateFailure.ServerFull => "This server is full.",
                    SessionCreateFailure.AccountAlreadyConnected => "This account already has an active session.",
                    _ => "Unable to allocate a server session."
                };
                await RejectAsync(socket, reason, WebSocketCloseStatus.PolicyViolation);
                return;
            }
            client = new(session, socket);
            if (!_clients.TryAdd(session.Id, client))
            {
                _sessions.Remove(session.Id);
                await RejectAsync(socket, "Unable to allocate a server session.", WebSocketCloseStatus.InternalServerError);
                return;
            }
            sessionRegistered = true;

            await SendMessageAsync(client, new WireMessage("connected", PlayerId: session.Id, DisplayName: session.DisplayName), context.RequestAborted);
            RaisePlayerChanged(PlayerConnected, session);
            await ReadClientMessagesAsync(client, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (WebSocketException) { }
        catch (Exception exception)
        {
            _logger.LogWarning("WebSocket connection failed: {Message}", exception.Message);
            if (socket.State == WebSocketState.Open)
            {
                try { await SendMessageAsync(socket, new WireMessage("error", Error: "Connection protocol error."), CancellationToken.None); }
                catch (WebSocketException) { }
            }
        }
        finally
        {
            if (client is not null && sessionRegistered)
            {
                _clients.TryRemove(client.Session.Id, out _);
                _sessions.Remove(client.Session.Id);
                client.Dispose();
                RaisePlayerChanged(PlayerDisconnected, client.Session);
            }
            else client?.Dispose();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed.", CancellationToken.None); }
                catch (WebSocketException) { }
            }
        }
    }

    private async Task ReadClientMessagesAsync(ClientConnection client, CancellationToken cancellationToken)
    {
        while (client.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var bytes = await ReceiveTextMessageAsync(client.Socket, cancellationToken);
            if (bytes is null) return;
            var message = JsonSerializer.Deserialize<WireMessage>(bytes, JsonOptions);
            if (message?.Type != "event" || string.IsNullOrWhiteSpace(message.Name))
            {
                await RejectAsync(client.Socket, "Expected a client event message.", WebSocketCloseStatus.ProtocolError);
                return;
            }

            try
            {
                NetworkEventPolicy.ValidateEventName(message.Name);
                if (!_eventPolicy.IsClientEventAllowed(message.Name))
                {
                    await SendMessageAsync(client, new WireMessage("event_rejected", Name: message.Name, Error: "The server has not registered this client event."), cancellationToken);
                    continue;
                }

                var arguments = NetworkEventPolicy.ValidateArguments((message.Arguments ?? []).Select(ConvertJsonValue));
                var received = new ValidatedNetworkEvent(message.Name, client.Session, arguments);
                try { await RaiseServerEventAsync(received); }
                catch (Exception exception)
                {
                    _logger.LogWarning("Server event handler failed for {EventName}: {Message}", message.Name, exception.Message);
                    await SendMessageAsync(client, new WireMessage("event_error", Name: message.Name, Error: "The server could not process this event."), cancellationToken);
                }
            }
            catch (ArgumentException exception)
            {
                await SendMessageAsync(client, new WireMessage("event_rejected", Name: message.Name, Error: exception.Message), cancellationToken);
            }
        }
    }

    private async ValueTask RaiseServerEventAsync(ValidatedNetworkEvent networkEvent)
    {
        var handlers = ServerEventReceived;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<ValidatedNetworkEvent, ValueTask>>())
            await handler(networkEvent);
    }

    private static object? ConvertJsonValue(object? value)
    {
        if (value is not JsonElement element) return value;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.Null => null,
            _ => throw new ArgumentException("Network event arguments must be primitive JSON values.")
        };
    }

    private static async ValueTask<byte[]?> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4 * 1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Only UTF-8 JSON text messages are supported.");
            if (message.Length + result.Count > MaxMessageBytes)
                throw new InvalidDataException("WebSocket message exceeds the 32 KiB limit.");
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return message.ToArray();
    }

    private static async ValueTask SendMessageAsync(ClientConnection client, object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > MaxMessageBytes) throw new InvalidOperationException("Outgoing network event exceeds the 32 KiB limit.");
        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            await client.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally { client.SendLock.Release(); }
    }

    private static async ValueTask SendMessageAsync(WebSocket socket, object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task RejectAsync(WebSocket socket, string message, WebSocketCloseStatus status)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            await SendMessageAsync(socket, new WireMessage("error", Error: message), CancellationToken.None);
            await socket.CloseAsync(status, "Request rejected.", CancellationToken.None);
        }
        catch (WebSocketException) { }
    }

    private static void RaisePlayerChanged(Action<PlayerSession>? handlers, PlayerSession session)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<PlayerSession>>())
        {
            try { handler(session); }
            catch { /* Observers cannot interrupt connection cleanup. */ }
        }
    }

    private static string ValidateListenUrl(string listenUrl)
    {
        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Networking:ListenUrl must be an absolute HTTP or HTTPS URL.", nameof(listenUrl));
        if (!string.IsNullOrEmpty(uri.Query) || uri.AbsolutePath != "/")
            throw new ArgumentException("Networking:ListenUrl must not include a path or query string.", nameof(listenUrl));
        if (uri.Scheme == "http" && !uri.IsLoopback)
            throw new ArgumentException("Remote WebSocket listeners require HTTPS. Plain HTTP is allowed only on loopback.", nameof(listenUrl));
        return uri.ToString().TrimEnd('/');
    }

    private sealed record WireMessage(
        string Type,
        string? Credential = null,
        string? Name = null,
        object?[]? Arguments = null,
        string? PlayerId = null,
        string? DisplayName = null,
        string? Error = null);

    private sealed class ClientConnection(PlayerSession session, WebSocket socket) : IDisposable
    {
        public PlayerSession Session { get; } = session;
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public void Dispose() => SendLock.Dispose();
    }
}
