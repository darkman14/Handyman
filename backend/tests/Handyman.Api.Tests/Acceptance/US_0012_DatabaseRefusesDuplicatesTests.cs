using Handyman.Api.Tests.Features;
using Npgsql;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0012: uniqueness rules enforced by Postgres itself (Testcontainers).</summary>
public class US_0012_DatabaseRefusesDuplicatesTests(SchemaFixture fx) : IClassFixture<SchemaFixture>
{
    private const string UniqueViolation = "23505";
    private static int _seq = 1000;

    private static string G(int n) => $"00000000-0000-0000-0000-{n:D12}";

    private async Task<long> Count(string sql) => long.Parse((await fx.QueryStrings($"SELECT ({sql})::text")).Single());

    private async Task<int> NumberOf(string jobId) =>
        int.Parse((await fx.QueryStrings($"SELECT number::text FROM app.jobs WHERE id = '{jobId}'")).Single());

    private async Task<(string client, string worker, string job)> Seed()
    {
        var n = Interlocked.Increment(ref _seq);
        var client = G(n); var worker = G(n + 100000); var job = G(n + 200000);
        await fx.Execute($"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES
              ('{client}', '+3816{n:D8}', 'Client', 'C', 0, 0),
              ('{worker}', '+3817{n:D8}', 'Worker', 'W', 0, 0);
            INSERT INTO app.trades (name) VALUES ('T{n}');
            INSERT INTO app.areas (name, kind) VALUES ('A{n}', 'Neighborhood');
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
            VALUES ('{job}', '{client}', (SELECT max(id) FROM app.trades), 't', 'd', (SELECT max(id) FROM app.areas), 's', '1', 'Open', now());
            """);
        return (client, worker, job);
    }

    private static string Bid(string job, string worker, string status) => $"""
        INSERT INTO app.bids (id, job_id, worker_id, arrival_window_start, arrival_window_end, diagnostic_fee, hourly_rate, status, created_at)
        VALUES ('{G(Interlocked.Increment(ref _seq) + 900000)}', '{job}', '{worker}', now(), now() + interval '1 hour', 0, 0, '{status}', now());
        """;

    private static string Rating(string job, string rater, string ratee, int stars) => $"""
        INSERT INTO app.ratings (id, job_id, rater_id, ratee_id, stars, created_at)
        VALUES ('{G(Interlocked.Increment(ref _seq) + 800000)}', '{job}', '{rater}', '{ratee}', {stars}, now());
        """;

    // AC1
    [Fact]
    public async Task Second_bid_by_the_same_worker_on_a_job_is_refused()
    {
        var (_, worker, job) = await Seed();
        await fx.Execute(Bid(job, worker, "Active"));
        var ex = await Should.ThrowAsync<PostgresException>(() => fx.Execute(Bid(job, worker, "Active")));
        ex.SqlState.ShouldBe(UniqueViolation);
        (await Count($"SELECT count(*) FROM app.bids WHERE job_id = '{job}' AND worker_id = '{worker}'")).ShouldBe(1);
    }

    // AC1
    [Fact]
    public async Task A_withdrawn_bid_still_blocks_a_new_bid_from_the_same_worker()
    {
        var (_, worker, job) = await Seed();
        await fx.Execute(Bid(job, worker, "Withdrawn"));
        var ex = await Should.ThrowAsync<PostgresException>(() => fx.Execute(Bid(job, worker, "Active")));
        ex.SqlState.ShouldBe(UniqueViolation);
        (await Count($"SELECT count(*) FROM app.bids WHERE job_id = '{job}'")).ShouldBe(1);
    }

    // AC1 edge
    [Fact]
    public async Task Same_worker_on_another_job_or_another_worker_on_the_same_job_is_allowed()
    {
        var (_, worker, job) = await Seed();
        var (_, worker2, job2) = await Seed();
        await fx.Execute(Bid(job, worker, "Active"));
        await fx.Execute(Bid(job2, worker, "Active"));
        await fx.Execute(Bid(job, worker2, "Active"));
    }

    // AC1 edge: two connections at the same moment, only one survives
    [Fact]
    public async Task Two_simultaneous_bids_from_two_connections_leave_exactly_one()
    {
        var (_, worker, job) = await Seed();
        async Task<bool> TryInsert()
        {
            try { await fx.Execute(Bid(job, worker, "Active")); return true; }
            catch (PostgresException e) when (e.SqlState == UniqueViolation) { return false; }
        }
        var results = await Task.WhenAll(TryInsert(), TryInsert());
        results.Count(r => r).ShouldBe(1);
        (await Count($"SELECT count(*) FROM app.bids WHERE job_id = '{job}' AND worker_id = '{worker}'")).ShouldBe(1);
    }

    // AC2
    [Fact]
    public async Task Second_rating_by_the_same_rater_on_a_job_is_refused_and_only_the_first_remains()
    {
        var (client, worker, job) = await Seed();
        await fx.Execute(Rating(job, client, worker, 5));
        var ex = await Should.ThrowAsync<PostgresException>(() => fx.Execute(Rating(job, client, worker, 1)));
        ex.SqlState.ShouldBe(UniqueViolation);
        (await fx.QueryStrings($"SELECT stars::text FROM app.ratings WHERE job_id = '{job}' AND rater_id = '{client}'"))
            .ShouldBe(["5"]);
    }

    // AC2 edge
    [Fact]
    public async Task The_other_party_can_rate_the_same_job()
    {
        var (client, worker, job) = await Seed();
        await fx.Execute(Rating(job, client, worker, 4));
        await fx.Execute(Rating(job, worker, client, 3));
        (await Count($"SELECT count(*) FROM app.ratings WHERE job_id = '{job}'")).ShouldBe(2);
    }

    // AC3
    [Fact]
    public async Task A_second_user_with_the_same_phone_is_refused()
    {
        await fx.Execute("""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count)
            VALUES ('00000000-0000-0000-0000-0000000a0001', '+381600000201', 'Client', 'Ana', 0, 0);
            """);
        var ex = await Should.ThrowAsync<PostgresException>(() => fx.Execute("""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count)
            VALUES ('00000000-0000-0000-0000-0000000a0002', '+381600000201', 'Worker', 'Other', 0, 0);
            """));
        ex.SqlState.ShouldBe(UniqueViolation);
        (await Count("SELECT count(*) FROM app.users WHERE phone = '+381600000201'")).ShouldBe(1);
    }

    private async Task<string> InsertStore(string code, string name)
    {
        await fx.Execute("INSERT INTO app.areas (name, kind) SELECT 'StoreArea', 'Neighborhood' WHERE NOT EXISTS (SELECT 1 FROM app.areas);");
        await fx.Execute($"""
            INSERT INTO app.stores (name, area_id, referral_code)
            VALUES ('{name}', (SELECT min(id) FROM app.areas), '{code}');
            """);
        return name;
    }

    // AC4
    [Theory]
    [InlineData("VRACAR01", "vracar01")]
    [InlineData("ZEMUN01", "ZEMUN01")]
    [InlineData("NBG01", "  nbg01 ")]
    public async Task A_store_with_the_same_referral_code_in_any_case_or_spacing_is_refused(string first, string second)
    {
        await InsertStore(first, "First-" + first);
        var ex = await Should.ThrowAsync<PostgresException>(() => InsertStore(second, "Second-" + first));
        ex.SqlState.ShouldBe(UniqueViolation);
        (await Count($"SELECT count(*) FROM app.stores WHERE name LIKE '%-{first}'")).ShouldBe(1);
    }

    // AC4 edge
    [Fact]
    public async Task Different_referral_codes_are_allowed()
    {
        await InsertStore("DIFF01", "D1");
        await InsertStore("DIFF02", "D2");
    }

    // AC5
    [Fact]
    public async Task Job_numbers_are_assigned_automatically_unique_and_increasing()
    {
        var (_, _, j1) = await Seed();
        var (_, _, j2) = await Seed();
        var (_, _, j3) = await Seed();
        var n1 = await NumberOf(j1); var n2 = await NumberOf(j2); var n3 = await NumberOf(j3);
        n2.ShouldBeGreaterThan(n1);
        n3.ShouldBeGreaterThan(n2);
        var all = await fx.QueryStrings("SELECT number::text FROM app.jobs");
        all.Distinct().Count().ShouldBe(all.Count);
    }

    // AC5: numbers cannot be supplied; a failed insert only leaves a gap
    [Fact]
    public async Task Job_number_cannot_be_supplied_and_a_failed_insert_never_causes_a_duplicate()
    {
        var (client, _, job) = await Seed();
        var supplied = await Should.ThrowAsync<PostgresException>(() => fx.Execute($"""
            INSERT INTO app.jobs (id, number, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
            VALUES ('{G(777001)}', 1, '{client}', (SELECT max(id) FROM app.trades), 't', 'd', (SELECT max(id) FROM app.areas), 's', '1', 'Open', now());
            """));
        supplied.SqlState.ShouldBe("428C9"); // cannot insert into a generated-always identity column
        await Should.ThrowAsync<PostgresException>(() => fx.Execute($"""
            INSERT INTO app.jobs (id, client_id, trade_id, title, description, area_id, street, house_number, status, created_at)
            VALUES ('{G(777002)}', '{client}', -1, 't', 'd', -1, 's', '1', 'Open', now());
            """));
        var (_, _, later) = await Seed();
        (await NumberOf(later)).ShouldBeGreaterThan(await NumberOf(job));
    }
}
