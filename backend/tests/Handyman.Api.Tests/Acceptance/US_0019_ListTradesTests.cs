using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Handyman.Api.Features.Trades;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0019: GET /api/trades is public reference data, in a fixed order, and ignores bad tokens.</summary>
public class US_0019_ListTradesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] TradeNames =
        ["Appliance repair", "Electrical", "Plumbing", "Heating & AC", "Painting", "Carpentry"];

    private string Cs => factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;

    private static async Task<List<TradeResponse>> Get(HttpClient client)
    {
        var response = await client.GetAsync("/api/trades");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<TradeResponse>>())!;
    }

    private static string SignedToken(SecurityKey key, DateTime? notBefore = null, DateTime? expires = null)
    {
        var token = new JwtSecurityToken(TestAuth.Issuer, TestAuth.Audience,
            [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore, expires ?? DateTime.UtcNow.AddHours(1),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient WithAuthorization(string? scheme, string parameter)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", scheme is null ? parameter : $"{scheme} {parameter}");
        return client;
    }

    // AC1 + AC2
    [Fact]
    public async Task Anonymous_gets_the_six_trades_in_the_fixed_order()
    {
        var trades = await Get(factory.CreateClient());
        trades.Select(t => t.Name).ShouldBe(TradeNames);
    }

    [Fact]
    public async Task Names_keep_the_ampersand_in_the_raw_json()
    {
        var json = await factory.CreateClient().GetStringAsync("/api/trades");
        json.ShouldContain("Heating & AC");
    }

    // AC3
    [Fact]
    public async Task A_signed_in_user_gets_the_same_list()
    {
        var anonymous = await Get(factory.CreateClient());
        var signedIn = await Get(factory.ClientFor(Guid.NewGuid()));
        signedIn.ShouldBe(anonymous);
    }

    // AC4
    [Fact]
    public async Task Ids_match_the_stored_ids_and_are_stable_across_calls()
    {
        var first = await Get(factory.CreateClient());
        var second = await Get(factory.CreateClient());
        second.ShouldBe(first);

        await using var c = new NpgsqlConnection(Cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT id, name FROM app.trades", c);
        await using var r = await cmd.ExecuteReaderAsync();
        var stored = new Dictionary<string, int>();
        while (await r.ReadAsync()) stored[r.GetString(1)] = r.GetInt32(0);
        first.ShouldAllBe(t => stored[t.Name] == t.Id);
    }

    // AC5
    [Fact]
    public async Task An_expired_token_is_treated_as_anonymous()
    {
        var token = SignedToken(TestAuth.Key, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-2));
        (await Get(WithAuthorization("Bearer", token))).Select(t => t.Name).ShouldBe(TradeNames);
    }

    [Fact]
    public async Task A_garbage_token_is_treated_as_anonymous()
    {
        (await Get(WithAuthorization("Bearer", "not.a.jwt"))).Select(t => t.Name).ShouldBe(TradeNames);
        (await Get(WithAuthorization("Bearer", "garbage"))).Select(t => t.Name).ShouldBe(TradeNames);
        (await Get(WithAuthorization("Bearer", ""))).Select(t => t.Name).ShouldBe(TradeNames);
    }

    [Fact]
    public async Task A_token_with_a_bad_signature_is_treated_as_anonymous()
    {
        var otherKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-signing-key-another-signing-key-01"));
        (await Get(WithAuthorization("Bearer", SignedToken(otherKey)))).Select(t => t.Name).ShouldBe(TradeNames);
    }

    [Fact]
    public async Task An_authorization_header_without_the_Bearer_prefix_is_treated_as_anonymous()
    {
        var valid = TestAuth.TokenFor(Guid.NewGuid());
        (await Get(WithAuthorization(null, valid))).Select(t => t.Name).ShouldBe(TradeNames);
        (await Get(WithAuthorization("Basic", "dXNlcjpwYXNz"))).Select(t => t.Name).ShouldBe(TradeNames);
    }

    [Fact]
    public async Task Protected_endpoints_still_answer_401_for_the_same_bad_tokens()
    {
        var otherKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-signing-key-another-signing-key-01"));
        var tokens = new[]
        {
            SignedToken(TestAuth.Key, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-2)),
            "garbage",
            SignedToken(otherKey),
        };
        foreach (var token in tokens)
            (await WithAuthorization("Bearer", token).GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await WithAuthorization(null, TestAuth.TokenFor(Guid.NewGuid())).GetAsync("/api/me"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task The_response_is_identical_in_Development_and_Test(string environment)
    {
        var expected = await Get(factory.CreateClient());
        using var host = factory.WithWebHostBuilder(b => b.UseEnvironment(environment));
        (await Get(host.CreateClient())).ShouldBe(expected);
    }

    // A database that held trades before the reference migration: any order, a duplicate name, a custom trade.
    [Fact]
    public async Task On_a_polluted_database_the_order_and_the_six_names_still_hold()
    {
        var polluted = new ApiFactory();
        await polluted.InitializeAsync();
        try
        {
            var cs = polluted.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;
            await using (var c = new NpgsqlConnection(cs))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand("""
                    UPDATE app.trades SET id = id + 100;
                    INSERT INTO app.trades (id, name) OVERRIDING SYSTEM VALUE VALUES (3, 'Carpentry'), (2, 'Roofing'), (1, 'Painting');
                    """, c);
                await cmd.ExecuteNonQueryAsync();
            }
            var trades = await Get(polluted.CreateClient());
            trades.Select(t => t.Name).ShouldBe(TradeNames);
            trades.Single(t => t.Name == "Carpentry").Id.ShouldBe(3);  // duplicate name: lowest id wins
            trades.Single(t => t.Name == "Painting").Id.ShouldBe(1);
            trades.Single(t => t.Name == "Plumbing").Id.ShouldBe(103);
        }
        finally
        {
            await ((IAsyncLifetime)polluted).DisposeAsync();
        }
    }
}
