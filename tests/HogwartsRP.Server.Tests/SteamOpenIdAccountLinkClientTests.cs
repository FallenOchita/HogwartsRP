using System.Net;
using System.Text;
using HogwartsRP.Server.Accounts;

namespace HogwartsRP.Server.Tests;

public sealed class SteamOpenIdAccountLinkClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HogwartsRP-SteamLink-" + Guid.NewGuid().ToString("N"));
    private readonly JsonAccountIdentityStore _store;
    private readonly Guid _accountId;
    private readonly Uri _callback = new("https://accounts.example.test/account/link/steam/callback");
    private readonly string _state = new('s', 43);

    public SteamOpenIdAccountLinkClientTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new JsonAccountIdentityStore(Path.Combine(_directory, "accounts.json"));
        _accountId = _store.CreateAccountAsync("Test Player").AsTask().GetAwaiter().GetResult().Id;
    }

    [Fact]
    public async Task VerifiesAssertionWithSteamBeforeLinkingClaimedSteamId()
    {
        var handler = new QueueHandler(_ => Task.FromResult(TextResponse(
            "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")));
        using var http = new HttpClient(handler);
        var client = new SteamOpenIdAccountLinkClient(http, _store);

        var link = await client.VerifyAndLinkAsync(CreateTransaction(), _state, CreateAssertion());

        Assert.Equal(_accountId, link.AccountId);
        Assert.Equal(ExternalIdentityProvider.Steam, link.Provider);
        Assert.Equal("76561198000000000", link.ProviderSubject);
        Assert.Null(link.ProviderDisplayName);
        Assert.Single(handler.Requests);
        Assert.Equal("https://steamcommunity.com/openid/", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        var form = await handler.Requests[0].Content!.ReadAsStringAsync();
        Assert.Contains("openid.mode=check_authentication", form);
        Assert.Contains("openid.claimed_id=", form);
    }

    [Fact]
    public async Task DoesNotLinkWhenSteamRejectsAssertion()
    {
        var handler = new QueueHandler(_ => Task.FromResult(TextResponse(
            "ns:http://specs.openid.net/auth/2.0\nis_valid:false\n")));
        using var http = new HttpClient(handler);
        var client = new SteamOpenIdAccountLinkClient(http, _store);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.VerifyAndLinkAsync(CreateTransaction(), _state, CreateAssertion()).AsTask());

        Assert.Empty(await _store.GetLinksAsync(_accountId));
    }

    [Fact]
    public async Task RejectsMismatchedReturnToBeforeCallingSteam()
    {
        var handler = new QueueHandler(_ => Task.FromResult(TextResponse(
            "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")));
        using var http = new HttpClient(handler);
        var client = new SteamOpenIdAccountLinkClient(http, _store);
        var assertion = CreateAssertion();
        assertion["openid.return_to"] = "https://attacker.example.test/callback?state=" + _state;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.VerifyAndLinkAsync(CreateTransaction(), _state, assertion).AsTask());

        Assert.Empty(handler.Requests);
        Assert.Empty(await _store.GetLinksAsync(_accountId));
    }

    [Fact]
    public async Task RejectsUnsignedIdentityFieldsBeforeCallingSteam()
    {
        var handler = new QueueHandler(_ => Task.FromResult(TextResponse(
            "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")));
        using var http = new HttpClient(handler);
        var client = new SteamOpenIdAccountLinkClient(http, _store);
        var assertion = CreateAssertion();
        assertion["openid.signed"] = "op_endpoint,return_to,response_nonce,assoc_handle";

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.VerifyAndLinkAsync(CreateTransaction(), _state, assertion).AsTask());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RejectsStaleNonceBeforeCallingSteam()
    {
        var handler = new QueueHandler(_ => Task.FromResult(TextResponse(
            "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")));
        using var http = new HttpClient(handler);
        var client = new SteamOpenIdAccountLinkClient(http, _store);
        var assertion = CreateAssertion();
        assertion["openid.response_nonce"] = "2020-01-01T00:00:00Zold";

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.VerifyAndLinkAsync(CreateTransaction(), _state, assertion).AsTask());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RejectsNonSteamClaimedIdentity()
    {
        var handler = new QueueHandler(_ => Task.FromResult(TextResponse(
            "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")));
        using var http = new HttpClient(handler);
        var client = new SteamOpenIdAccountLinkClient(http, _store);
        var assertion = CreateAssertion();
        assertion["openid.claimed_id"] = "https://steamcommunity.com/openid/id/76561198000000000";
        assertion["openid.identity"] = assertion["openid.claimed_id"];

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.VerifyAndLinkAsync(CreateTransaction(), _state, assertion).AsTask());

        Assert.Empty(handler.Requests);
    }

    private AccountLinkAuthorizationTransaction CreateTransaction() =>
        new(_accountId, ExternalIdentityProvider.Steam, _callback, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

    private Dictionary<string, string> CreateAssertion()
    {
        var claimedId = "http://steamcommunity.com/openid/id/76561198000000000";
        var nonce = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/",
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = _callback.AbsoluteUri + "?state=" + _state,
            ["openid.response_nonce"] = nonce + "test-nonce",
            ["openid.assoc_handle"] = "test-association",
            ["openid.signed"] = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
            ["openid.sig"] = "test-signature"
        };
    }

    private static HttpResponseMessage TextResponse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class QueueHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = request.Content is null
                    ? null
                    : new StringContent(await request.Content.ReadAsStringAsync(cancellationToken))
            });
            return await responder(request);
        }
    }
}
