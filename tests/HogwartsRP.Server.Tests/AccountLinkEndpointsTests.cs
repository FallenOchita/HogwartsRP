using HogwartsRP.Server.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace HogwartsRP.Server.Tests;

public sealed class AccountLinkEndpointsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HogwartsRP-AccountRoutes-" + Guid.NewGuid().ToString("N"));
    private readonly JsonAccountIdentityStore _identities;
    private readonly BrowserAccountSessionStore _sessions = new();
    private readonly AccountLinkAuthorizationStore _transactions = new();
    private readonly IssuedBrowserAccountSession _issued;
    private readonly AccountLinkEndpointOptions _options = new(
        new Uri("https://accounts.example.test/connections"),
        new Uri("https://accounts.example.test/account/links/discord/callback"),
        "discord-client",
        new Uri("https://accounts.example.test/account/links/steam/callback"),
        new Uri("https://accounts.example.test/account/links/epic/callback"),
        "epic-client");

    public AccountLinkEndpointsTests()
    {
        Directory.CreateDirectory(_directory);
        _identities = new JsonAccountIdentityStore(Path.Combine(_directory, "accounts.json"));
        _issued = _sessions.Issue(_identities.CreateAccountAsync("Route Test").AsTask().GetAwaiter().GetResult().Id);
    }

    [Fact]
    public async Task DiscordStartRedirectsWithStateAndS256Challenge()
    {
        var context = CreateContext();
        var result = AccountLinkEndpoints.Start("discord", context, _options, _sessions, _transactions, CreateDiscordClient(), null, null);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        var location = new Uri(context.Response.Headers.Location!);
        Assert.Equal("discord.com", location.Host);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("identify", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(43, query["state"].ToString().Length);
        Assert.Equal(43, query["code_challenge"].ToString().Length);
        Assert.Equal(_options.DiscordCallbackUri.AbsoluteUri, query["redirect_uri"].ToString());
    }

    [Fact]
    public async Task StartRequiresCsrfAndSameOrigin()
    {
        var context = CreateContext();
        context.Request.Headers[BrowserAccountSessionEndpoints.CsrfHeaderName] = "wrong-token";
        var result = AccountLinkEndpoints.Start("discord", context, _options, _sessions, _transactions, CreateDiscordClient(), null, null);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProviderCanRemainUnconfiguredWithoutBlockingAnotherProvider()
    {
        var options = _options with { DiscordClientId = null, EpicClientId = null };
        var steam = new SteamOpenIdAccountLinkClient(new HttpClient(new RejectHandler()), _identities);
        var steamContext = CreateContext();
        var steamResult = AccountLinkEndpoints.Start("steam", steamContext, options, _sessions, _transactions, null, steam, null);
        await steamResult.ExecuteAsync(steamContext);

        Assert.Equal(StatusCodes.Status302Found, steamContext.Response.StatusCode);
        Assert.Contains("steamcommunity.com/openid/login", steamContext.Response.Headers.Location.ToString());

        var discordContext = CreateContext();
        var discordResult = AccountLinkEndpoints.Start("discord", discordContext, options, _sessions, _transactions, null, steam, null);
        await discordResult.ExecuteAsync(discordContext);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, discordContext.Response.StatusCode);
    }

    [Fact]
    public async Task CallbackRejectsMissingOrInvalidStateBeforeProviderExchange()
    {
        var context = CreateContext();
        context.Request.QueryString = new QueryString("?code=not-a-real-code");
        var discord = CreateDiscordClient();

        var result = await AccountLinkEndpoints.CompleteDiscordAsync(
            context, _options, _sessions, _transactions, discord);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnlinkRequiresCsrfAndOnlyRemovesCurrentAccountsProviderLink()
    {
        await _identities.LinkVerifiedIdentityAsync(
            _issued.Info.AccountId, ExternalIdentityProvider.Discord, "verified-discord-subject", "TestUser");
        var otherAccount = await _identities.CreateAccountAsync("Other Account");
        await _identities.LinkVerifiedIdentityAsync(
            otherAccount.Id, ExternalIdentityProvider.Discord, "other-discord-subject", "OtherUser");

        var context = CreateContext();
        var result = await AccountLinkEndpoints.UnlinkAsync("discord", context, _sessions, _identities);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Empty(await _identities.GetLinksAsync(_issued.Info.AccountId));
        Assert.Single(await _identities.GetLinksAsync(otherAccount.Id));
    }

    [Fact]
    public async Task LinkStatusIsScopedToAuthenticatedAccountAndNotCached()
    {
        await _identities.LinkVerifiedIdentityAsync(
            _issued.Info.AccountId, ExternalIdentityProvider.Steam, "76561198000000000");

        var context = CreateContext();
        var result = await AccountLinkEndpoints.GetLinks(context, _sessions, _identities);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("\"provider\":\"steam\"", body, StringComparison.Ordinal);
        Assert.Contains("\"connected\":true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("other-account", body, StringComparison.Ordinal);
    }

    private DiscordAccountLinkClient CreateDiscordClient() =>
        new(new HttpClient(new RejectHandler()), _identities,
            new DiscordAccountLinkOptions("discord-client", "server-secret"));

    private DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddRouting()
            .BuildServiceProvider();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("accounts.example.test");
        context.Request.Headers.Cookie = $"{BrowserAccountSessionEndpoints.CookieName}={_issued.SessionToken}";
        context.Request.Headers.Origin = "https://accounts.example.test";
        context.Request.Headers[BrowserAccountSessionEndpoints.CsrfHeaderName] = _issued.CsrfToken;
        context.Response.Body = new MemoryStream();
        return context;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class RejectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The endpoint test must not contact an external provider.");
    }
}
