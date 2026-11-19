using System.Net;
using System.Text;
using HogwartsRP.Server.Accounts;

namespace HogwartsRP.Server.Tests;

public sealed class EpicGamesAccountLinkClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HogwartsRP-EpicLink-" + Guid.NewGuid().ToString("N"));
    private readonly JsonAccountIdentityStore _store;
    private readonly Guid _accountId;
    private readonly Uri _callback = new("https://accounts.example.test/account/link/epic/callback");

    public EpicGamesAccountLinkClientTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new JsonAccountIdentityStore(Path.Combine(_directory, "accounts.json"));
        _accountId = _store.CreateAccountAsync("Test Player").AsTask().GetAwaiter().GetResult().Id;
    }

    [Fact]
    public async Task ExchangesCodeAndLinksOnlyEpicVerifiedAccountId()
    {
        var handler = new QueueHandler(
            _ => Task.FromResult(JsonResponse("""{"access_token":"epic-access-token","token_type":"bearer","expires_in":3600}""")),
            _ => Task.FromResult(JsonResponse("""{"active":true,"token_type":"bearer","client_id":"test-client","account_id":"0123456789abcdef0123456789abcdef"}""")));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var link = await client.ExchangeAndLinkAsync(CreateTransaction(), "authorization-code");

        Assert.Equal(_accountId, link.AccountId);
        Assert.Equal(ExternalIdentityProvider.EpicGames, link.Provider);
        Assert.Equal("0123456789abcdef0123456789abcdef", link.ProviderSubject);
        Assert.Null(link.ProviderDisplayName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.epicgames.dev/epic/oauth/v2/token", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("Basic", handler.Requests[0].Headers.Authorization!.Scheme);
        var tokenForm = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("grant_type=authorization_code", tokenForm);
        Assert.Contains("deployment_id=test-deployment", tokenForm);
        Assert.Contains("scope=basic_profile", tokenForm);
        Assert.Contains("code=authorization-code", tokenForm);
        Assert.Contains("redirect_uri=https%3A%2F%2Faccounts.example.test%2Faccount%2Flink%2Fepic%2Fcallback", tokenForm);

        Assert.Equal("https://api.epicgames.dev/epic/oauth/v2/tokenInfo", handler.Requests[1].RequestUri!.AbsoluteUri);
        var verifyForm = await handler.Requests[1].Content!.ReadAsStringAsync();
        Assert.Contains("token=epic-access-token", verifyForm);
    }

    [Fact]
    public async Task DoesNotLinkWhenEpicReportsInactiveToken()
    {
        var handler = new QueueHandler(
            _ => Task.FromResult(JsonResponse("""{"access_token":"epic-access-token","token_type":"bearer"}""")),
            _ => Task.FromResult(JsonResponse("""{"active":false,"token_type":"bearer","client_id":"test-client","account_id":"0123456789abcdef0123456789abcdef"}""")));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "authorization-code").AsTask());

        Assert.Empty(await _store.GetLinksAsync(_accountId));
    }

    [Fact]
    public async Task RejectsTokenIssuedForDifferentOAuthClient()
    {
        var handler = new QueueHandler(
            _ => Task.FromResult(JsonResponse("""{"access_token":"epic-access-token","token_type":"bearer"}""")),
            _ => Task.FromResult(JsonResponse("""{"active":true,"token_type":"bearer","client_id":"other-client","account_id":"0123456789abcdef0123456789abcdef"}""")));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "authorization-code").AsTask());

        Assert.Empty(await _store.GetLinksAsync(_accountId));
    }

    [Fact]
    public async Task RejectsMalformedTokenResponseBeforeCallingIntrospection()
    {
        var handler = new QueueHandler(
            _ => Task.FromResult(JsonResponse("""{"access_token":"epic-access-token","token_type":"mac"}""")));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "authorization-code").AsTask());

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RejectsMalformedEpicAccountId()
    {
        var handler = new QueueHandler(
            _ => Task.FromResult(JsonResponse("""{"access_token":"epic-access-token","token_type":"bearer"}""")),
            _ => Task.FromResult(JsonResponse("""{"active":true,"token_type":"bearer","client_id":"test-client","account_id":"bad account id"}""")));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "authorization-code").AsTask());

        Assert.Empty(await _store.GetLinksAsync(_accountId));
    }

    [Fact]
    public async Task RejectsWrongProviderAndPkceVerifier()
    {
        var handler = new QueueHandler(_ => Task.FromResult(JsonResponse("{}")));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ExchangeAndLinkAsync(
                CreateTransaction(ExternalIdentityProvider.Discord), "authorization-code").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ExchangeAndLinkAsync(
                CreateTransaction(ExternalIdentityProvider.EpicGames, "unexpected-verifier"), "authorization-code").AsTask());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RejectsEmptyAuthorizationCode()
    {
        var handler = new QueueHandler(_ => Task.FromResult(JsonResponse("{}")));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), " ").AsTask());

        Assert.Empty(handler.Requests);
    }

    private EpicGamesAccountLinkClient CreateClient(HttpClient http) =>
        new(http, _store, new EpicGamesAccountLinkOptions("test-client", "test-secret", "test-deployment"));

    private AccountLinkAuthorizationTransaction CreateTransaction(
        ExternalIdentityProvider provider = ExternalIdentityProvider.EpicGames,
        string? verifier = null) =>
        new(_accountId, provider, _callback, verifier,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class QueueHandler(params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] responders)
        : HttpMessageHandler
    {
        private int _index;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Headers = { Authorization = request.Headers.Authorization },
                Content = request.Content is null
                    ? null
                    : new StringContent(await request.Content.ReadAsStringAsync(cancellationToken))
            });
            if (_index >= responders.Length) throw new InvalidOperationException("No fake Epic response was configured.");
            return await responders[_index++](request);
        }
    }
}
