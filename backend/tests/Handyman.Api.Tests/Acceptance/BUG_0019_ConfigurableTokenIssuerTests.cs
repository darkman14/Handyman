using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

public class BUG_0019_ConfigurableTokenIssuerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LocalIssuer = "http://127.0.0.1:54321/auth/v1";

    private HttpClient ClientWithIssuer(string? issuer) =>
        factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Supabase:Issuer"] = issuer })))
            .CreateClient();

    private static async Task<HttpStatusCode> GetMe(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/me")).StatusCode;
    }

    private async Task<string> TokenWithIssuer(string issuer) =>
        TestAuth.Build(
            [new Claim("sub", (await factory.SeedUserAsync()).ToString()), new Claim("role", "authenticated")],
            issuer: issuer);

    [Fact]
    public async Task Default_issuer_is_the_url_based_one_and_is_accepted() =>
        (await GetMe(factory.CreateClient(), await TokenWithIssuer(TestAuth.Issuer))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Default_issuer_rejects_a_different_issuer() =>
        (await GetMe(factory.CreateClient(), await TokenWithIssuer(LocalIssuer))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Configured_issuer_accepts_a_token_with_that_issuer() =>
        (await GetMe(ClientWithIssuer(LocalIssuer), await TokenWithIssuer(LocalIssuer))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Configured_issuer_is_trimmed() =>
        (await GetMe(ClientWithIssuer($"  {LocalIssuer}  "), await TokenWithIssuer(LocalIssuer))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Configured_issuer_rejects_the_old_url_based_issuer() =>
        (await GetMe(ClientWithIssuer(LocalIssuer), await TokenWithIssuer(TestAuth.Issuer))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Configured_issuer_rejects_any_other_issuer() =>
        (await GetMe(ClientWithIssuer(LocalIssuer), await TokenWithIssuer("http://evil.local/auth/v1"))).ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_or_whitespace_issuer_falls_back_to_the_default(string issuer)
    {
        (await GetMe(ClientWithIssuer(issuer), await TokenWithIssuer(TestAuth.Issuer))).ShouldBe(HttpStatusCode.OK);
        (await GetMe(ClientWithIssuer(issuer), await TokenWithIssuer(LocalIssuer))).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
