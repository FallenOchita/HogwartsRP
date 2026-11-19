using System.Net;
using System.Text;
using HogwartsRP.Server.Accounts;

namespace HogwartsRP.Server.Tests;

public sealed class DiscordAccountLinkClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HogwartsRP-DiscordLink-" + Guid.NewGuid().ToString("N"));
    private readonly JsonAccountIdentityStore _store;
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Uri _callback = new("https://accounts.example.test/account/link/discord/callback");
    private readonly string _verifier = new string('v', 64);

    public DiscordAccountLinkClientTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new JsonAccountIdentityStore(Path.Combine(_directory, "accounts.json"));
        _store.CreateAccountAsync("Test Player").AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ExchangesCodeWithPkceAndLinksOnlyDiscordVerifiedSubject()
    {
        var handler = new QueueHandler(
            (request, _) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://discord.com/api/oauth2/token", request.RequestUri!.AbsoluteUri);
                return Task.FromResult(JsonResponse("""{"access_token":"test-access-token","token_type":"Bearer","expires_in":604800}"""));
            },
            (request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://discord.com/api/users/@me", request.RequestUri!.AbsoluteUri);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal("test-access-token", request.Headers.Authorization.Parameter);
                return Task.FromResult(JsonResponse("""{"id":"123456789012345678","username":"wizard","global_name":"Wizard Player"}"""));
            });

        using var http = new HttpClient(handler);
        var client = new DiscordAccountLinkClient(http, _store, new DiscordAccountLinkOptions("client-id", "client-secret"));
        var transaction = CreateTransaction();

        var link = await client.ExchangeAndLinkAsync(transaction, "authorization-code");

        Assert.Equal(_accountId, link.AccountId);
        Assert.Equal(ExternalIdentityProvider.Discord, link.Provider);
        Assert.Equal("123456789012345678", link.ProviderSubject);
        Assert.Equal("Wizard Player", link.ProviderDisplayName);
        Assert.Equal(2, handler.Requests.Count);
        var form = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("grant_type=authorization_code", form);
        Assert.Contains("code_verifier=" + Uri.EscapeDataString(_verifier), form);
        Assert.Contains("redirect_uri=" + Uri.EscapeDataString(_callback.AbsoluteUri), form);
        Assert.Contains("client_secret=client-secret", form);
    }

    [Fact]
    public async Task RejectsWrongProviderAndMissingPkceVerifierBeforeNetworkCalls()
    {
        var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        var client = new DiscordAccountLinkClient(http, _store, new DiscordAccountLinkOptions("client-id", "client-secret"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(ExternalIdentityProvider.Steam), "code").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(verifier: null), "code").AsTask());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DoesNotPersistIdentityWhenTokenExchangeFails()
    {
        var handler = new QueueHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var http = new HttpClient(handler);
        var client = new DiscordAccountLinkClient(http, _store, new DiscordAccountLinkOptions("client-id", "client-secret"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "bad-code").AsTask());

        Assert.Empty(await _store.GetLinksAsync(_accountId));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DoesNotPersistIdentityWhenDiscordCurrentUserResponseIsInvalid()
    {
        var handler = new QueueHandler(
            (_, _) => Task.FromResult(JsonResponse("""{"access_token":"test-access-token","token_type":"Bearer"}""")),
            (_, _) => Task.FromResult(JsonResponse("""{"username":"missing-id"}""")));
        using var http = new HttpClient(handler);
        var client = new DiscordAccountLinkClient(http, _store, new DiscordAccountLinkOptions("client-id", "client-secret"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "code").AsTask());

        Assert.Empty(await _store.GetLinksAsync(_accountId));
    }

    [Fact]
    public async Task DoesNotLinkDiscordIdentityAlreadyOwnedByAnotherAccount()
    {
        var other = await _store.CreateAccountAsync("Other Player");
        var handler = new QueueHandler(
            (_, _) => Task.FromResult(JsonResponse("""{"access_token":"test-access-token","token_type":"Bearer"}""")),
            (_, _) => Task.FromResult(JsonResponse("""{"id":"123456789012345678","username":"wizard"}""")));
        using var http = new HttpClient(handler);
        var client = new DiscordAccountLinkClient(http, _store, new DiscordAccountLinkOptions("client-id", "client-secret"));

        await client.ExchangeAndLinkAsync(CreateTransaction(other.Id), "code");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "code").AsTask());
        Assert.Single(await _store.GetLinksAsync(other.Id));
        Assert.Empty(await _store.GetLinksAsync(_accountId));
    }

    [Fact]
    public async Task RejectsInvalidAuthorizationCodeBeforeNetworkCalls()
    {
        var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        var client = new DiscordAccountLinkClient(http, _store, new DiscordAccountLinkOptions("client-id", "client-secret"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ExchangeAndLinkAsync(CreateTransaction(), "invalid\ncode").AsTask());
        Assert.Empty(handler.Requests);
    }

    private AccountLinkAuthorizationTransaction CreateTransaction(
        ExternalIdentityProvider provider = ExternalIdentityProvider.Discord,
        Guid? accountId = null,
        string? verifier = "default") =>
        new(accountId ?? _accountId, provider, _callback, verifier is null ? null : _verifier,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class QueueHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responders) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responders = new(responders);
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = request.Content is null ? null : new StringContent(await request.Content.ReadAsStringAsync(cancellationToken))
            });
            if (_responders.Count == 0) throw new InvalidOperationException("Unexpected HTTP request.");
            return await _responders.Dequeue()(request, cancellationToken);
        }
    }
}
