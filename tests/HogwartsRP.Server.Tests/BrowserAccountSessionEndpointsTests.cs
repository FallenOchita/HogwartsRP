using System.Text;
using HogwartsRP.Server.Accounts;
using Microsoft.AspNetCore.Http;

namespace HogwartsRP.Server.Tests;

public sealed class BrowserAccountSessionEndpointsTests
{
    [Fact]
    public async Task GetSessionRejectsMissingCookieWithoutCaching()
    {
        var context = CreateContext();
        var result = BrowserAccountSessionEndpoints.GetSession(context, new BrowserAccountSessionStore());

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task GetSessionReturnsOnlyServerBoundAccountAndExpiry()
    {
        var store = new BrowserAccountSessionStore();
        var accountId = Guid.NewGuid();
        var issued = store.Issue(accountId);
        var context = CreateContext(issued.SessionToken);

        var result = BrowserAccountSessionEndpoints.GetSession(context, store);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        Assert.Contains(accountId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expiresAtUtc", body, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.SessionToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.CsrfToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeSessionRequiresMatchingCsrfToken()
    {
        var store = new BrowserAccountSessionStore();
        var issued = store.Issue(Guid.NewGuid());
        var context = CreateContext(issued.SessionToken);
        context.Request.Headers[BrowserAccountSessionEndpoints.CsrfHeaderName] = "wrong-token";

        var result = BrowserAccountSessionEndpoints.RevokeSession(context, store);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.True(store.TryGet(issued.SessionToken, out _));
        Assert.Empty(context.Response.Headers["Set-Cookie"]);
    }

    [Fact]
    public async Task RevokeSessionClearsCookieAndRevokesOnlyCurrentSession()
    {
        var store = new BrowserAccountSessionStore();
        var issued = store.Issue(Guid.NewGuid());
        var other = store.Issue(Guid.NewGuid());
        var context = CreateContext(issued.SessionToken);
        context.Request.Headers[BrowserAccountSessionEndpoints.CsrfHeaderName] = issued.CsrfToken;

        var result = BrowserAccountSessionEndpoints.RevokeSession(context, store);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.False(store.TryGet(issued.SessionToken, out _));
        Assert.True(store.TryGet(other.SessionToken, out _));
        Assert.Contains(BrowserAccountSessionEndpoints.CookieName, context.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
        Assert.Contains("path=/account", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetSessionCookieUsesSecureHttpOnlyStrictAndAccountPath()
    {
        var issued = new BrowserAccountSessionStore().Issue(Guid.NewGuid());
        var context = CreateContext();

        BrowserAccountSessionEndpoints.SetSessionCookie(context, issued);

        var cookie = context.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(BrowserAccountSessionEndpoints.CookieName, cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/account", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(issued.CsrfToken, cookie, StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateContext(string? sessionToken = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Scheme = "https";
        if (sessionToken is not null)
            context.Request.Headers.Cookie = $"{BrowserAccountSessionEndpoints.CookieName}={sessionToken}";
        return context;
    }
}
