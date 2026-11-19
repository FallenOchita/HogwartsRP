using System.Net.WebSockets;
using System.Text.Json;
using HogwartsRP.GameIntegration.Abstractions;
using HogwartsRP.Networking.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HogwartsRP.Networking.Client.Tests;

public sealed class CharacterSubmissionProtocolTests
{
    [Fact]
    public async Task SubmitCharacterAsync_correlates_concurrent_responses()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, cancellationToken) =>
        {
            var requests = new List<(string RequestId, string Name)>();
            for (var i = 0; i < 2; i++)
            {
                using var request = await ReceiveJsonAsync(socket, cancellationToken);
                requests.Add((
                    request.RootElement.GetProperty("requestId").GetString()!,
                    request.RootElement.GetProperty("submission").GetProperty("name").GetString()!));
            }

            foreach (var request in requests.AsEnumerable().Reverse())
            {
                var character = new RegisteredCharacter(Guid.NewGuid(), "server-owned-account", request.Name, null, "Ravenclaw", null, null, null);
                var response = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "character_submission_result",
                    requestId = request.RequestId,
                    submissionResult = new CharacterSubmissionResult(true, character)
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await socket.SendAsync(response, WebSocketMessageType.Text, true, cancellationToken);
            }
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", cancellationToken);
        });

        await using var client = new WebSocketPlayerNetworkClient();
        await client.ConnectAsync(server.Endpoint, "test-ticket");
        var first = client.SubmitCharacterAsync(NewSubmission("Ada")).AsTask();
        var second = client.SubmitCharacterAsync(NewSubmission("Rowan")).AsTask();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("Ada", results[0].Character?.Name);
        Assert.Equal("Rowan", results[1].Character?.Name);
        Assert.All(results, result => Assert.True(result.Success));
    }

    [Fact]
    public async Task SubmitCharacterAsync_fails_when_server_closes_before_reply()
    {
        await using var server = await TestWebSocketServer.StartAsync(async (socket, cancellationToken) =>
        {
            using var request = await ReceiveJsonAsync(socket, cancellationToken);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "No response", cancellationToken);
        });

        await using var client = new WebSocketPlayerNetworkClient();
        await client.ConnectAsync(server.Endpoint, "test-ticket");

        var exception = await Assert.ThrowsAsync<IOException>(async () =>
            await client.SubmitCharacterAsync(NewSubmission("Ada")).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Contains("connection closed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CharacterSubmission NewSubmission(string name) =>
        new(name, null, "Ravenclaw", null, null, null);

    private static async Task<JsonDocument> ReceiveJsonAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("Client closed before sending the expected message.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(stream.ToArray());
    }
}

internal sealed class TestWebSocketServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    private TestWebSocketServer(WebApplication application, Uri endpoint)
    {
        _application = application;
        Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    public static async Task<TestWebSocketServer> StartAsync(
        Func<WebSocket, CancellationToken, Task> onAuthenticated)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/connect", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var auth = await ReceiveJsonAsync(socket, context.RequestAborted);
            if (auth.RootElement.GetProperty("type").GetString() != "authenticate")
            {
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication required", CancellationToken.None);
                return;
            }

            var handshake = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "connected",
                playerId = Guid.NewGuid().ToString("N"),
                displayName = "Test Player"
            });
            await socket.SendAsync(handshake, WebSocketMessageType.Text, true, context.RequestAborted);
            await onAuthenticated(socket, context.RequestAborted);
        });
        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new TestWebSocketServer(app, new Uri(address));
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("Client closed before authentication.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(stream.ToArray());
    }
}
