using HogwartsRP.Server.Accounts;

namespace HogwartsRP.Server.Tests;

public sealed class BrowserAccountSessionStoreTests
{
    [Fact]
    public void IssueReturnsOpaqueTokensAndBindsSessionToServerSuppliedAccount()
    {
        var accountId = Guid.NewGuid();
        var store = new BrowserAccountSessionStore();
        var issued = store.Issue(accountId);

        Assert.Equal(43, issued.SessionToken.Length);
        Assert.Equal(43, issued.CsrfToken.Length);
        Assert.NotEqual(issued.SessionToken, issued.CsrfToken);
        Assert.True(store.TryGet(issued.SessionToken, out var info));
        Assert.Equal(accountId, info!.AccountId);
        Assert.Equal(issued.Info.ExpiresAtUtc, info.ExpiresAtUtc);
    }

    [Fact]
    public void UnknownOrMalformedTokensAreRejected()
    {
        var store = new BrowserAccountSessionStore();

        Assert.False(store.TryGet("not-a-session", out _));
        Assert.False(store.ValidateCsrf("not-a-session", "not-a-csrf-token"));
        Assert.False(store.Revoke("not-a-session"));
    }

    [Fact]
    public void CsrfTokenMustMatchTheSession()
    {
        var store = new BrowserAccountSessionStore();
        var first = store.Issue(Guid.NewGuid());
        var second = store.Issue(Guid.NewGuid());

        Assert.True(store.ValidateCsrf(first.SessionToken, first.CsrfToken));
        Assert.False(store.ValidateCsrf(first.SessionToken, second.CsrfToken));
        Assert.False(store.ValidateCsrf(second.SessionToken, first.CsrfToken));
    }

    [Fact]
    public void RevokeInvalidatesOnlyTheSelectedSession()
    {
        var store = new BrowserAccountSessionStore();
        var first = store.Issue(Guid.NewGuid());
        var second = store.Issue(Guid.NewGuid());

        Assert.True(store.Revoke(first.SessionToken));
        Assert.False(store.TryGet(first.SessionToken, out _));
        Assert.True(store.TryGet(second.SessionToken, out _));
        Assert.False(store.Revoke(first.SessionToken));
    }

    [Fact]
    public void ExpiredSessionsCannotBeResolvedOrUsedForCsrf()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-19T00:00:00Z"));
        var store = new BrowserAccountSessionStore(clock, TimeSpan.FromMinutes(5));
        var issued = store.Issue(Guid.NewGuid());

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(store.TryGet(issued.SessionToken, out _));
        Assert.False(store.ValidateCsrf(issued.SessionToken, issued.CsrfToken));
        Assert.Equal(0, store.RemoveExpired());
    }

    [Fact]
    public void CapacityIsBoundedAndExpiredEntriesArePrunedBeforeIssuing()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-19T00:00:00Z"));
        var store = new BrowserAccountSessionStore(clock, TimeSpan.FromMinutes(5), maximumSessions: 1);
        var first = store.Issue(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => store.Issue(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromMinutes(5));

        var second = store.Issue(Guid.NewGuid());
        Assert.False(store.TryGet(first.SessionToken, out _));
        Assert.True(store.TryGet(second.SessionToken, out _));
    }

    [Fact]
    public void RejectsEmptyAccountAndUnsafeLifetimeOrCapacity()
    {
        var store = new BrowserAccountSessionStore();

        Assert.Throws<ArgumentException>(() => store.Issue(Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserAccountSessionStore(lifetime: TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserAccountSessionStore(maximumSessions: 0));
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
