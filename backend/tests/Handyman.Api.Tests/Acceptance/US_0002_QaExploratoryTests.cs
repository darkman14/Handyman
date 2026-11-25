using System.Net;
using System.Text.Json;
using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>QA exploratory attacks on US-0002 (caller identification and blocked users).</summary>
public class US_0002_QaExploratoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonDocument> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    private async Task<string> XminOf(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Database.SqlQueryRaw<string>("select xmin::text as \"Value\" from app.users where id = {0}", id).ToListAsync()).Single();
    }

    [Fact]
    public async Task Block_takes_effect_for_every_request_started_after_the_update_even_in_parallel()
    {
        var (id, client) = await factory.SignedInUserAsync();
        using var stop = new CancellationTokenSource();
        var background = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested) await client.GetAsync("/api/me");
        });
        await Task.Delay(100);

        await factory.SetBlockedAtAsync(id, DateTimeOffset.UtcNow);
        var after = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => client.GetAsync("/api/me")));
        stop.Cancel();
        await background;

        after.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unblock_then_block_flapping_always_follows_the_latest_state()
    {
        var (id, client) = await factory.SignedInUserAsync();
        for (var i = 0; i < 10; i++)
        {
            await factory.SetBlockedAtAsync(id, DateTimeOffset.UtcNow);
            (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            await factory.SetBlockedAtAsync(id, null);
            (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("1970-01-01T00:00:00Z")]
    [InlineData("9999-12-31T00:00:00Z")]
    public async Task Extreme_blocked_at_values_still_block(string value)
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.Parse(value));
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Parallel_blocked_and_unblocked_users_do_not_cross_talk_and_me_returns_own_id()
    {
        var users = new List<(Guid Id, HttpClient Client, bool Blocked)>();
        for (var i = 0; i < 12; i++)
        {
            var blocked = i % 2 == 0;
            var (id, c) = await factory.SignedInUserAsync(blockedAt: blocked ? DateTimeOffset.UtcNow : null);
            users.Add((id, c, blocked));
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 120).Select(async n =>
        {
            var u = users[n % users.Count];
            var r = await u.Client.GetAsync("/api/me");
            if (u.Blocked) return r.StatusCode == HttpStatusCode.Forbidden;
            if (r.StatusCode != HttpStatusCode.OK) return false;
            using var doc = await Json(r);
            return doc.RootElement.GetProperty("id").GetGuid() == u.Id;
        }));

        results.ShouldAllBe(ok => ok);
    }

    [Fact]
    public async Task Blocked_user_with_a_forged_or_wrongly_signed_token_gets_401_not_403()
    {
        var id = await factory.SeedUserAsync(blockedAt: DateTimeOffset.UtcNow);
        var wrongKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("another-key-another-key-another-key-123456"));
        var token = TestAuth.Build([new("sub", id.ToString()), new("role", "authenticated")], key: wrongKey);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/trades")).StatusCode.ShouldBe(HttpStatusCode.OK); // anonymous on public data, unchanged
    }

    [Theory]
    [InlineData("/api/areas?parentId=abc")]
    [InlineData("/api/areas?parentId=1")]
    [InlineData("/api/areas?parentId=99999999999999999999")]
    [InlineData("/api/trades?x=1")]
    [InlineData("/API/ME")]
    [InlineData("/api/me/")]
    [InlineData("/api/")]
    [InlineData("/api")]
    public async Task Blocked_user_gets_403_not_400_or_404_whatever_the_path_or_query(string path)
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        using var doc = await Json(response);
        doc.RootElement.GetProperty("code").GetString().ShouldBe("user_blocked");
    }

    [Fact]
    public async Task Unblocked_user_with_bad_parentId_still_gets_400_not_403()
    {
        var (_, client) = await factory.SignedInUserAsync();
        (await client.GetAsync("/api/areas?parentId=abc")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Head_and_options_for_a_blocked_user_are_refused_on_api()
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);
        (await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/me"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/trades"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/me"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cors_preflight_without_a_token_never_needs_a_user_row()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/me");
        request.Headers.Add("Origin", "http://localhost:8081");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        ((int)response.StatusCode).ShouldBeLessThan(500);
    }

    [Fact]
    public async Task Blocked_response_has_problem_shape_no_user_data_and_no_cacheable_headers()
    {
        var (id, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);
        var response = await client.GetAsync("/api/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var doc = await Json(response);
        doc.RootElement.GetProperty("status").GetInt32().ShouldBe(403);
        doc.RootElement.GetProperty("code").GetString().ShouldBe("user_blocked");
        var body = doc.RootElement.GetRawText();
        body.ShouldNotContain(id.ToString());
        body.ShouldNotContain("blocked_at", Case.Insensitive);
        body.ShouldNotContain("Test User");
        response.Headers.Contains("Retry-After").ShouldBeFalse();
        if (response.Headers.CacheControl is { } cc) cc.Public.ShouldBeFalse();
    }

    [Fact]
    public async Task Refused_calls_do_not_touch_the_users_row()
    {
        var (id, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);
        var before = await XminOf(id);

        foreach (var path in new[] { "/api/me", "/api/trades", "/api/areas?parentId=zzz" })
            (await client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/me", new StringContent("{\"role\":\"admin\"}"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await XminOf(id)).ShouldBe(before);
    }

    [Fact]
    public async Task Allowed_calls_do_not_write_to_the_users_row_either()
    {
        var (id, client) = await factory.SignedInUserAsync();
        var before = await XminOf(id);
        for (var i = 0; i < 5; i++) (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await XminOf(id)).ShouldBe(before);
    }

    [Fact]
    public async Task Deleted_user_mid_session_gets_404_on_me_and_200_on_public_data()
    {
        var (id, client) = await factory.SignedInUserAsync();
        await factory.DeleteUserAsync(id);

        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/trades")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/areas")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reinserted_user_with_the_same_id_is_recognised_again()
    {
        var (id, client) = await factory.SignedInUserAsync();
        await factory.DeleteUserAsync(id);
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await factory.SeedUserAsync(id);

        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_and_openapi_are_never_user_blocked()
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);
        (await client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/")).StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/openapi/v1.json")).StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Paths_that_only_look_like_api_are_not_treated_as_api()
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);
        var response = await client.GetAsync("/apiary");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Database_failure_gives_500_problem_without_internals()
    {
        using var broken = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=secretpw;Timeout=2;Command Timeout=2",
            })));
        var client = broken.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestAuth.TokenFor(Guid.NewGuid()));

        var response = await client.GetAsync("/api/me");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("secretpw");
        body.ShouldNotContain("127.0.0.1");
        body.ShouldNotContain("Npgsql");
        body.ShouldNotContain("   at ");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Database_failure_never_fails_open_for_a_signed_in_caller()
    {
        using var broken = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=2;Command Timeout=2",
            })));
        var client = broken.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestAuth.TokenFor(Guid.NewGuid()));

        // the public endpoint also needs the DB; either way it must not be a 200 served without the block check
        (await client.GetAsync("/api/trades")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Blocked_user_of_each_role_is_refused_on_head_of_health_free_paths()
    {
        foreach (var role in new[] { UserRole.Client, UserRole.Worker, UserRole.Admin })
        {
            var (_, client) = await factory.SignedInUserAsync(role, DateTimeOffset.UtcNow);
            (await client.GetAsync("/api/trades")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role.ToString());
        }
    }
}
