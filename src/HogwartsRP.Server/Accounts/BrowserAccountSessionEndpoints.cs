using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HogwartsRP.Server.Accounts;

/// <summary>HTTP endpoints for inspecting and revoking an existing central browser account session.</summary>
public static class BrowserAccountSessionEndpoints
{
    public const string CookieName = "HogwartsRP.AccountSession";
    public const string CsrfHeaderName = "X-HogwartsRP-CSRF";

    public static void Map(WebApplication application, BrowserAccountSessionStore sessions)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(sessions);
        application.MapGet("/account/session", (HttpContext context) => GetSession(context, sessions));
        application.MapDelete("/account/session", (HttpContext context) => RevokeSession(context, sessions));
    }

    /// <summary>Sets the opaque session cookie after a trusted sign-in handler issues the session.</summary>
    public static void SetSessionCookie(HttpContext context, IssuedBrowserAccountSession issued)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(issued);
        context.Response.Cookies.Append(CookieName, issued.SessionToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/account",
            Expires = issued.Info.ExpiresAtUtc,
            IsEssential = true
        });
    }

    public static IResult GetSession(HttpContext context, BrowserAccountSessionStore sessions)
    {
        SetNoStoreHeaders(context);
        var token = context.Request.Cookies[CookieName];
        if (!sessions.TryGet(token, out var session) || session is null)
            return Results.Unauthorized();

        return Results.Json(new
        {
            authenticated = true,
            accountId = session.AccountId,
            createdAtUtc = session.CreatedAtUtc,
            expiresAtUtc = session.ExpiresAtUtc
        });
    }

    public static IResult RevokeSession(HttpContext context, BrowserAccountSessionStore sessions)
    {
        SetNoStoreHeaders(context);
        var token = context.Request.Cookies[CookieName];
        if (!sessions.TryGet(token, out _))
            return Results.Unauthorized();

        var csrfToken = context.Request.Headers[CsrfHeaderName].FirstOrDefault();
        if (!sessions.ValidateCsrf(token, csrfToken))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        sessions.Revoke(token);
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/account"
        });
        return Results.NoContent();
    }

    private static void SetNoStoreHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
