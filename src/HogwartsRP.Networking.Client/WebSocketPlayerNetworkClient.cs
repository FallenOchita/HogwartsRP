using System.Collections.Concurrent;
using System.Net.WebSockets;
using HogwartsRP.GameIntegration.Abstractions;
using System.Text.Json;
using HogwartsRP.Networking.Abstractions;

namespace HogwartsRP.Networking.Client;

/// <summary>Client for the HogwartsRP JSON WebSocket protocol. Authentication remains an injected credential.</summary>
public sealed class WebSocketPlayerNetworkClient : IPlayerNetworkClientAdapter
{
    private const int MaxMessageBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CharacterSubmissionResult>> _pendingSubmissions = new(StringComparer.Ordinal);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveLifetime;
    private Task? _receiveLoop;

    public PlayerSession? Session { get; private set; }
    public bool IsConnected => Volatile.Read(ref _socket)?.State == WebSocketState.Open;

    public event Action<ServerNetworkEvent>? ServerEventReceived;
    public event Action<string>? StatusMessage;

    public async ValueTask<PlayerSession> ConnectAsync(Uri serverEndpoint, string credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverEndpoint);
        if (string.IsNullOrWhiteSpace(credential)) throw new ArgumentException("A join credential is required.", nameof(credential));

        await _lifecycle.WaitAsync(cancellationToken);
        ClientWebSocket? socket = null;
        try
        {
            if (IsConnected) throw new InvalidOperationException("This client is already connected.");
            var endpoint = BuildConnectUri(serverEndpoint);
            socket = new ClientWebSocket();
            await socket.ConnectAsync(endpoint, cancellationToken);
            await SendMessageAsync(socket, new WireMessage("authenticate", Credential: credential), cancellationToken);

            var responseBytes = await ReceiveTextMessageAsync(socket, cancellationToken);
            if (responseBytes is null) throw new InvalidOperationException("The server closed before completing authentication.");
            var response = JsonSerializer.Deserialize<WireMessage>(responseBytes, JsonOptions)
                ?? throw new InvalidDataException("The server returned an empty authentication response.");
            if (response.Type == "error")
                throw new UnauthorizedAccessException(response.Error ?? "The server rejected the join credential.");
            if (response.Type != "connected" || string.IsNullOrWhiteSpace(response.PlayerId) ||
                !Guid.TryParseExact(response.PlayerId, "N", out _) ||
                string.IsNullOrWhiteSpace(response.DisplayName) || response.DisplayName.Length > 64)
                throw new InvalidDataException("The server returned an invalid session handshake.");

            Session = new PlayerSession(response.PlayerId, response.DisplayName);
            _socket = socket;
            _receiveLifetime = new CancellationTokenSource();
            _receiveLoop = ReceiveLoopAsync(socket, _receiveLifetime.Token);
            RaiseStatus("Connected to HogwartsRP server.");
            return Session;
        }
        catch
        {
            if (socket is not null)
            {
                _socket = null;
                Session = null;
                socket.Dispose();
            }
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask SendEventAsync(string eventName, IReadOnlyList<object?> arguments, CancellationToken cancellationToken = default)
    {
        ValidateEventName(eventName);
        var safeArguments = ValidateArguments(arguments);
        var socket = Volatile.Read(ref _socket);
        if (socket?.State != WebSocketState.Open) throw new InvalidOperationException("The client is not connected.");

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await SendMessageAsync(socket, new WireMessage("event", Name: eventName, Arguments: safeArguments), cancellationToken);
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>Submits a character draft and awaits its correlated server-side validation/persistence result.</summary>
    public async ValueTask<CharacterSubmissionResult> SubmitCharacterAsync(CharacterSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var socket = Volatile.Read(ref _socket);
        if (socket?.State != WebSocketState.Open) throw new InvalidOperationException("The client is not connected.");

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<CharacterSubmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingSubmissions.TryAdd(requestId, completion))
            throw new InvalidOperationException("Could not allocate a character submission request.");

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (socket.State != WebSocketState.Open) throw new InvalidOperationException("The client is no longer connected.");
                await SendMessageAsync(socket, new WireMessage("submit_character", RequestId: requestId, Submission: submission), cancellationToken);
            }
            finally { _sendLock.Release(); }

            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pendingSubmissions.TryRemove(requestId, out _);
        }
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            var socket = Interlocked.Exchange(ref _socket, null);
            var receiveLifetime = Interlocked.Exchange(ref _receiveLifetime, null);
            var receiveLoop = _receiveLoop;
            _receiveLoop = null;
            Session = null;
            if (socket is null) return;

