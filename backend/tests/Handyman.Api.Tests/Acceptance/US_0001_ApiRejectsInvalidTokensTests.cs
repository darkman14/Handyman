using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

public class US_0001_ApiRejectsInvalidTokensTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Claim[] UserClaims(string? sub = null) =>
        [new Claim("sub", sub ?? Guid.NewGuid().ToString()), new Claim("role", "authenticated")];

    private async Task<HttpStatusCode> Get(string path, string? token, string scheme = "Bearer")
    {
        var client = factory.CreateClient();
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, token);
        return (await client.GetAsync(path)).StatusCode;
    }

    private Task<HttpStatusCode> GetMe(string? token, string scheme = "Bearer") => Get("/api/me", token, scheme);

    // AC1
    [Fact]
    public async Task Ac1_api_returns_401_without_authorization_header() =>
        (await GetMe(null)).ShouldBe(HttpStatusCode.Unauthorized);

    // AC2
    [Fact]
    public async Task Ac2_expired_token_returns_401()
    {
        var past = DateTime.UtcNow.AddMinutes(-10);
        var token = TestAuth.Build(UserClaims(), expires: past, notBefore: past.AddHours(-1));
        (await GetMe(token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_that_expires_in_a_few_seconds_is_accepted()
    {
        var token = TestAuth.Build(UserClaims(), expires: DateTime.UtcNow.AddSeconds(5));
        (await GetMe(token)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_expired_a_few_seconds_ago_is_accepted_within_the_30_second_clock_skew()
    {
        var expires = DateTime.UtcNow.AddSeconds(-5);
        var token = TestAuth.Build(UserClaims(), expires: expires, notBefore: expires.AddHours(-1));
        (await GetMe(token)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_expired_longer_ago_than_the_clock_skew_returns_401()
    {
        var expires = DateTime.UtcNow.AddSeconds(-90);
        var token = TestAuth.Build(UserClaims(), expires: expires, notBefore: expires.AddHours(-1));
        (await GetMe(token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // AC3
    [Fact]
    public async Task Ac3_token_from_another_issuer_returns_401() =>
        (await GetMe(TestAuth.Build(UserClaims(), issuer: "http://evil.local/auth/v1"))).ShouldBe(HttpStatusCode.Unauthorized);

    // AC4
    [Fact]
    public async Task Ac4_wrong_audience_returns_401() =>
        (await GetMe(TestAuth.Build(UserClaims(), audience: "anon"))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Ac4_missing_audience_returns_401() =>
        (await GetMe(TestAuth.Build(UserClaims(), audience: null))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Ac4_token_signed_with_another_key_returns_401()
    {
        var other = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-signing-key-another-signing-key-99"));
        (await GetMe(TestAuth.Build(UserClaims(), key: other))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_signed_with_a_different_algorithm_returns_401()
    {
        // Same secret, longer so HS512 can use it: only the algorithm differs.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestAuth.Secret + TestAuth.Secret));
        (await GetMe(TestAuth.Build(UserClaims(), key: key, algorithm: SecurityAlgorithms.HmacSha512)))
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ac4_missing_sub_returns_401() =>
        (await GetMe(TestAuth.Build([new Claim("role", "authenticated")]))).ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    public async Task Ac4_empty_or_non_guid_sub_returns_401(string sub) =>
        (await GetMe(TestAuth.Build(UserClaims(sub)))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Supabase_anon_key_as_bearer_returns_401()
    {
        // The anon key is signed with the project secret, has role "anon" and no sub.
        var anon = TestAuth.Build([new Claim("role", "anon")], expires: DateTime.UtcNow.AddYears(10));
        (await GetMe(anon)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Valid_signature_and_sub_but_role_anon_returns_401() =>
        (await GetMe(TestAuth.Build([new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "anon")])))
            .ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Malformed_token_returns_401() =>
        (await GetMe("this.is.not-a-jwt")).ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("Basic")]
    [InlineData("Bearr")]
    [InlineData("Token")]
    public async Task Wrong_authorization_scheme_returns_401(string scheme) =>
        (await GetMe(TestAuth.TokenFor(Guid.NewGuid()), scheme)).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Bearer_without_a_token_returns_401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer");
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // AC5
    [Fact]
    public async Task Ac5_valid_token_is_not_401_and_returns_the_sub()
    {
        var userId = Guid.NewGuid();
        var response = await factory.ClientFor(userId).GetAsync("/api/me");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain(userId.ToString());
    }

    [Fact]
    public async Task Ac5_health_returns_200_without_token() =>
        (await Get("/health", null)).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Health_returns_200_with_garbage_token() =>
        (await Get("/health", "garbage")).ShouldBe(HttpStatusCode.OK);

    // Public reference data: bad tokens are treated as anonymous.
    [Theory]
    [InlineData("/api/trades")]
    [InlineData("/api/areas")]
    public async Task Public_endpoints_return_200_without_or_with_bad_tokens(string path)
    {
        var past = DateTime.UtcNow.AddHours(-2);
        var expired = TestAuth.Build(UserClaims(), expires: past, notBefore: past.AddHours(-1));
        var noSub = TestAuth.Build([new Claim("role", "authenticated")]);

        (await Get(path, null)).ShouldBe(HttpStatusCode.OK);
        (await Get(path, "garbage")).ShouldBe(HttpStatusCode.OK);
        (await Get(path, expired)).ShouldBe(HttpStatusCode.OK);
        (await Get(path, noSub)).ShouldBe(HttpStatusCode.OK);
    }
}
