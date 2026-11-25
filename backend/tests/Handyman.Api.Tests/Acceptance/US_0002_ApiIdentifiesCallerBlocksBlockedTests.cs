using System.Net;
using System.Text.Json;
using Handyman.Api.Data;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0002: the API identifies the caller from app.users and refuses blocked users on every /api call.</summary>
public class US_0002_ApiIdentifiesCallerBlocksBlockedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private async Task AssertBlocked(HttpClient client, string path = "/api/me")
    {
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        (await CodeOf(response)).ShouldBe("user_blocked");
    }

    // AC1
    [Fact]
    public async Task Ac1_me_returns_the_callers_own_id()
    {
        var (id, client) = await factory.SignedInUserAsync();
        var (otherId, _) = await factory.SignedInUserAsync();

        var response = await client.GetAsync("/api/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
        doc.RootElement.GetProperty("id").GetGuid().ShouldNotBe(otherId);
    }

    [Theory]
    [InlineData(UserRole.Client)]
    [InlineData(UserRole.Worker)]
    [InlineData(UserRole.Admin)]
    public async Task Ac1_unblocked_users_of_every_role_are_handled(UserRole role)
    {
        var (_, client) = await factory.SignedInUserAsync(role);
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // AC2
    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/trades")]
    [InlineData("/api/areas")]
    [InlineData("/api/does-not-exist")]
    public async Task Ac2_blocked_user_gets_403_user_blocked_on_every_api_path(string path)
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow.AddDays(-1));
        await AssertBlocked(client, path);
    }

    [Fact]
    public async Task Ac2_blocked_user_is_refused_for_other_verbs_too()
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow.AddDays(-1));
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            var response = await client.SendAsync(new HttpRequestMessage(method, "/api/me") { Content = new StringContent("{}") });
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, method.Method);
            (await CodeOf(response)).ShouldBe("user_blocked");
        }
    }

    [Fact]
    public async Task Ac2_blocked_at_in_the_future_still_counts_as_blocked()
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow.AddYears(1));
        await AssertBlocked(client);
    }

    [Fact]
    public async Task Ac2_blocked_admin_is_refused_too()
    {
        var (_, client) = await factory.SignedInUserAsync(UserRole.Admin, DateTimeOffset.UtcNow);
        await AssertBlocked(client);
    }

    [Fact]
    public async Task Ac2_blocked_response_is_not_the_not_allowed_code_and_leaks_nothing()
    {
        var (id, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);
        var response = await client.GetAsync("/api/me");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("not_allowed");
        body.ShouldNotContain(id.ToString());
        body.ShouldNotContain("Npgsql");
    }

    [Fact]
    public async Task Ac2_no_data_is_changed_by_a_refused_call()
    {
        var (id, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var before = await Snapshot(id);

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            (await client.SendAsync(new HttpRequestMessage(method, "/api/me") { Content = new StringContent("{}") }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Snapshot(id)).ShouldBe(before);
    }

    private async Task<(int Users, DateTimeOffset? BlockedAt, int RatingSum, string Name)> Snapshot(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        var user = db.Users.Single(u => u.Id == id);
        return (db.Users.Count(), user.BlockedAt, user.RatingSum, user.Name);
    }

    // AC3
    [Fact]
    public async Task Ac3_blocking_takes_effect_on_the_next_call_with_the_same_token()
    {
        var (id, client) = await factory.SignedInUserAsync();
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/trades")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.SetBlockedAtAsync(id, DateTimeOffset.UtcNow);

        await AssertBlocked(client);
        await AssertBlocked(client, "/api/trades");
    }

    [Fact]
    public async Task Ac3_block_then_unblock_restores_access_on_the_next_call()
    {
        var (id, client) = await factory.SignedInUserAsync();
        await factory.SetBlockedAtAsync(id, DateTimeOffset.UtcNow);
        await AssertBlocked(client);

        await factory.SetBlockedAtAsync(id, null);

        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Blocking_one_user_does_not_affect_another()
    {
        var (blockedId, blocked) = await factory.SignedInUserAsync();
        var (_, control) = await factory.SignedInUserAsync();
        await factory.SetBlockedAtAsync(blockedId, DateTimeOffset.UtcNow);

        await AssertBlocked(blocked);
        (await control.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // AC4
    [Fact]
    public async Task Ac4_valid_token_without_a_users_row_gets_404_profile_missing_on_me()
    {
        var client = factory.ClientFor(Guid.NewGuid());

        var response = await client.GetAsync("/api/me");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(response)).ShouldBe("profile_missing");
    }

    [Fact]
    public async Task Ac4_token_of_a_deleted_user_gets_404_profile_missing()
    {
        var (id, client) = await factory.SignedInUserAsync();
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.DeleteUserAsync(id);

        var response = await client.GetAsync("/api/me");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOf(response)).ShouldBe("profile_missing");
    }

    [Theory]
    [InlineData("/api/trades")]
    [InlineData("/api/areas")]
    public async Task Public_endpoints_stay_200_for_a_valid_token_without_a_users_row(string path)
    {
        (await factory.ClientFor(Guid.NewGuid()).GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Anonymous and invalid tokens are not affected by the block check.
    [Theory]
    [InlineData("/api/trades")]
    [InlineData("/api/areas")]
    public async Task Public_endpoints_stay_200_for_anonymous_and_invalid_tokens(string path)
    {
        (await factory.CreateClient().GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var garbage = factory.CreateClient();
        garbage.DefaultRequestHeaders.Authorization = new("Bearer", "garbage");
        (await garbage.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_and_invalid_token_on_me_stay_401_not_403()
    {
        (await factory.CreateClient().GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var garbage = factory.CreateClient();
        garbage.DefaultRequestHeaders.Authorization = new("Bearer", "garbage");
        (await garbage.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_token_of_a_blocked_user_is_401_not_403()
    {
        var id = await factory.SeedUserAsync(blockedAt: DateTimeOffset.UtcNow);
        var past = DateTime.UtcNow.AddHours(-2);
        var token = TestAuth.Build([new("sub", id.ToString()), new("role", "authenticated")], expires: past, notBefore: past.AddHours(-1));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // AC5
    [Fact]
    public async Task Ac5_health_still_returns_200_for_a_blocked_user()
    {
        var (_, client) = await factory.SignedInUserAsync(blockedAt: DateTimeOffset.UtcNow);

        (await client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
