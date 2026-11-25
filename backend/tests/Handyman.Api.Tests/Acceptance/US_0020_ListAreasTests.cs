using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Handyman.Api.Features.Areas;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0020: GET /api/areas browses the area tree level by level, publicly, sorted in Serbian Latin order.</summary>
public class US_0020_ListAreasTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<List<AreaResponse>> Get(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/areas" + query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<AreaResponse>>())!;
    }

    private async Task<AreaResponse> Child(string parentName, int parentId, string name) =>
        (await Get(factory.CreateClient(), $"?parentId={parentId}")).Single(a => a.Name == name);

    private async Task<(AreaResponse Beograd, AreaResponse Vracar, AreaResponse Zvezdara)> Tree()
    {
        var beograd = (await Get(factory.CreateClient())).Single();
        var vracar = await Child("Beograd", beograd.Id, "Vračar");
        var zvezdara = await Child("Beograd", beograd.Id, "Zvezdara");
        return (beograd, vracar, zvezdara);
    }

    // AC1
    [Fact]
    public async Task Anonymous_without_parentId_gets_only_Beograd()
    {
        var top = await Get(factory.CreateClient());
        top.Count.ShouldBe(1);
        top[0].Name.ShouldBe("Beograd");
        top[0].Kind.ShouldBe("city");
        top[0].Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task An_empty_parentId_is_the_top_level()
    {
        (await Get(factory.CreateClient(), "?parentId=")).ShouldBe(await Get(factory.CreateClient()));
    }

    // AC2
    [Fact]
    public async Task Beograd_has_three_municipalities()
    {
        var (beograd, _, _) = await Tree();
        var list = await Get(factory.CreateClient(), $"?parentId={beograd.Id}");
        list.Select(a => a.Name).ShouldBe(["Novi Beograd", "Vračar", "Zvezdara"]);
        list.ShouldAllBe(a => a.Kind == "municipality");
    }

    // AC3
    [Fact]
    public async Task Vracar_has_six_neighborhoods_in_Serbian_Latin_order()
    {
        var (_, vracar, _) = await Tree();
        var list = await Get(factory.CreateClient(), $"?parentId={vracar.Id}");
        list.Select(a => a.Name).ShouldBe(["Crveni krst", "Čubura", "Englezovac", "Kalenić", "Krunski venac", "Neimar"]);
        list.ShouldAllBe(a => a.Kind == "neighborhood");
    }

    [Fact]
    public async Task Zvezdara_puts_C_before_Dj_before_K()
    {
        var (_, _, zvezdara) = await Tree();
        var list = await Get(factory.CreateClient(), $"?parentId={zvezdara.Id}");
        list.Select(a => a.Name).ShouldBe(["Cvetkova pijaca", "Đeram", "Konjarnik", "Mali Mokri Lug", "Mirijevo"]);
    }

    // AC4
    [Fact]
    public async Task A_neighborhood_or_unknown_id_as_parent_gives_an_empty_list()
    {
        var (_, vracar, _) = await Tree();
        var neighborhood = (await Get(factory.CreateClient(), $"?parentId={vracar.Id}")).First();
        (await Get(factory.CreateClient(), $"?parentId={neighborhood.Id}")).ShouldBeEmpty();
        (await Get(factory.CreateClient(), "?parentId=999999")).ShouldBeEmpty();
        (await Get(factory.CreateClient(), "?parentId=-1")).ShouldBeEmpty();
        (await Get(factory.CreateClient(), "?parentId=0")).ShouldBeEmpty();
    }

    // AC5
    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    [InlineData("1,2")]
    public async Task A_parentId_that_is_not_an_id_returns_400_validation_failed_naming_parentId(string value)
    {
        var response = await factory.CreateClient().GetAsync($"/api/areas?parentId={value}");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe("validation_failed");
        (await response.Content.ReadAsStringAsync()).ShouldContain("parentId");
    }

    // Edge cases
    [Fact]
    public async Task Diacritics_survive_json_and_the_url_round_trip()
    {
        var (_, vracar, _) = await Tree();
        var json = await factory.CreateClient().GetStringAsync($"/api/areas?parentId={vracar.Id}");
        json.ShouldContain("Čubura");
        json.ShouldContain("Kalenić");
        // A municipality id from one call works in the next call without change.
        var again = await Get(factory.CreateClient(), $"?parentId={Uri.EscapeDataString(vracar.Id.ToString())}");
        again.Count.ShouldBe(6);
    }

    [Fact]
    public async Task A_parentId_passed_twice_does_not_crash()
    {
        var (beograd, vracar, _) = await Tree();
        var response = await factory.CreateClient().GetAsync($"/api/areas?parentId={beograd.Id}&parentId={vracar.Id}");
        ((int)response.StatusCode).ShouldBeLessThan(500);
        if (response.StatusCode == HttpStatusCode.BadRequest)
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("validation_failed");
    }

    [Fact]
    public async Task A_signed_in_user_gets_the_same_list()
    {
        (await Get(factory.ClientFor(Guid.NewGuid()))).ShouldBe(await Get(factory.CreateClient()));
    }

    [Fact]
    public async Task Expired_garbage_and_bad_signature_tokens_are_treated_as_anonymous()
    {
        var anonymous = await Get(factory.CreateClient());
        var expired = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(TestAuth.Issuer, TestAuth.Audience,
            [new Claim("sub", Guid.NewGuid().ToString())], DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-2),
            new SigningCredentials(TestAuth.Key, SecurityAlgorithms.HmacSha256)));
        foreach (var token in new[] { expired, "garbage", "not.a.jwt" })
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            (await Get(client)).ShouldBe(anonymous);
        }
    }

    [Fact]
    public void Serbian_Latin_comparer_orders_the_special_letters()
    {
        var sorted = new[] { "Šid", "Zemun", "Đeram", "Dedinje", "Čubura", "Ćuprija", "Cvetkova", "Žarkovo", "Crveni" }
            .OrderBy(s => s, SerbianLatinComparer.Instance).ToArray();
        sorted.ShouldBe(["Crveni", "Cvetkova", "Čubura", "Ćuprija", "Dedinje", "Đeram", "Šid", "Zemun", "Žarkovo"]);
    }
}
