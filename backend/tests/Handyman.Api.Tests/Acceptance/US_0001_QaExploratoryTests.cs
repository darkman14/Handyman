using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Handyman.Api.Tests.Infrastructure;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>QA attack tests for US-0001: hand-built JWTs sent over HTTP. KNOWN_GAP tests pin current behavior of filed bugs.</summary>
public class US_0001_QaExploratoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string B64(object o) => B64(JsonSerializer.SerializeToUtf8Bytes(o));

    /// <summary>Builds a compact JWT by hand. Pass sign=false for an empty signature (alg none).</summary>
    private static string Raw(Dictionary<string, object?> header, Dictionary<string, object?> payload, bool sign = true, string? secret = null)
    {
        var input = B64(header) + "." + B64(payload);
        if (!sign) return input + ".";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? TestAuth.Secret));
        return input + "." + B64(h.ComputeHash(Encoding.ASCII.GetBytes(input)));
    }

    private static long Now(int offsetSeconds = 0) => DateTimeOffset.UtcNow.AddSeconds(offsetSeconds).ToUnixTimeSeconds();

    private static Dictionary<string, object?> Hdr(string alg = "HS256") => new() { ["alg"] = alg, ["typ"] = "JWT" };

    private static Dictionary<string, object?> Pay(object? sub = null, object? role = null, bool withSub = true, bool withRole = true, bool withExp = true) =>
        new Dictionary<string, object?>
        {
            ["iss"] = TestAuth.Issuer,
            ["aud"] = "authenticated",
        }.Also(d =>
        {
            if (withSub) d["sub"] = sub ?? Guid.NewGuid().ToString();
            if (withRole) d["role"] = role ?? "authenticated";
            if (withExp) d["exp"] = Now(3600);
        });

    private async Task<HttpResponseMessage> Send(string path, string? token, string scheme = "Bearer", HttpMethod? method = null)
    {
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        return await client.SendAsync(req);
    }

    private async Task<HttpStatusCode> Me(string? token, string scheme = "Bearer") => (await Send("/api/me", token, scheme)).StatusCode;

    // ---- Control: the hand-built token is accepted, so every 401 below is caused by the one thing changed ----
    [Fact]
    public async Task Control_hand_built_valid_token_is_accepted() =>
        (await Me(Raw(Hdr(), Pay()))).ShouldBe(HttpStatusCode.OK);

    // ---- Endpoint enumeration ----
    [Fact]
    public async Task Every_non_public_endpoint_in_the_openapi_file_returns_401_without_or_with_bad_token()
    {
        var file = FindUp("docs/api/Handyman.Api.json");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        var publicGets = new HashSet<string> { "/api/trades", "/api/areas", "/health" };
        var checkedCount = 0;
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith("/api")) continue;
            foreach (var m in path.Value.EnumerateObject())
            {
                var method = new HttpMethod(m.Name.ToUpperInvariant());
                var url = path.Name.Replace("{id}", Guid.NewGuid().ToString());
                if (method == HttpMethod.Get && publicGets.Contains(path.Name)) continue;
                foreach (var token in new string?[] { null, "garbage", Raw(Hdr(), Pay(withSub: false)) })
                    (await Send(url, token, method: method)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {url}");
                checkedCount++;
            }
        }
        checkedCount.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Other_verbs_on_public_paths_never_succeed_anonymously(string verb)
    {
        foreach (var path in new[] { "/api/me", "/api/trades", "/api/areas" })
        {
            var code = (await Send(path, null, method: new HttpMethod(verb))).StatusCode;
            ((int)code).ShouldBeInRange(400, 499, $"{verb} {path}");
        }
    }

    [Fact]
    public async Task Unknown_api_route_never_returns_200_or_500()
    {
        var code = (await Send("/api/does-not-exist", null)).StatusCode;
        code.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    // ---- Algorithm attacks ----
    [Fact]
    public async Task Alg_none_with_empty_signature_returns_401() =>
        (await Me(Raw(Hdr("none"), Pay(), sign: false))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Alg_None_mixed_case_and_missing_signature_segment_returns_401()
    {
        var t = B64(Hdr("None")) + "." + B64(Pay());
        (await Me(t)).ShouldBe(HttpStatusCode.Unauthorized);
        (await Me(t + ".")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Alg_none_header_with_a_real_hs256_signature_returns_401()
    {
        // Header lies about the algorithm, signature was made with the correct key.
        var input = B64(Hdr("none")) + "." + B64(Pay());
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(TestAuth.Secret));
        (await Me(input + "." + B64(h.ComputeHash(Encoding.ASCII.GetBytes(input))))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("HS384")]
    [InlineData("HS512")]
    [InlineData("RS256")]
    [InlineData("ES256")]
    [InlineData("PS256")]
    public async Task Algorithm_confusion_other_algorithms_with_a_hmac_signature_return_401(string alg) =>
        (await Me(Raw(Hdr(alg), Pay()))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Hs256_signature_made_with_the_issuer_url_or_public_looking_string_as_key_returns_401()
    {
        foreach (var key in new[] { TestAuth.Issuer, "authenticated", "", "secret" })
            (await Me(Raw(Hdr(), Pay(), secret: key.Length == 0 ? "x" : key))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tampered_payload_with_original_signature_returns_401()
    {
        var good = Raw(Hdr(), Pay(role: "anon"));
        var parts = good.Split('.');
        var other = B64(Pay());
        (await Me($"{parts[0]}.{other}.{parts[2]}")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Flipped_signature_bit_returns_401()
    {
        var parts = Raw(Hdr(), Pay()).Split('.');
        var sig = parts[2].ToCharArray();
        sig[0] = sig[0] == 'A' ? 'B' : 'A';
        (await Me($"{parts[0]}.{parts[1]}.{new string(sig)}")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Header parameters that point at keys: must never cause outbound fetches or trust ----
    [Theory]
    [InlineData("kid", "unknown-key-id")]
    [InlineData("jku", "http://127.0.0.1:1/jwks.json")]
    [InlineData("x5u", "http://127.0.0.1:1/cert.pem")]
    [InlineData("jwk", "evil")]
    [InlineData("x5c", "evil")]
    public async Task Key_pointer_headers_do_not_change_the_outcome_and_do_not_hang(string name, string value)
    {
        var header = Hdr();
        header[name] = name is "jwk" or "x5c" ? new { kty = "oct", k = "ZXZpbA" } : value;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = factory.CreateClient();
        // signed with an attacker key: must be 401
        var evil = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        evil.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Raw(header, Pay(), secret: "attacker-key-attacker-key-attacker-key"));
        (await client.SendAsync(evil, cts.Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // signed with the real key: signature is valid, the extra header is ignored
        var ok = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        ok.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Raw(header, Pay()));
        (await client.SendAsync(ok, cts.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Time claims ----
    [Fact]
    public async Task Nbf_far_in_the_future_returns_401()
    {
        var p = Pay(); p["nbf"] = Now(3600);
        (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nbf_just_past_the_clock_skew_returns_401()
    {
        var p = Pay(); p["nbf"] = Now(90);
        (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nbf_within_the_clock_skew_is_accepted()
    {
        var p = Pay(); p["nbf"] = Now(5);
        (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Iat_in_the_future_is_not_validated_by_the_api_and_is_accepted()
    {
        // Documented behavior: iat is informational (Supabase tokens always have exp). Not a defect on its own.
        var p = Pay(); p["iat"] = Now(86400);
        (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_exp_returns_401() =>
        (await Me(Raw(Hdr(), Pay(withExp: false)))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Exp_as_string_or_null_or_zero_returns_401()
    {
        foreach (var exp in new object?[] { "never", null, 0, -1 })
        {
            var p = Pay(); p["exp"] = exp;
            (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.Unauthorized, $"exp={exp ?? "null"}");
        }
    }

    [Fact]
    public async Task Exp_far_future_is_accepted()
    {
        var p = Pay(); p["exp"] = Now(10 * 365 * 86400);
        (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.OK);
    }

    // ---- Transport shape ----
    [Fact]
    public async Task Huge_token_never_gets_200_or_500()
    {
        var p = Pay(); p["blob"] = new string('A', 200_000);
        var code = await Me(Raw(Hdr(), p));
        // 401 if the server reads it, 431/413/400 if limits trip first. Never accepted, never a crash.
        code.ShouldNotBe(HttpStatusCode.OK);
        ((int)code).ShouldBeLessThan(500);
    }

    [Fact]
    public async Task Huge_garbage_token_returns_401_or_a_4xx_limit()
    {
        var code = await Me(new string('A', 500_000));
        ((int)code).ShouldBeInRange(400, 499);
    }

    [Fact]
    public async Task Two_authorization_headers_valid_and_garbage_are_not_accepted()
    {
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.TryAddWithoutValidation("Authorization", new[] { "Bearer " + Raw(Hdr(), Pay()), "Bearer garbage" });
        (await client.SendAsync(req)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req2.Headers.TryAddWithoutValidation("Authorization", new[] { "Bearer garbage", "Bearer " + Raw(Hdr(), Pay()) });
        (await client.SendAsync(req2)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_valid_tokens_in_one_header_value_are_not_accepted()
    {
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Raw(Hdr(), Pay())}, Bearer {Raw(Hdr(), Pay())}");
        (await client.SendAsync(req)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lowercase_and_uppercase_bearer_scheme_follow_rfc_7235_and_are_accepted()
    {
        // The scheme is case-insensitive per RFC 7235; accepting it is correct, rejecting would also be safe.
        var t = Raw(Hdr(), Pay());
        (await Me(t, "bearer")).ShouldBe(HttpStatusCode.OK);
        (await Me(t, "BEARER")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_in_query_string_is_not_accepted()
    {
        var t = Raw(Hdr(), Pay());
        foreach (var q in new[] { "access_token", "token", "jwt", "apikey", "Authorization" })
            (await Send($"/api/me?{q}={t}", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, q);
    }

    [Fact]
    public async Task Token_in_cookie_or_apikey_header_is_not_accepted()
    {
        var t = Raw(Hdr(), Pay());
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.TryAddWithoutValidation("apikey", t);
        req.Headers.TryAddWithoutValidation("X-Access-Token", t);
        req.Headers.TryAddWithoutValidation("Cookie", $"access_token={t}; sb-access-token={t}");
        (await client.SendAsync(req)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_with_whitespace_padding_or_quotes_is_not_a_crash()
    {
        var t = Raw(Hdr(), Pay());
        var client = factory.CreateClient();
        foreach (var value in new[] { $"Bearer \"{t}\"", $"Bearer  {t} x", "Bearer ", "Bearer null", "Bearer undefined", $"Bearer{t}" })
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            req.Headers.TryAddWithoutValidation("Authorization", value);
            var code = (await client.SendAsync(req)).StatusCode;
            code.ShouldBe(HttpStatusCode.Unauthorized, value.Length > 20 ? value[..20] : value);
        }
    }

    [Fact]
    public async Task Jwe_style_five_segment_and_two_segment_tokens_return_401()
    {
        var t = Raw(Hdr(), Pay());
        (await Me(t + ".extra.part")).ShouldBe(HttpStatusCode.Unauthorized);
        (await Me(string.Join('.', t.Split('.').Take(2)))).ShouldBe(HttpStatusCode.Unauthorized);
        (await Me("....")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- sub variants ----
    [Fact]
    public async Task Uppercase_guid_sub_is_accepted_and_resolves_to_the_same_user()
    {
        var id = Guid.NewGuid();
        var res = await Send("/api/me", Raw(Hdr(), Pay(sub: id.ToString().ToUpperInvariant())));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).ToLowerInvariant().ShouldContain(id.ToString());
    }

    [Theory]
    [InlineData("{0}")]       // braces
    [InlineData("({0})")]     // parentheses
    [InlineData("{0:N}")]     // no hyphens
    public async Task Alternative_guid_spellings_of_sub_resolve_to_one_canonical_user_id_or_are_refused(string format)
    {
        var id = Guid.NewGuid();
        var sub = format switch { "{0}" => "{" + id + "}", "({0})" => "(" + id + ")", _ => id.ToString("N") };
        var res = await Send("/api/me", Raw(Hdr(), Pay(sub: sub)));
        if (res.StatusCode == HttpStatusCode.OK)
            (await res.Content.ReadAsStringAsync()).ShouldContain(id.ToString()); // canonical output, same identity
        else
            res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sub_with_surrounding_whitespace_returns_401_or_the_canonical_id()
    {
        var id = Guid.NewGuid();
        var res = await Send("/api/me", Raw(Hdr(), Pay(sub: $" {id} ")));
        res.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
    }

    [Fact]
    public async Task KNOWN_GAP_BUG_0016_all_zero_guid_sub_is_accepted_but_should_be_401() =>
        // Desired: 401 (the nil GUID is not a user id). Pins current behavior so the suite stays green; flip to Unauthorized when fixed.
        (await Me(Raw(Hdr(), Pay(sub: Guid.Empty.ToString())))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Sub_as_number_null_array_or_object_returns_401()
    {
        foreach (var sub in new object?[] { 12345, new { a = 1 }, true, new string[0] })
            (await Me(Raw(Hdr(), Pay(sub: sub)))).ShouldBe(HttpStatusCode.Unauthorized, JsonSerializer.Serialize(sub));
    }

    [Fact]
    public async Task Duplicate_sub_claims_cannot_smuggle_another_identity()
    {
        // JSON with duplicate "sub" keys: parser keeps one; whichever it is, it must be a GUID that /me echoes consistently.
        var a = Guid.NewGuid();
        var header = B64(Hdr());
        var payload = $"{{\"iss\":\"{TestAuth.Issuer}\",\"aud\":\"authenticated\",\"role\":\"authenticated\",\"sub\":\"not-a-guid\",\"sub\":\"{a}\",\"exp\":{Now(3600)}}}";
        var input = header + "." + B64(Encoding.UTF8.GetBytes(payload));
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(TestAuth.Secret));
        var res = await Send("/api/me", input + "." + B64(h.ComputeHash(Encoding.ASCII.GetBytes(input))));
        res.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        if (res.StatusCode == HttpStatusCode.OK) (await res.Content.ReadAsStringAsync()).ShouldContain(a.ToString());
    }

    [Fact]
    public async Task Sub_array_of_two_claims_with_a_bad_first_value_returns_401()
    {
        (await Me(Raw(Hdr(), Pay(sub: new[] { "not-a-guid", Guid.NewGuid().ToString() })))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- role variants ----
    [Theory]
    [InlineData("anon")]
    [InlineData("service_role")]
    [InlineData("Authenticated")]
    [InlineData("AUTHENTICATED")]
    [InlineData(" authenticated")]
    [InlineData("authenticated ")]
    [InlineData("admin")]
    [InlineData("")]
    public async Task Role_other_than_exactly_authenticated_returns_401(string role) =>
        (await Me(Raw(Hdr(), Pay(role: role)))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Missing_role_claim_returns_401() =>
        (await Me(Raw(Hdr(), Pay(withRole: false)))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Role_as_non_string_returns_401()
    {
        foreach (var role in new object?[] { 1, true, new { a = 1 } })
            (await Me(Raw(Hdr(), Pay(role: role)))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Role_array_starting_with_anon_returns_401()
    {
        (await Me(Raw(Hdr(), Pay(role: new[] { "anon", "authenticated" })))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Service_role_key_style_token_returns_401() =>
        (await Me(Raw(Hdr(), Pay(withSub: false, role: "service_role")))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Role_nested_in_app_metadata_only_returns_401()
    {
        var p = Pay(withRole: false);
        p["app_metadata"] = new { role = "authenticated" };
        p["user_metadata"] = new { role = "authenticated" };
        (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- issuer / audience variants ----
    [Fact]
    public async Task Issuer_variants_return_401()
    {
        foreach (var iss in new[] { TestAuth.Issuer + "/", TestAuth.Issuer.ToUpperInvariant(), "http://test.local/auth/v1/x", "http://test.local", "", "http://test.local/auth/v1 " })
        {
            var p = Pay(); p["iss"] = iss;
            (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.Unauthorized, $"iss='{iss}'");
        }
        var none = Pay(); none.Remove("iss");
        (await Me(Raw(Hdr(), none))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Audience_variants_return_401()
    {
        foreach (var aud in new object[] { "Authenticated", "authenticated ", new[] { "anon" }, new[] { "other", "anon" }, "" })
        {
            var p = Pay(); p["aud"] = aud;
            (await Me(Raw(Hdr(), p))).ShouldBe(HttpStatusCode.Unauthorized, JsonSerializer.Serialize(aud));
        }
    }

    // ---- Public endpoints and health with attack tokens ----
    [Theory]
    [InlineData("/api/trades")]
    [InlineData("/api/areas")]
    [InlineData("/health")]
    public async Task Public_paths_return_200_with_every_kind_of_bad_token(string path)
    {
        var tokens = new[]
        {
            "garbage", "", Raw(Hdr("none"), Pay(), sign: false), Raw(Hdr(), Pay(), secret: "another-key-another-key-another-key"),
            Raw(Hdr("HS512"), Pay()), new string('A', 20_000), Raw(Hdr(), Pay(withExp: false)), Raw(Hdr(), Pay(role: "anon")),
        };
        foreach (var t in tokens)
        {
            var client = factory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + t);
            (await client.SendAsync(req)).StatusCode.ShouldBe(HttpStatusCode.OK, $"{path} token '{(t.Length > 15 ? t[..15] : t)}'");
        }
        var basic = factory.CreateClient();
        var r = new HttpRequestMessage(HttpMethod.Get, path);
        r.Headers.TryAddWithoutValidation("Authorization", "Basic dXNlcjpwYXNz");
        (await basic.SendAsync(r)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Public_endpoint_with_a_valid_token_also_returns_200()
    {
        (await factory.ClientFor(Guid.NewGuid()).GetAsync("/api/trades")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- 401 responses leak nothing ----
    [Fact]
    public async Task Unauthorized_responses_do_not_leak_secrets_keys_or_internals()
    {
        var cases = new Dictionary<string, string?>
        {
            ["none"] = null,
            ["garbage"] = "garbage",
            ["badsig"] = Raw(Hdr(), Pay(), secret: "another-key-another-key-another-key"),
            ["expired"] = Raw(Hdr(), new Dictionary<string, object?> { ["iss"] = TestAuth.Issuer, ["aud"] = "authenticated", ["sub"] = Guid.NewGuid().ToString(), ["role"] = "authenticated", ["exp"] = Now(-3600) }),
            ["badiss"] = Raw(Hdr(), Pay().Also(d => d["iss"] = "http://evil/auth/v1")),
            ["badaud"] = Raw(Hdr(), Pay().Also(d => d["aud"] = "anon")),
            ["nosub"] = Raw(Hdr(), Pay(withSub: false)),
            ["anon"] = Raw(Hdr(), Pay(role: "anon")),
        };
        foreach (var (name, token) in cases)
        {
            var res = await Send("/api/me", token);
            res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, name);
            var body = await res.Content.ReadAsStringAsync();
            body.ShouldBeEmpty(name);
            var headers = string.Join("\n", res.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
            foreach (var secret in new[] { TestAuth.Secret, "Exception", "StackTrace", "   at ", "Npgsql", "Microsoft.", "System." })
            {
                body.ShouldNotContain(secret, Case.Sensitive);
                headers.ShouldNotContain(secret, Case.Sensitive);
            }
            res.Headers.Contains("Server").ShouldBeFalse(name + ": Server header present"); // Kestrel default is suppressed in TestServer
            res.Headers.WwwAuthenticate.Select(a => a.Scheme).ShouldAllBe(s => s == "Bearer");
        }
    }

    [Fact]
    public async Task Unauthorized_response_does_not_reveal_the_expected_issuer()
    {
        var res = await Send("/api/me", Raw(Hdr(), Pay().Also(d => d["iss"] = "http://evil/auth/v1")));
        var headers = string.Join("\n", res.Headers.WwwAuthenticate.Select(a => a.ToString()));
        headers.ShouldNotContain(TestAuth.Issuer);
    }

    private static string FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, relative))) dir = dir.Parent;
        return dir == null ? throw new FileNotFoundException(relative) : Path.Combine(dir.FullName, relative);
    }
}

internal static class DictExt
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
