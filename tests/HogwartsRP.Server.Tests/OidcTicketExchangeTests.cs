using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HogwartsRP.Core;
using HogwartsRP.Server;

namespace HogwartsRP.Server.Tests;

public sealed class OidcTicketExchangeTests
{
    private const string Issuer = "https://identity.example.test";
    private const string ClientId = "hogwartsrp-desktop";
    private static readonly DateTimeOffset CurrentTime = new(2026, 11, 18, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidTokenCreatesSingleUseServerOwnedTicket()
    {
        using var signingKey = RSA.Create(2048);
        var time = new TestTimeProvider(CurrentTime);
        var tickets = new OneTimeJoinTicketStore(time);
        using var exchange = CreateExchange(signingKey, tickets, time);
        var challenge = await exchange.CreateChallengeAsync();
        var idToken = CreateIdToken(signingKey, challenge.Nonce);

        var response = await exchange.ExchangeAsync(new(challenge.ChallengeId, idToken));

        Assert.NotNull(response);
        Assert.Equal(CurrentTime.AddMinutes(2), response.ExpiresAt);
        var identity = await tickets.ValidateCredentialAsync(response.Credential);
        Assert.NotNull(identity);
        Assert.StartsWith("oidc:", identity.AccountId);
        Assert.Equal("Ada Lovelace", identity.DisplayName);
        Assert.Null(await tickets.ValidateCredentialAsync(response.Credential));
        Assert.Null(await exchange.ExchangeAsync(new(challenge.ChallengeId, idToken)));
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("nonce")]
    [InlineData("expired")]
    [InlineData("signature")]
    public async Task InvalidTokensDoNotCreateJoinTickets(string invalidPart)
    {
        using var signingKey = RSA.Create(2048);
        using var otherSigningKey = RSA.Create(2048);
        var time = new TestTimeProvider(CurrentTime);
        var tickets = new OneTimeJoinTicketStore(time);
        using var exchange = CreateExchange(signingKey, tickets, time);
        var challenge = await exchange.CreateChallengeAsync();
        var idToken = CreateIdToken(
            invalidPart == "signature" ? otherSigningKey : signingKey,
            invalidPart == "nonce" ? "wrong-nonce" : challenge.Nonce,
            issuer: invalidPart == "issuer" ? "https://attacker.example.test" : Issuer,
            audience: invalidPart == "audience" ? "another-client" : ClientId,
            expiresAt: invalidPart == "expired" ? CurrentTime.AddSeconds(-1) : CurrentTime.AddMinutes(3));

        var response = await exchange.ExchangeAsync(new(challenge.ChallengeId, idToken));

        Assert.Null(response);
        Assert.Null(await tickets.ValidateCredentialAsync("there-is-no-ticket"));
    }

    [Fact]
    public async Task ExpiredChallengeIsRejectedBeforeProviderLookup()
    {
        using var signingKey = RSA.Create(2048);
        var handler = new MockOidcHandler(signingKey);
        var time = new TestTimeProvider(CurrentTime);
        var tickets = new OneTimeJoinTicketStore(time);
        using var exchange = new OidcTicketExchange(Issuer, ClientId, tickets, time, handler);
        var challenge = await exchange.CreateChallengeAsync();
        time.Advance(TimeSpan.FromMinutes(6));

        var response = await exchange.ExchangeAsync(new(challenge.ChallengeId, CreateIdToken(signingKey, challenge.Nonce)));

        Assert.Null(response);
        Assert.Equal(0, handler.RequestCount);
    }

    private static OidcTicketExchange CreateExchange(RSA signingKey, OneTimeJoinTicketStore tickets, TimeProvider time) =>
        new(Issuer, ClientId, tickets, time, new MockOidcHandler(signingKey));

    private static string CreateIdToken(
        RSA signingKey,
        string nonce,
        string issuer = Issuer,
        string audience = ClientId,
        DateTimeOffset? expiresAt = null)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "test-key", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["sub"] = "account-123",
            ["aud"] = audience,
            ["exp"] = (expiresAt ?? CurrentTime.AddMinutes(3)).ToUnixTimeSeconds(),
            ["iat"] = CurrentTime.ToUnixTimeSeconds(),
            ["nonce"] = nonce,
            ["name"] = "Ada Lovelace"
        }));
        var signingInput = header + "." + payload;
        var signature = signingKey.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + Base64Url(signature);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class MockOidcHandler(RSA signingKey) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var content = request.RequestUri?.AbsolutePath switch
            {
                "/.well-known/openid-configuration" => JsonSerializer.Serialize(new { issuer = Issuer, jwks_uri = Issuer + "/keys" }),
                "/keys" => CreateJwks(signingKey),
                _ => throw new InvalidOperationException("Unexpected mock identity-provider request.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }

        private static string CreateJwks(RSA key)
        {
            var parameters = key.ExportParameters(false);
            return JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        alg = "RS256",
                        kid = "test-key",
                        n = Base64Url(parameters.Modulus!),
                        e = Base64Url(parameters.Exponent!)
                    }
                }
            });
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow += delta;
    }
}
