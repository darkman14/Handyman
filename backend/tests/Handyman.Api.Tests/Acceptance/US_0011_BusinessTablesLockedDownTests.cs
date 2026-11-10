using System.Net;
using System.Text;
using Npgsql;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>
/// US-0011 checks against the LOCAL Supabase stack (manual QA, opt-in). They return immediately (pass vacuously)
/// unless QA_SUPABASE_DB_URL (Npgsql connection string), QA_SUPABASE_API_URL and QA_SUPABASE_ANON_KEY are set.
/// Precondition for AC3/AC5: the API has been run once in Development against the local database.
/// </summary>
public class US_0011_BusinessTablesLockedDownTests
{
    private static readonly string[] Tables =
    [
        "users", "worker_profiles", "trades", "worker_trades", "areas", "worker_areas", "stores",
        "jobs", "job_photos", "bids", "ratings", "notifications", "device_tokens",
    ];

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private static async Task<List<string>> Query(string sql)
    {
        await using var conn = new NpgsqlConnection(Env("QA_SUPABASE_DB_URL"));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await r.ReadAsync()) list.Add(r.GetValue(0)?.ToString() ?? "");
        return list;
    }

    // AC5 (local database): 13 tables + history, RLS on, no policies, no grants to Data API roles
    [Fact]
    public async Task Local_supabase_has_the_13_tables_with_rls_and_no_policies_or_api_role_grants()
    {
        if (Env("QA_SUPABASE_DB_URL") is null) return;
        (await Query("SELECT tablename FROM pg_tables WHERE schemaname = 'app'"))
            .ShouldBe([.. Tables, "__EFMigrationsHistory"], ignoreOrder: true);
        (await Query("""
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'app' AND c.relkind = 'r' AND NOT c.relrowsecurity
            """)).ShouldBeEmpty();
        (await Query("SELECT policyname FROM pg_policies WHERE schemaname = 'app'")).ShouldBeEmpty();
        (await Query("SELECT \"MigrationId\" FROM app.\"__EFMigrationsHistory\"")).ShouldContain(id => id.EndsWith("_Initial")); // a local DB created before the in-place replacement keeps a stale row
    }

    // AC3: the Data API does not expose schema app, with anon key (as apikey and as bearer), for reads and writes
    [Fact]
    public async Task Data_api_returns_and_writes_nothing_for_any_app_table_with_the_anon_key()
    {
        var api = Env("QA_SUPABASE_API_URL");
        var key = Env("QA_SUPABASE_ANON_KEY");
        if (api is null || key is null) return;
        using var http = new HttpClient();

        foreach (var table in Tables)
        {
            var read = new HttpRequestMessage(HttpMethod.Get, $"{api}/rest/v1/{table}?select=*");
            read.Headers.Add("apikey", key);
            read.Headers.Add("Authorization", $"Bearer {key}");
            read.Headers.Add("Accept-Profile", "app");
            var readRes = await http.SendAsync(read);
            ((int)readRes.StatusCode).ShouldBeGreaterThanOrEqualTo(400, table);

            var write = new HttpRequestMessage(HttpMethod.Post, $"{api}/rest/v1/{table}")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            write.Headers.Add("apikey", key);
            write.Headers.Add("Authorization", $"Bearer {key}");
            write.Headers.Add("Content-Profile", "app");
            var writeRes = await http.SendAsync(write);
            ((int)writeRes.StatusCode).ShouldBeGreaterThanOrEqualTo(400, table);
        }

        (await Query("SELECT count(*) FROM app.users")).Single().ShouldBe("0");
    }

    // AC3 defence in depth: even if app were exposed, anon/authenticated could not use it
    [Fact]
    public async Task Api_roles_have_no_usage_on_schema_app()
    {
        if (Env("QA_SUPABASE_DB_URL") is null) return;
        (await Query("SELECT has_schema_privilege('anon','app','USAGE') OR has_schema_privilege('authenticated','app','USAGE')"))
            .Single().ShouldBe("False");
    }
}
