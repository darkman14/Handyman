using System.Net;
using System.Text.Json;
using Handyman.Api.Features.Areas;
using Handyman.Api.Tests.Infrastructure;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0020 QA (qa-api): independent attack on GET /api/areas. KNOWN_GAP tests pin today's behavior of a filed bug.</summary>
public class US_0020_QaExploratoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpStatusCode Status, JsonElement Body)> Call(string query, HttpClient? client = null)
    {
        var r = await (client ?? factory.CreateClient()).GetAsync("/api/areas" + query);
        var text = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<JsonElement> Ok(string query)
    {
        var (status, body) = await Call(query);
        status.ShouldBe(HttpStatusCode.OK, query);
        return body;
    }

    private static string[] Names(JsonElement list) => list.EnumerateArray().Select(a => a.GetProperty("name").GetString()!).ToArray();

    // AC3: sort order for every level, all parents
    [Fact]
    public async Task Every_level_is_sorted_in_Serbian_Latin_order_with_the_right_kind_and_shape()
    {
        var top = await Ok("");
        var seen = new HashSet<int>();
        var total = 0;
        foreach (var city in top.EnumerateArray())
        {
            var cityId = city.GetProperty("id").GetInt32();
            var munis = await Ok($"?parentId={cityId}");
            Names(munis).ShouldBe(Names(munis).OrderBy(n => n, SerbianLatinComparer.Instance).ToArray());
            foreach (var m in munis.EnumerateArray())
            {
                m.GetProperty("kind").GetString().ShouldBe("municipality");
                var hoods = await Ok($"?parentId={m.GetProperty("id").GetInt32()}");
                hoods.GetArrayLength().ShouldBeGreaterThan(0);
                Names(hoods).ShouldBe(Names(hoods).OrderBy(n => n, SerbianLatinComparer.Instance).ToArray());
                foreach (var h in hoods.EnumerateArray())
                {
                    h.GetProperty("kind").GetString().ShouldBe("neighborhood");
                    seen.Add(h.GetProperty("id").GetInt32()).ShouldBeTrue("neighborhood listed under two parents");
                    total++;
                }
            }
        }
        total.ShouldBe(seen.Count);
        total.ShouldBeGreaterThanOrEqualTo(6 + 5);
    }

    [Fact]
    public async Task Each_item_has_exactly_id_name_kind_with_lowercase_kind_and_no_parent_or_internal_fields()
    {
        var top = await Ok("");
        var beogradId = top[0].GetProperty("id").GetInt32();
        foreach (var json in new[] { top, await Ok($"?parentId={beogradId}") })
            foreach (var item in json.EnumerateArray())
            {
                item.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ShouldBe(["id", "kind", "name"]);
                item.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Number);
                var kind = item.GetProperty("kind").GetString()!;
                kind.ShouldBe(kind.ToLowerInvariant());
                new[] { "city", "municipality", "neighborhood" }.ShouldContain(kind);
            }
    }

    [Fact]
    public async Task Response_is_json_and_a_signed_in_or_bogus_token_changes_nothing()
    {
        var r = await factory.CreateClient().GetAsync("/api/areas");
        r.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var anon = await r.Content.ReadAsStringAsync();
        (await factory.ClientFor(Guid.NewGuid()).GetStringAsync("/api/areas")).ShouldBe(anon);
        foreach (var header in new[] { "Bearer", "Bearer ", "Basic abc", "Bearer a.b.c", "Bearer eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0." })
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/areas");
            req.Headers.TryAddWithoutValidation("Authorization", header);
            var resp = await factory.CreateClient().SendAsync(req);
            resp.StatusCode.ShouldBe(HttpStatusCode.OK, header);
            (await resp.Content.ReadAsStringAsync()).ShouldBe(anon, header);
        }
    }

    // AC1 variants
    [Theory]
    [InlineData("?parentId")]
    [InlineData("?parentId=")]
    [InlineData("?parentId=%20%20")]
    [InlineData("?foo=bar")]
    [InlineData("?parentId=&parentId=")]
    public async Task Missing_empty_or_blank_parentId_is_the_top_level(string query)
    {
        var body = await Ok(query);
        Names(body).ShouldBe(["Beograd"]);
    }

    [Theory]
    [InlineData("?PARENTID=")]
    [InlineData("?parentid=")]
    [InlineData("?ParentId=")]
    public async Task The_parameter_name_is_case_insensitive_like_the_rest_of_aspnet(string prefix)
    {
        var beogradId = (await Ok(""))[0].GetProperty("id").GetInt32();
        Names(await Ok(prefix + beogradId)).ShouldBe(["Novi Beograd", "Vračar", "Zvezdara"]);
    }

    // AC4 boundaries
    [Theory]
    [InlineData("2147483647")]
    [InlineData("-2147483648")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Int_boundaries_return_an_empty_list_not_an_error(string id)
    {
        (await Ok("?parentId=" + id)).GetArrayLength().ShouldBe(0);
    }

    // AC5 variants
    [Theory]
    [InlineData("abc")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("99999999999999999999999999999999")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("0x1")]
    [InlineData("null")]
    [InlineData("%00")]
    [InlineData("%EF%BC%91")] // full-width digit one
    [InlineData("%D9%A1")] // Arabic-indic digit one
    [InlineData("%ZZ")] // broken percent escape
    [InlineData("%20%31")] // leading space then 1
    [InlineData("1%20")] // trailing space
    [InlineData("1&parentId=1")] // repeated value
    [InlineData("1&parentId=2")]
    [InlineData("'%3B%20DROP%20TABLE%20areas%3B--")]
    [InlineData("1%20OR%201=1")]
    public async Task Bad_parentId_values_give_400_validation_failed_naming_parentId_without_internals(string value)
    {
        var (status, body) = await Call("?parentId=" + value);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().ShouldBe("validation_failed");
        body.GetProperty("errors").TryGetProperty("parentId", out _).ShouldBeTrue();
        var text = body.GetRawText();
        text.ShouldNotContain("Exception");
        text.ShouldNotContain("   at ");
        text.ShouldNotContain("DROP");
    }

    [Fact]
    public async Task A_very_long_parentId_gives_400_not_500()
    {
        var (status, body) = await Call("?parentId=" + new string('9', 5000));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.GetProperty("code").GetString().ShouldBe("validation_failed");
        body.GetRawText().Length.ShouldBeLessThan(1000); // does not echo the input
    }

    [Fact]
    public async Task Blank_value_beside_a_real_one_in_a_repeated_parentId_uses_the_real_one()
    {
        var beogradId = (await Ok(""))[0].GetProperty("id").GetInt32();
        Names(await Ok($"?parentId={beogradId}&parentId=")).ShouldBe(["Novi Beograd", "Vračar", "Zvezdara"]);
        Names(await Ok($"?parentId=&parentId={beogradId}")).ShouldBe(["Novi Beograd", "Vračar", "Zvezdara"]);
    }

    // Methods: read-only endpoint
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Writing_methods_are_refused_and_change_nothing(string method)
    {
        var before = await factory.CreateClient().GetStringAsync("/api/areas?parentId=1");
        var r = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/areas")
        {
            Content = new StringContent("{\"name\":\"X\",\"parentId\":1}", System.Text.Encoding.UTF8, "application/json"),
        });
        ((int)r.StatusCode).ShouldBeInRange(400, 499);
        r.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await factory.CreateClient().GetStringAsync("/api/areas?parentId=1")).ShouldBe(before);
    }

    [Fact]
    public async Task HEAD_is_refused_with_405_and_allow_GET_without_a_500()
    {
        var r = await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/areas"));
        r.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        r.Content.Headers.Allow.ShouldContain("GET");
    }

    // KNOWN_GAP BUG-0014: non-canonical integers are accepted and whitespace handling is inconsistent
    [Fact]
    public async Task KNOWN_GAP_BUG_0014_non_canonical_ids_are_accepted_as_the_same_id()
    {
        var beogradId = (await Ok(""))[0].GetProperty("id").GetInt32();
        var canonical = Names(await Ok($"?parentId={beogradId}"));
        Names(await Ok($"?parentId=%2B{beogradId}")).ShouldBe(canonical); // +1
        Names(await Ok($"?parentId=000{beogradId}")).ShouldBe(canonical); // 0001
        (await Ok("?parentId=-0")).GetArrayLength().ShouldBe(0);
        // padded value is a 400 while an all-blank value is the top level
        (await Call($"?parentId=%20{beogradId}")).Status.ShouldBe(HttpStatusCode.BadRequest);
        Names(await Ok("?parentId=%20%20")).ShouldBe(["Beograd"]);
    }
}
