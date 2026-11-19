using HogwartsRP.Server.Accounts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace HogwartsRP.Server.Accounts;

/// <summary>Public portal URLs and non-secret provider client identifiers for account linking.</summary>
public sealed record AccountLinkEndpointOptions(
    Uri PortalUri,
    Uri DiscordCallbackUri,
    string? DiscordClientId,
    Uri SteamCallbackUri,
    Uri EpicCallbackUri,
    string? EpicClientId)
{
    public void Validate()
    {
        ValidateHttps(PortalUri, nameof(PortalUri));
        ValidateHttps(DiscordCallbackUri, nameof(DiscordCallbackUri));
        ValidateHttps(SteamCallbackUri, nameof(SteamCallbackUri));
        ValidateHttps(EpicCallbackUri, nameof(EpicCallbackUri));
        if (DiscordClientId is { } discordId
            && (string.IsNullOrWhiteSpace(discordId) || discordId.Length > 256 || discordId.Any(char.IsControl)))
            throw new ArgumentException("The Discord OAuth client ID is invalid.", nameof(DiscordClientId));
        if (EpicClientId is { } epicId
            && (string.IsNullOrWhiteSpace(epicId) || epicId.Length > 256 || epicId.Any(char.IsControl)))
            throw new ArgumentException("The Epic OAuth client ID is invalid.", nameof(EpicClientId));
        if (SteamCallbackUri.Query.Contains("state=", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Steam callback URI must not contain a state query parameter.", nameof(SteamCallbackUri));
    }

    private static void ValidateHttps(Uri uri, string name)
    {
        ArgumentNullException.ThrowIfNull(uri, name);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Account-link URLs must be absolute HTTPS URLs without user info or fragments.", name);
    }
}

/// <summary>
/// Central browser account-link routes. Map these only on the central account host, never on
/// individual game servers. Account ownership always comes from the authenticated session cookie.
/// </summary>
public static class AccountLinkEndpoints
{
    private const string SteamLoginEndpoint = "https://steamcommunity.com/openid/login";
    private const string DiscordAuthorizeEndpoint = "https://discord.com/oauth2/authorize";
    private const string EpicAuthorizeEndpoint = "https://www.epicgames.com/id/authorize";
    private const string OpenIdNamespace = "http://specs.openid.net/auth/2.0";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

    public static void Map(
        WebApplication application,
        AccountLinkEndpointOptions options,
        BrowserAccountSessionStore sessions,
        AccountLinkAuthorizationStore transactions,
        JsonAccountIdentityStore identities,
        DiscordAccountLinkClient? discord,
        SteamOpenIdAccountLinkClient? steam,
        EpicGamesAccountLinkClient? epic)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(identities);
        options.Validate();

        application.MapGet("/account/links", (HttpContext context) =>
            GetLinks(context, sessions, identities));
        application.MapPost("/account/links/{provider}/start", (string provider, HttpContext context) =>
            Start(provider, context, options, sessions, transactions));
        application.MapGet("/account/links/discord/callback", (HttpContext context) =>
            CompleteDiscordAsync(context, options, sessions, transactions, discord));
        application.MapGet("/account/links/steam/callback", (HttpContext context) =>
            CompleteSteamAsync(context, options, sessions, transactions, steam));
        application.MapGet("/account/links/epic/callback", (HttpContext context) =>
            CompleteEpicAsync(context, options, sessions, transactions, epic));
        application.MapDelete("/account/links/{provider}", (string provider, HttpContext context) =>
            UnlinkAsync(provider, context, sessions, identities));
    }

    public static async Task<IResult> GetLinks(
        HttpContext context,
        BrowserAccountSessionStore sessions,
        JsonAccountIdentityStore identities)
    {
        SetNoStore(context);
        if (!TryGetSession(context, sessions, out var sessionToken, out var session))
            return Results.Unauthorized();

        var links = await identities.GetLinksAsync(session!.AccountId, context.RequestAborted);
        return Results.Json(new
        {
            accountId = session.AccountId,
            providers = Enum.GetValues<ExternalIdentityProvider>().Select(provider =>
            {
                var link = links.SingleOrDefault(item => item.Provider == provider);
                return new
                {
                    provider = ProviderName(provider),
                    connected = link is not null,
                    displayName = link?.ProviderDisplayName,
                    linkedAtUtc = link?.LinkedAtUtc
                };
            })
        });
    }

    public static IResult Start(
        string providerName,
        HttpContext context,
        AccountLinkEndpointOptions options,
        BrowserAccountSessionStore sessions,
        AccountLinkAuthorizationStore transactions)
    {
        SetNoStore(context);
        if (!TryGetSession(context, sessions, out var sessionToken, out var session))
            return Results.Unauthorized();
        if (!HasValidCsrfAndOrigin(context, sessions, sessionToken))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!TryParseProvider(providerName, out var provider))
            return Results.NotFound();
        if (!IsConfigured(provider, options, discord, steam, epic))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
            var callback = CallbackUri(options, provider);
            var start = transactions.Begin(session!.AccountId, sessionToken, provider, callback, sessions);
            return Results.Redirect(BuildAuthorizationUri(provider, start, options, callback));
        }
        catch (InvalidOperationException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    public static async Task<IResult> CompleteDiscordAsync(
        HttpContext context,
        AccountLinkEndpointOptions options,
        BrowserAccountSessionStore sessions,
        AccountLinkAuthorizationStore transactions,
        DiscordAccountLinkClient? discord)
    {
        SetNoStore(context);
        if (discord is null || options.DiscordClientId is null) return PortalResult(options, "failed", "discord");
        var state = SingleQueryValue(context, "state");
        var callback = options.DiscordCallbackUri;
        if (!TryConsume(state, ExternalIdentityProvider.Discord, callback, context, sessions, transactions, out var transaction))
            return Results.BadRequest();

        if (SingleQueryValue(context, "error") is not null)
            return PortalResult(options, "failed", "discord");
        var issuer = SingleQueryValue(context, "iss");
        if (issuer is not null && !string.Equals(issuer, "https://discord.com", StringComparison.Ordinal))
            return PortalResult(options, "failed", "discord");
        var code = SingleQueryValue(context, "code");
        if (code is null)
            return PortalResult(options, "failed", "discord");

        try
        {
            await discord.ExchangeAndLinkAsync(transaction!, code, context.RequestAborted);
            return PortalResult(options, "connected", "discord");
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return PortalResult(options, "failed", "discord");
        }
    }

    public static async Task<IResult> CompleteEpicAsync(
        HttpContext context,
        AccountLinkEndpointOptions options,
        BrowserAccountSessionStore sessions,
        AccountLinkAuthorizationStore transactions,
        EpicGamesAccountLinkClient? epic)
    {
        SetNoStore(context);
        if (epic is null || options.EpicClientId is null) return PortalResult(options, "failed", "epic");
        var state = SingleQueryValue(context, "state");
        var callback = options.EpicCallbackUri;
        if (!TryConsume(state, ExternalIdentityProvider.EpicGames, callback, context, sessions, transactions, out var transaction))
            return Results.BadRequest();

        if (SingleQueryValue(context, "error") is not null)
            return PortalResult(options, "failed", "epic");
        var code = SingleQueryValue(context, "code");
        if (code is null)
            return PortalResult(options, "failed", "epic");

        try
        {
            await epic.ExchangeAndLinkAsync(transaction!, code, context.RequestAborted);
            return PortalResult(options, "connected", "epic");
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return PortalResult(options, "failed", "epic");
        }
    }

    public static async Task<IResult> CompleteSteamAsync(
        HttpContext context,
        AccountLinkEndpointOptions options,
        BrowserAccountSessionStore sessions,
        AccountLinkAuthorizationStore transactions,
        SteamOpenIdAccountLinkClient? steam)
    {
        SetNoStore(context);
        if (steam is null) return PortalResult(options, "failed", "steam");
        var state = SingleQueryValue(context, "state");
        var callback = options.SteamCallbackUri;
        if (!TryConsume(state, ExternalIdentityProvider.Steam, callback, context, sessions, transactions, out var transaction))
            return Results.BadRequest();

        if (string.Equals(SingleQueryValue(context, "openid.mode"), "cancel", StringComparison.Ordinal))
            return PortalResult(options, "cancelled", "steam");

        try
        {
            var assertion = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in context.Request.Query)
            {
                if (!item.Key.StartsWith("openid.", StringComparison.Ordinal)) continue;
                if (item.Value.Count != 1 || item.Value[0] is null || assertion.Count >= 64)
                    return PortalResult(options, "failed", "steam");
                assertion.Add(item.Key, item.Value[0]!);
            }

            await steam.VerifyAndLinkAsync(transaction!, state!, assertion, context.RequestAborted);
            return PortalResult(options, "connected", "steam");
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return PortalResult(options, "failed", "steam");
        }
    }

    public static async Task<IResult> UnlinkAsync(
        string providerName,
        HttpContext context,
        BrowserAccountSessionStore sessions,
        JsonAccountIdentityStore identities)
    {
        SetNoStore(context);
        if (!TryGetSession(context, sessions, out var sessionToken, out var session))
            return Results.Unauthorized();
        if (!HasValidCsrfAndOrigin(context, sessions, sessionToken))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!TryParseProvider(providerName, out var provider))
            return Results.NotFound();

        var removed = await identities.UnlinkAsync(session!.AccountId, provider, context.RequestAborted);
        return removed ? Results.NoContent() : Results.NotFound();
    }

    private static bool TryConsume(
        string? state,
        ExternalIdentityProvider provider,
        Uri callback,
        HttpContext context,
        BrowserAccountSessionStore sessions,
        AccountLinkAuthorizationStore transactions,
        out AccountLinkAuthorizationTransaction? transaction)
    {
        var cookie = context.Request.Cookies[BrowserAccountSessionEndpoints.CookieName];
        return transactions.TryConsume(state, provider, callback, cookie, sessions, out transaction);
    }

    private static string BuildAuthorizationUri(
        ExternalIdentityProvider provider,
        AccountLinkAuthorizationStart start,
        AccountLinkEndpointOptions options,
        Uri callback)
    {
        Dictionary<string, string?> query;
        string endpoint;
        switch (provider)
        {
            case ExternalIdentityProvider.Discord:
                endpoint = DiscordAuthorizeEndpoint;
                query = new()
                {
                    ["client_id"] = options.DiscordClientId,
                    ["response_type"] = "code",
                    ["scope"] = "identify",
                    ["redirect_uri"] = callback.AbsoluteUri,
                    ["state"] = start.State,
                    ["code_challenge"] = start.CodeChallenge,
                    ["code_challenge_method"] = start.CodeChallengeMethod
                };
                break;
            case ExternalIdentityProvider.Steam:
                endpoint = SteamLoginEndpoint;
                var returnTo = QueryHelpers.AddQueryString(callback.AbsoluteUri, "state", start.State);
                query = new()
                {
                    ["openid.ns"] = OpenIdNamespace,
                    ["openid.mode"] = "checkid_setup",
                    ["openid.return_to"] = returnTo,
                    ["openid.realm"] = callback.GetLeftPart(UriPartial.Authority) + "/",
                    ["openid.identity"] = IdentifierSelect,
                    ["openid.claimed_id"] = IdentifierSelect
                };
                break;
            case ExternalIdentityProvider.EpicGames:
                endpoint = EpicAuthorizeEndpoint;
                query = new()
                {
                    ["client_id"] = options.EpicClientId,
                    ["response_type"] = "code",
                    ["scope"] = "basic_profile",
                    ["redirect_uri"] = callback.AbsoluteUri,
                    ["state"] = start.State
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }

        return QueryHelpers.AddQueryString(endpoint, query);
    }


    private static bool IsConfigured(
        ExternalIdentityProvider provider,
        AccountLinkEndpointOptions options,
        DiscordAccountLinkClient? discord,
        SteamOpenIdAccountLinkClient? steam,
        EpicGamesAccountLinkClient? epic) => provider switch
    {
        ExternalIdentityProvider.Discord => discord is not null && !string.IsNullOrWhiteSpace(options.DiscordClientId),
        ExternalIdentityProvider.Steam => steam is not null,
        ExternalIdentityProvider.EpicGames => epic is not null && !string.IsNullOrWhiteSpace(options.EpicClientId),
        _ => false
    };

    private static bool TryGetSession(
        HttpContext context,
        BrowserAccountSessionStore sessions,
        out string? token,
        out BrowserAccountSessionInfo? session)
    {
        token = context.Request.Cookies[BrowserAccountSessionEndpoints.CookieName];
        return sessions.TryGet(token, out session);
    }

    private static bool HasValidCsrfAndOrigin(
        HttpContext context,
        BrowserAccountSessionStore sessions,
        string? sessionToken)
    {
        var csrf = context.Request.Headers[BrowserAccountSessionEndpoints.CsrfHeaderName].FirstOrDefault();
        if (!sessions.ValidateCsrf(sessionToken, csrf)) return false;

        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) return false;
        return string.Equals(originUri.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(originUri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && originUri.Port == context.Request.Host.Port.GetValueOrDefault(
                originUri.Scheme == Uri.UriSchemeHttps ? 443 : 80);
    }

    private static bool TryParseProvider(string value, out ExternalIdentityProvider provider)
    {
        provider = value.ToLowerInvariant() switch
        {
            "discord" => ExternalIdentityProvider.Discord,
            "steam" => ExternalIdentityProvider.Steam,
            "epic" or "epicgames" => ExternalIdentityProvider.EpicGames,
            _ => (ExternalIdentityProvider)(-1)
        };
        return Enum.IsDefined(provider);
    }

    private static string ProviderName(ExternalIdentityProvider provider) => provider switch
    {
        ExternalIdentityProvider.Discord => "discord",
        ExternalIdentityProvider.Steam => "steam",
        ExternalIdentityProvider.EpicGames => "epic",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static Uri CallbackUri(AccountLinkEndpointOptions options, ExternalIdentityProvider provider) => provider switch
    {
        ExternalIdentityProvider.Discord => options.DiscordCallbackUri,
        ExternalIdentityProvider.Steam => options.SteamCallbackUri,
        ExternalIdentityProvider.EpicGames => options.EpicCallbackUri,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string? SingleQueryValue(HttpContext context, string key)
    {
        var values = context.Request.Query[key];
        return values.Count == 1 ? values[0] : null;
    }

    private static IResult PortalResult(AccountLinkEndpointOptions options, string result, string provider)
    {
        var url = QueryHelpers.AddQueryString(options.PortalUri.AbsoluteUri, new Dictionary<string, string?>
        {
            ["link"] = result,
            ["provider"] = provider
        });
        return Results.Redirect(url);
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is HttpRequestException or InvalidDataException or ArgumentException
            or InvalidOperationException or KeyNotFoundException or System.Text.Json.JsonException;

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.ReferrerPolicy = "no-referrer";
    }
}
