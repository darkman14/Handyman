using System.Net;
using System.Net.Http.Json;
using Handyman.Api.Auth;
using Handyman.Api.Features.Trades;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0019 QA exploratory: blocked user today, unreachable JWKS, custom trades, methods, headers.</summary>
public class US_0019_QaExploratoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] TradeNames =
        ["Appliance repair", "Electrical", "Plumbing", "Heating & AC", "Painting", "Carpentry"];

    private static async Task<List<TradeResponse>> Get(HttpClient client)
    {
        var response = await client.GetAsync("/api/trades");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<TradeResponse>>())!;
    }

    private async Task Exec(string sql)
    {
        var cs = factory.Services.GetRequiredService<IConfiguration>()
            .GetConnectionString("Default")!;
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    // US-0002: a blocked user's valid token is refused on the public list too, with 403 user_blocked.
    [Fact]
    public async Task A_blocked_users_valid_token_gets_403_user_blocked()
    {
        var id = Guid.NewGuid();
        await Exec($"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count, blocked_at)
            VALUES ('{id}', '+381600019001', 'Client', 'Blocked', 0, 0, now());
            """);
        var response = await factory.ClientFor(id).GetAsync("/api/trades");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"code\":\"user_blocked\"");
    }

    // Production validates tokens against the Supabase JWKS. Simulate Supabase being down: the real key resolver
    // pointed at a dead port, a token that looks real (kid header, right issuer and audience).
    [Fact]
    public async Task With_the_JWKS_unreachable_trades_still_answers_200_and_protected_endpoints_answer_401_not_500()
    {
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .PostConfigure<JwksCache>((options, jwks) =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = TestAuth.Issuer,
                        ValidateAudience = true,
                        ValidAudience = TestAuth.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        NameClaimType = "sub",
                        IssuerSigningKeyResolver = (_, _, kid, _) => jwks.GetKeys("http://127.0.0.1:1/auth/v1/.well-known/jwks.json", kid),
                    };
                })));

        var token = TestAuth.TokenFor(Guid.NewGuid());
        foreach (var path in new[] { "/api/trades", "/api/me" })
        {
            var client = host.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            var response = await client.GetAsync(path);
            response.StatusCode.ShouldBe(path == "/api/trades" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, path);
        }
    }

    // Custom trades on a shared database are not offered: the list is the six reference trades (story: "always the six").
    [Fact]
    public async Task A_seventh_custom_trade_is_not_listed()
    {
        var before = await Get(factory.CreateClient());
        await Exec("INSERT INTO app.trades (name) VALUES ('Roofing QA')");
        try
        {
            var after = await Get(factory.CreateClient());
            after.ShouldBe(before);
            after.Count.ShouldBe(6);
        }
        finally
        {
            await Exec("DELETE FROM app.trades WHERE name = 'Roofing QA'");
        }
    }

    [Fact]
    public async Task Wrong_methods_are_refused_with_405_and_GET_allowed()
    {
        var client = factory.CreateClient();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            var response = await client.SendAsync(new HttpRequestMessage(method, "/api/trades"));
            response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed, method.Method);
        }
    }

    [Fact]
    public async Task Trailing_slash_and_query_noise_return_the_same_list()
    {
        var client = factory.CreateClient();
        var expected = await Get(client);
        foreach (var url in new[] { "/api/trades/", "/api/trades?foo=bar&id=1&page=-1" })
        {
            var response = await client.GetAsync(url);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
            (await response.Content.ReadFromJsonAsync<List<TradeResponse>>()).ShouldBe(expected);
        }
    }

    [Fact]
    public async Task Duplicate_authorization_headers_and_foreign_Accept_types_still_return_the_json_list()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/trades");
        request.Headers.TryAddWithoutValidation("Authorization", ["Bearer a", "Bearer b"]);
        request.Headers.TryAddWithoutValidation("Accept", "application/xml");
        var response = await factory.CreateClient().SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Body_is_exactly_id_and_name_per_trade_and_nothing_else()
    {
        var json = await factory.CreateClient().GetStringAsync("/api/trades");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().ShouldBe(6);
        foreach (var item in doc.RootElement.EnumerateArray())
            item.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "name"]);
    }
}
