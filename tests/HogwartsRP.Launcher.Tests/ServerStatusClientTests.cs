using System.Net;
using System.Text;
using HogwartsRP.Launcher;

namespace HogwartsRP.Launcher.Tests;

public sealed class ServerStatusClientTests
{
    [Fact]
    public async Task GetStatusAsync_RequestsStatusEndpointAndReturnsPlayerCounts()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"status":"online","serverName":"HogwartsRP Test","onlinePlayers":7,"maxPlayers":64,"checkedAtUtc":"2026-11-19T12:00:00Z"}
                """, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        using var client = new ServerStatusClient(http);

        var result = await client.GetStatusAsync(new Uri("https://example.test:7777/custom-path"));

        Assert.Equal("HogwartsRP Test", result.ServerName);
        Assert.Equal(7, result.OnlinePlayers);
        Assert.Equal(64, result.MaxPlayers);
        Assert.Equal("/status", handler.LastRequestUri?.AbsolutePath);
        Assert.Equal("https", handler.LastRequestUri?.Scheme);
    }

    [Fact]
    public async Task GetStatusAsync_RejectsNonHttpAddresses()
    {
        using var client = new ServerStatusClient(new HttpClient(new StubHandler(_ => throw new InvalidOperationException("No request expected."))));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStatusAsync(new Uri("wss://example.test/connect")));
    }

    [Fact]
    public async Task GetStatusAsync_RejectsImpossiblePlayerCounts()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"status":"online","serverName":"HogwartsRP Test","onlinePlayers":9,"maxPlayers":4,"checkedAtUtc":"2026-11-19T12:00:00Z"}
                """, Encoding.UTF8, "application/json")
        });
        using var client = new ServerStatusClient(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetStatusAsync(new Uri("https://example.test")));
    }

    [Fact]
    public async Task GetStatusAsync_PropagatesUnavailableServerResponse()
    {
        using var client = new ServerStatusClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStatusAsync(new Uri("https://example.test")));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
