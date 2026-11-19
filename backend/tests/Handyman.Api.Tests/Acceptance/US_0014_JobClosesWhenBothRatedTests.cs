using Handyman.Api.Domain;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Features;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0014 (domain level, no endpoints): QA regression tests on Job.AddRating, incl. real Postgres.</summary>
public class US_0014_JobClosesWhenBothRatedTests(SchemaFixture fx) : IClassFixture<SchemaFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static int _seq = 14000;

    private static (Job job, Bid bid, Guid client, Guid worker) DoneJob()
    {
        var client = Guid.NewGuid(); var worker = Guid.NewGuid();
        var job = new Job { Id = Guid.NewGuid(), ClientId = client };
        var bid = new Bid { Id = Guid.NewGuid(), JobId = job.Id, WorkerId = worker };
        job.Hire(bid, [bid]);
        job.Finish(Now);
        return (job, bid, client, worker);
    }

    // AC1 + AC2: both orders, ratee, UTC
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_orders_close_the_job_with_correct_ratees_and_utc_close_time(bool clientFirst)
    {
        var (job, bid, client, worker) = DoneJob();
        var (a, b) = clientFirst ? (client, worker) : (worker, client);
        var list = new List<Rating>();
        var r1 = job.AddRating(a, 5, "x", Now, bid, list);
        job.Status.ShouldBe(JobStatus.Done);
        job.ClosedAt.ShouldBeNull();
        r1.RateeId.ShouldBe(b);
        list.Add(r1);
        var t = new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.FromHours(-5));
        var r2 = job.AddRating(b, 1, null, t, bid, list);
        r2.RateeId.ShouldBe(a);
        job.Status.ShouldBe(JobStatus.Closed);
        job.ClosedAt.ShouldBe(t);
        job.ClosedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        r2.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    // AC3
    [Fact]
    public void Strangers_other_bidders_and_empty_guid_are_refused_and_the_callers_list_is_untouched()
    {
        var (job, bid, client, worker) = DoneJob();
        var r1 = job.AddRating(client, 5, null, Now, bid, []);
        var list = new List<Rating> { r1 };
        foreach (var who in new[] { Guid.NewGuid(), Guid.Empty, new Bid().WorkerId })
        {
            Should.Throw<DomainException>(() => job.AddRating(who, 5, null, Now, bid, list));
        }
        job.Status.ShouldBe(JobStatus.Done);
        job.ClosedAt.ShouldBeNull();
        list.Count.ShouldBe(1);
    }

    // AC4
    [Fact]
    public void Same_rater_twice_is_refused_in_both_directions_and_first_rating_is_unchanged()
    {
        var (job, bid, client, worker) = DoneJob();
        var first = job.AddRating(worker, 3, "orig", Now, bid, []);
        Should.Throw<DomainException>(() => job.AddRating(worker, 5, "dup", Now, bid, [first]));
        first.Stars.ShouldBe(3);
        first.Comment.ShouldBe("orig");
        job.Status.ShouldBe(JobStatus.Done);
    }

    // AC5
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void Invalid_stars_are_refused_for_both_parties(int stars)
    {
        var (job, bid, client, worker) = DoneJob();
        Should.Throw<DomainException>(() => job.AddRating(client, stars, null, Now, bid, []));
        Should.Throw<DomainException>(() => job.AddRating(worker, stars, null, Now, bid, []));
        job.Status.ShouldBe(JobStatus.Done);
    }

    [Fact]
    public void Comment_null_empty_whitespace_and_very_long_are_accepted_by_the_domain()
    {
        var (job, bid, client, _) = DoneJob();
        foreach (var c in new string?[] { null, "", "   ", new string('x', 100_000) })
            job.AddRating(client, 3, c, Now, bid, []).Comment.ShouldBe(c);
    }

    // AC5 edge: bad accepted bid wiring
    [Fact]
    public void Accepted_bid_that_is_null_foreign_or_when_no_bid_is_accepted_is_handled()
    {
        var (job, bid, client, _) = DoneJob();
        var foreignBid = new Bid { Id = bid.Id, JobId = Guid.NewGuid(), WorkerId = bid.WorkerId };
        Should.Throw<DomainException>(() => job.AddRating(client, 5, null, Now, foreignBid, []));

        var open = new Job { Id = Guid.NewGuid(), ClientId = client }; // AcceptedBidId null, Open
        Should.Throw<DomainException>(() => open.AddRating(client, 5, null, Now, bid, []));
    }

    [Fact]
    public void Null_arguments_throw_and_change_nothing()
    {
        var (job, bid, client, _) = DoneJob();
        Should.Throw<Exception>(() => job.AddRating(client, 5, null, Now, null!, []));
        Should.Throw<Exception>(() => job.AddRating(client, 5, null, Now, bid, null!));
        job.Status.ShouldBe(JobStatus.Done);
    }

    // Persistence: unique index backs the rule, concurrent double submit by one rater
    [Fact]
    public async Task Concurrent_double_submit_by_the_same_rater_leaves_exactly_one_row()
    {
        var (client, worker, jobId) = await Seed();
        async Task<bool> Submit(int stars)
        {
            await using var db = fx.NewContext();
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            var acc = await db.Bids.SingleAsync(b => b.Id == job.AcceptedBidId);
            var ratings = await db.Ratings.Where(r => r.JobId == jobId).ToListAsync();
            db.Ratings.Add(job.AddRating(client, stars, null, Now, acc, ratings));
            await gate.SignalAndWait();
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException) { return false; }
        }
        gate = new Gate(2);
        var results = await Task.WhenAll(Submit(5), Submit(1));
        results.Count(r => r).ShouldBe(1);
        (await fx.QueryStrings($"SELECT count(*)::text FROM app.ratings WHERE job_id = '{jobId}'")).Single().ShouldBe("1");
    }

    // Known risk for E07: two concurrent FIRST ratings by different raters both see an empty list.
    // Documents current behavior: both rows are stored, the job stays Done (stuck). E07 must prevent this.
    [Fact]
    public async Task Concurrent_first_ratings_by_both_sides_can_leave_the_job_stuck_in_done_RISK_for_E07()
    {
        var (client, worker, jobId) = await Seed();
        gate = new Gate(2);
        async Task Submit(Guid rater)
        {
            await using var db = fx.NewContext();
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            var acc = await db.Bids.SingleAsync(b => b.Id == job.AcceptedBidId);
            var ratings = await db.Ratings.Where(r => r.JobId == jobId).ToListAsync();
            db.Ratings.Add(job.AddRating(rater, 4, null, Now, acc, ratings));
            await gate.SignalAndWait(); // both have read the empty list
            await db.SaveChangesAsync();
        }
        await Task.WhenAll(Submit(client), Submit(worker));
        (await fx.QueryStrings($"SELECT count(*)::text FROM app.ratings WHERE job_id = '{jobId}'")).Single().ShouldBe("2");
        (await fx.QueryStrings($"SELECT status FROM app.jobs WHERE id = '{jobId}'")).Single().ShouldBe("Done");
    }

    // Mitigation check: locking the job row (SELECT ... FOR UPDATE) in a transaction serializes the handlers.
    [Fact]
    public async Task Locking_the_job_row_in_a_transaction_closes_the_job_even_with_simultaneous_submits()
    {
        var (client, worker, jobId) = await Seed();
        async Task Submit(Guid rater)
        {
            await using var db = fx.NewContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var job = await db.Jobs.FromSqlRaw("SELECT * FROM app.jobs WHERE id = {0} FOR UPDATE", jobId).SingleAsync();
            var acc = await db.Bids.SingleAsync(b => b.Id == job.AcceptedBidId);
            var ratings = await db.Ratings.Where(r => r.JobId == jobId).ToListAsync();
            db.Ratings.Add(job.AddRating(rater, 4, null, Now, acc, ratings));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await Task.WhenAll(Submit(client), Submit(worker));
        (await fx.QueryStrings($"SELECT status FROM app.jobs WHERE id = '{jobId}'")).Single().ShouldBe("Closed");
        (await fx.QueryStrings($"SELECT count(*)::text FROM app.ratings WHERE job_id = '{jobId}'")).Single().ShouldBe("2");
    }

    // Refused rating persists nothing
    [Fact]
    public async Task A_refused_rating_stores_nothing_and_does_not_touch_the_job_row()
    {
        var (client, worker, jobId) = await Seed();
        await using var db = fx.NewContext();
        var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
        var acc = await db.Bids.SingleAsync(b => b.Id == job.AcceptedBidId);
        Should.Throw<DomainException>(() => job.AddRating(client, 9, null, Now, acc, []));
        Should.Throw<DomainException>(() => job.AddRating(Guid.NewGuid(), 5, null, Now, acc, []));
        await db.SaveChangesAsync();
        (await fx.QueryStrings($"SELECT count(*)::text FROM app.ratings WHERE job_id = '{jobId}'")).Single().ShouldBe("0");
        (await fx.QueryStrings($"SELECT status FROM app.jobs WHERE id = '{jobId}'")).Single().ShouldBe("Done");
    }

    private Gate gate = new(1);

    private sealed class Gate(int n)
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _left = n;
        public Task SignalAndWait()
        {
            if (Interlocked.Decrement(ref _left) == 0) _tcs.SetResult();
            return _tcs.Task;
        }
    }

    private async Task<(Guid client, Guid worker, Guid jobId)> Seed()
    {
        var n = Interlocked.Increment(ref _seq);
        var client = new Guid($"00000000-0000-0000-0014-{n:D12}");
        var worker = new Guid($"00000000-0000-0000-0114-{n:D12}");
        var jobId = new Guid($"00000000-0000-0000-0214-{n:D12}");
        await fx.Execute($"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES
              ('{client}', '+3816{n:D8}', 'Client', 'Ivana', 0, 0),
              ('{worker}', '+3817{n:D8}', 'Worker', 'Jelena', 0, 0);
            INSERT INTO app.trades (name) VALUES ('QT{n}');
            INSERT INTO app.areas (name, kind) VALUES ('QA{n}', 'Neighborhood');
            """);
        var tradeId = int.Parse((await fx.QueryStrings("SELECT max(id)::text FROM app.trades")).Single());
        var areaId = int.Parse((await fx.QueryStrings("SELECT max(id)::text FROM app.areas")).Single());
        await using (var db = fx.NewContext())
        {
            db.Jobs.Add(new Job { Id = jobId, ClientId = client, TradeId = tradeId, AreaId = areaId, Title = "t", Description = "d", Street = "s", HouseNumber = "1" });
            db.Bids.Add(new Bid { Id = Guid.NewGuid(), JobId = jobId, WorkerId = worker, ArrivalWindowStart = Now, ArrivalWindowEnd = Now.AddHours(1), CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            var bids = await db.Bids.Where(b => b.JobId == jobId).ToListAsync();
            job.Hire(bids[0], bids);
            job.Finish(Now);
            await db.SaveChangesAsync();
        }
        return (client, worker, jobId);
    }
}
