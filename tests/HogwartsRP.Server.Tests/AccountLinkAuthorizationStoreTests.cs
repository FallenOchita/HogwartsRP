using System.Security.Cryptography;
using System.Text;
using HogwartsRP.Server.Accounts;

namespace HogwartsRP.Server.Tests;

public sealed class AccountLinkAuthorizationStoreTests
{
    private static readonly Uri Callback = new("https://accounts.example.test/account/link/callback");

    [Fact]
    public void DiscordAuthorizationCodeFlowReceivesS256Pkce()
    {
        var (store, sessions, issued) = CreateSession();
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions);

        Assert.Equal(43, start.State.Length);
        Assert.Equal(43, start.CodeChallenge!.Length);
        Assert.Equal("S256", start.CodeChallengeMethod);
        Assert.DoesNotContain(issued.SessionToken, start.State);
    }

    [Fact]
    public void EpicWebAuthorizationCodeFlowDoesNotCreateUndocumentedPkceValues()
    {
        var (store, sessions, issued) = CreateSession();
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.EpicGames, Callback, sessions);

        Assert.Null(start.CodeChallenge);
        Assert.Null(start.CodeChallengeMethod);
        Assert.True(store.TryConsume(start.State, ExternalIdentityProvider.EpicGames, Callback, issued.SessionToken, sessions, out var transaction));
        Assert.Null(transaction!.CodeVerifier);
    }

    [Fact]
    public void SteamOpenIdTransactionDoesNotCreatePkceValues()
    {
        var (store, sessions, issued) = CreateSession();
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Steam, Callback, sessions);

        Assert.Null(start.CodeChallenge);
        Assert.Null(start.CodeChallengeMethod);
    }

    [Fact]
    public void CallbackConsumesTransactionAndReturnsServerOnlyVerifier()
    {
        var (store, sessions, issued) = CreateSession();
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions);

        Assert.True(store.TryConsume(start.State, ExternalIdentityProvider.Discord, Callback, issued.SessionToken, sessions, out var transaction));
        Assert.NotNull(transaction);
        Assert.Equal(issued.Info.AccountId, transaction.AccountId);
        Assert.Equal(ExternalIdentityProvider.Discord, transaction.Provider);
        Assert.Equal(Callback, transaction.CallbackUri);
        Assert.NotNull(transaction.CodeVerifier);
        Assert.Equal(start.ExpiresAtUtc, transaction.ExpiresAtUtc);
        Assert.False(store.TryConsume(start.State, ExternalIdentityProvider.Discord, Callback, issued.SessionToken, sessions, out _));
    }

    [Fact]
    public void WrongProviderOrCallbackDoesNotConsumeValidTransaction()
    {
        var (store, sessions, issued) = CreateSession();
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions);

        Assert.False(store.TryConsume(start.State, ExternalIdentityProvider.Steam, Callback, issued.SessionToken, sessions, out _));
        Assert.False(store.TryConsume(start.State, ExternalIdentityProvider.Discord,
            new Uri("https://accounts.example.test/other/callback"), issued.SessionToken, sessions, out _));
        Assert.True(store.TryConsume(start.State, ExternalIdentityProvider.Discord, Callback, issued.SessionToken, sessions, out _));
    }

    [Fact]
    public void TransactionIsBoundToTheInitiatingBrowserSessionAndAccount()
    {
        var (store, sessions, issued) = CreateSession();
        var other = sessions.Issue(Guid.NewGuid());
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.EpicGames, Callback, sessions);

        Assert.False(store.TryConsume(start.State, ExternalIdentityProvider.EpicGames, Callback, other.SessionToken, sessions, out _));
        Assert.True(store.TryConsume(start.State, ExternalIdentityProvider.EpicGames, Callback, issued.SessionToken, sessions, out _));
    }

    [Fact]
    public void InvalidOrExpiredBrowserSessionCannotStartOrCompleteLink()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-19T00:00:00Z"));
        var sessions = new BrowserAccountSessionStore(clock, TimeSpan.FromMinutes(5));
        var issued = sessions.Issue(Guid.NewGuid());
        var store = new AccountLinkAuthorizationStore(clock);

        Assert.Throws<UnauthorizedAccessException>(() =>
            store.Begin(Guid.NewGuid(), issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions));
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(store.TryConsume(start.State, ExternalIdentityProvider.Discord, Callback, issued.SessionToken, sessions, out _));
    }

    [Fact]
    public void ExpiredTransactionsAreRejectedAndPruned()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-19T00:00:00Z"));
        var (sessions, issued) = CreateBrowserSession(clock);
        var store = new AccountLinkAuthorizationStore(clock, TimeSpan.FromMinutes(2));
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions);

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.False(store.TryConsume(start.State, ExternalIdentityProvider.Discord, Callback, issued.SessionToken, sessions, out _));
        Assert.Equal(0, store.RemoveExpired());
    }

    [Fact]
    public void CapacityIsBoundedAndExpiredTransactionsArePrunedBeforeNewStart()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-19T00:00:00Z"));
        var (sessions, issued) = CreateBrowserSession(clock);
        var store = new AccountLinkAuthorizationStore(clock, TimeSpan.FromMinutes(2), maximumTransactions: 1);
        store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions);

        Assert.Throws<InvalidOperationException>(() =>
            store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Steam, Callback, sessions));
        clock.Advance(TimeSpan.FromMinutes(2));
        var next = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Steam, Callback, sessions);
        Assert.Equal(43, next.State.Length);
    }

    [Fact]
    public void RejectsInsecureOrMalformedCallbackUris()
    {
        var (store, sessions, issued) = CreateSession();

        Assert.Throws<ArgumentException>(() =>
            store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord,
                new Uri("http://accounts.example.test/callback"), sessions));
        Assert.Throws<ArgumentException>(() =>
            store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord,
                new Uri("https://user:pass@accounts.example.test/callback"), sessions));
        Assert.Throws<ArgumentException>(() =>
            store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord,
                new Uri("https://accounts.example.test/callback#fragment"), sessions));
    }

    [Fact]
    public void PkceChallengeIsSha256OfReturnedVerifierAfterConsume()
    {
        var (store, sessions, issued) = CreateSession();
        var start = store.Begin(issued.Info.AccountId, issued.SessionToken, ExternalIdentityProvider.Discord, Callback, sessions);
        Assert.True(store.TryConsume(start.State, ExternalIdentityProvider.Discord, Callback, issued.SessionToken, sessions, out var transaction));

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(transaction!.CodeVerifier!)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, start.CodeChallenge);
    }

    private static (AccountLinkAuthorizationStore Store, BrowserAccountSessionStore Sessions, IssuedBrowserAccountSession Issued) CreateSession()
    {
        var (sessions, issued) = CreateBrowserSession(TimeProvider.System);
        return (new AccountLinkAuthorizationStore(), sessions, issued);
    }

    private static (BrowserAccountSessionStore Sessions, IssuedBrowserAccountSession Issued) CreateBrowserSession(TimeProvider clock)
    {
        var sessions = new BrowserAccountSessionStore(clock);
        var issued = sessions.Issue(Guid.NewGuid());
        return (sessions, issued);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
