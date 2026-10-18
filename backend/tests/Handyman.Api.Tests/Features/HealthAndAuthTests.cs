using System.Net;
using Handyman.Api.Tests.Infrastructure;
using Shouldly;

namespace Handyman.Api.Tests.Features;

public class HealthAndAuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_200()
    {
        var response = await factory.CreateClient().GetAsync("/health");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_endpoint_returns_401_without_token()
    {
        var response = await factory.CreateClient().GetAsync("/api/me");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_returns_200_with_test_token()
    {
        var userId = Guid.NewGuid();
        var response = await factory.ClientFor(userId).GetAsync("/api/me");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain(userId.ToString());
    }
}