            receiveLifetime?.Cancel();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Launcher disconnected.", cancellationToken); }
                catch (WebSocketException) { }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            }
            if (receiveLoop is not null)
                await Task.WhenAny(receiveLoop, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            socket.Dispose();
            receiveLifetime?.Dispose();
            RaiseStatus("Disconnected from HogwartsRP server.");
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _lifecycle.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var bytes = await ReceiveTextMessageAsync(socket, cancellationToken);
                if (bytes is null) break;
                var message = JsonSerializer.Deserialize<WireMessage>(bytes, JsonOptions)
                    ?? throw new InvalidDataException("The server returned an empty network message.");
                if (message.Type == "character_submission_result")
                {
                    if (string.IsNullOrWhiteSpace(message.RequestId) || message.RequestId.Length > 64 ||
                        !_pendingSubmissions.TryRemove(message.RequestId, out var pending))
                    {
                        RaiseStatus("Ignored an unmatched character submission response.");
                        continue;
                    }
                    if (message.SubmissionResult is null)
                        pending.TrySetException(new InvalidDataException("The server returned an empty character submission result."));
                    else
                        pending.TrySetResult(message.SubmissionResult);
                    continue;
                }
                if (message.Type == "event" && !string.IsNullOrWhiteSpace(message.Name))
                {
                    ValidateEventName(message.Name);
                    RaiseServerEvent(new ServerNetworkEvent(message.Name, ValidateArguments((message.Arguments ?? []).Select(ConvertJsonValue))));
                    continue;
                }
                if (message.Type is "event_rejected" or "event_error" or "error")
                {
                    RaiseStatus(message.Error ?? $"Server response: {message.Type}.");
                    continue;
                }
                throw new InvalidDataException($"Unsupported server message '{message.Type}'.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WebSocketException exception) { RaiseStatus($"Network connection closed: {exception.Message}"); }
        catch (Exception exception) { RaiseStatus($"Network protocol error: {exception.Message}"); }
        finally
        {
            foreach (var pending in _pendingSubmissions.ToArray())
            {
                if (_pendingSubmissions.TryRemove(pending.Key, out var completion))
                    completion.TrySetException(new IOException("The server connection closed before the character submission completed."));
            }
            if (ReferenceEquals(Interlocked.CompareExchange(ref _socket, null, socket), socket))
            {
                Session = null;
                var lifetime = Interlocked.Exchange(ref _receiveLifetime, null);
                lifetime?.Dispose();
                socket.Dispose();
                RaiseStatus("Server connection closed.");
            }
        }
    }

    private static Uri BuildConnectUri(Uri serverEndpoint)
    {
        if (!serverEndpoint.IsAbsoluteUri || !string.IsNullOrEmpty(serverEndpoint.Query) || !string.IsNullOrEmpty(serverEndpoint.Fragment))
            throw new ArgumentException("Server address must be an absolute base URL without a query or fragment.", nameof(serverEndpoint));
        var scheme = serverEndpoint.Scheme switch
        {
            "https" or "wss" => "wss",
            "http" when serverEndpoint.IsLoopback => "ws",
            "ws" when serverEndpoint.IsLoopback => "ws",
            "ws" or "http" => throw new ArgumentException("Remote server connections require HTTPS/WSS.", nameof(serverEndpoint)),
            _ => throw new ArgumentException("Server address must use HTTPS or WSS.", nameof(serverEndpoint))
        };
        if (serverEndpoint.AbsolutePath is not ("/" or "/connect"))
            throw new ArgumentException("Server address must be the server root or /connect endpoint.", nameof(serverEndpoint));
        var builder = new UriBuilder(serverEndpoint)
        {
            Scheme = scheme,
            Path = "/connect",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
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
                throw new InvalidDataException("Server message exceeds the 32 KiB limit.");
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return message.ToArray();
    }

    private static async ValueTask SendMessageAsync(WebSocket socket, object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > MaxMessageBytes) throw new InvalidOperationException("Network message exceeds the 32 KiB limit.");
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
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

    private static object?[] ValidateArguments(IEnumerable<object?> arguments)
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

    private static void ValidateEventName(string eventName)
    {
        if (string.IsNullOrEmpty(eventName) || eventName.Length > 128 || !char.IsAsciiLetter(eventName[0]) ||
            eventName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not ':' and not '-'))
            throw new ArgumentException("Invalid network event name.", nameof(eventName));
    }

    private void RaiseServerEvent(ServerNetworkEvent networkEvent)
    {
        if (ServerEventReceived is null) return;
        foreach (var handler in ServerEventReceived.GetInvocationList().Cast<Action<ServerNetworkEvent>>())
        {
            try { handler(networkEvent); }
            catch { /* Client observers cannot terminate the receive loop. */ }
        }
    }

    private void RaiseStatus(string message)
    {
        if (StatusMessage is null) return;
        foreach (var handler in StatusMessage.GetInvocationList().Cast<Action<string>>())
        {
            try { handler(message); }
            catch { /* Status observers cannot terminate connection cleanup. */ }
        }
    }

    private sealed record WireMessage(
        string Type,
        string? Credential = null,
        string? Name = null,
        object?[]? Arguments = null,
        string? PlayerId = null,
        string? DisplayName = null,
        string? Error = null,
        string? RequestId = null,
        CharacterSubmission? Submission = null,
        CharacterSubmissionResult? SubmissionResult = null);
}
