using System.Reflection;
using Handyman.Api.Domain;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Features;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

/// <summary>US-0013 QA: domain edge cases beyond JobTests, plus EF persistence against real Postgres.</summary>
public class US_0013_JobLifecycleTests(SchemaFixture fx) : IClassFixture<SchemaFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static int _seq = 5000;

    private static Job NewJob() => new() { Id = Guid.NewGuid(), Title = "Leaking tap (Ana)" };

    private static Bid NewBid(Job job, BidStatus status = BidStatus.Active)
    {
        var bid = new Bid { Id = Guid.NewGuid(), JobId = job.Id, WorkerId = Guid.NewGuid() };
        SetBidStatus(bid, status);
        return bid;
    }

    private static void SetBidStatus(Bid bid, BidStatus s) =>
        typeof(Bid).GetProperty(nameof(Bid.Status), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bid, s);

    private static void SetJob(Job job, string prop, object? v) =>
        typeof(Job).GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(job, v);

    private static Job JobIn(JobStatus status, out Bid? accepted)
    {
        var job = NewJob(); accepted = null;
        if (status is JobStatus.Assigned or JobStatus.Done)
        {
            accepted = NewBid(job); var b = accepted;
            job.Hire(b, [b]);
            if (status == JobStatus.Done) job.Finish(Now);
        }
        if (status == JobStatus.Cancelled) job.Cancel();
        if (status == JobStatus.Closed) SetJob(job, nameof(Job.Status), JobStatus.Closed);
        return job;
    }

    private static (JobStatus, Guid?, DateTimeOffset?, DateTimeOffset?) Snap(Job j) => (j.Status, j.AcceptedBidId, j.FinishedAt, j.ClosedAt);

    // AC3: every non-Open status refuses Hire, nothing changes
    [Theory]
    [InlineData(JobStatus.Assigned)]
    [InlineData(JobStatus.Done)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Cancelled)]
    public void Hire_is_refused_unchanged_for_every_non_open_status(JobStatus status)
    {
        var job = JobIn(status, out var existing);
        var fresh = NewBid(job);
        var rival = NewBid(job);
        var before = Snap(job);
        var existingBefore = existing?.Status;

        Should.Throw<DomainException>(() => job.Hire(fresh, [fresh, rival, .. existing is null ? [] : new[] { existing }]));

        Snap(job).ShouldBe(before);
        fresh.Status.ShouldBe(BidStatus.Active);
        rival.Status.ShouldBe(BidStatus.Active);
        existing?.Status.ShouldBe(existingBefore!.Value);
    }

    // AC3: every non-Active bid status refuses Hire
    [Theory]
    [InlineData(BidStatus.Accepted)]
    [InlineData(BidStatus.Rejected)]
    [InlineData(BidStatus.Withdrawn)]
    public void Hire_is_refused_for_a_bid_that_is_not_active_and_other_bids_stay(BidStatus status)
    {
        var job = NewJob();
        var bad = NewBid(job, status);
        var other = NewBid(job);

        Should.Throw<DomainException>(() => job.Hire(bad, [bad, other]));

        job.Status.ShouldBe(JobStatus.Open);
        job.AcceptedBidId.ShouldBeNull();
        bad.Status.ShouldBe(status);
        other.Status.ShouldBe(BidStatus.Active);
    }

    // AC3: foreign bid as the hired one
    [Fact]
    public void Hire_is_refused_for_a_bid_of_another_job()
    {
        var job = NewJob(); var other = NewJob();
        var mine = NewBid(job); var foreign = NewBid(other);

        Should.Throw<DomainException>(() => job.Hire(foreign, [foreign, mine]));

        job.Status.ShouldBe(JobStatus.Open);
        foreign.Status.ShouldBe(BidStatus.Active);
        mine.Status.ShouldBe(BidStatus.Active);
    }

    // AC3 atomicity: a foreign bid hidden in the list must not leave half-done state
    [Fact]
    public void Hire_with_a_foreign_bid_in_the_list_changes_nothing_not_even_the_hired_bid()
    {
        var job = NewJob(); var other = NewJob();
        var jelena = NewBid(job); var nikola = NewBid(job); var foreign = NewBid(other);

        Should.Throw<DomainException>(() => job.Hire(jelena, [nikola, foreign, jelena]));

        job.Status.ShouldBe(JobStatus.Open);
        job.AcceptedBidId.ShouldBeNull();
        jelena.Status.ShouldBe(BidStatus.Active);
        nikola.Status.ShouldBe(BidStatus.Active);
        foreign.Status.ShouldBe(BidStatus.Active);
    }

    // AC2 edge: duplicates in the list, same instance twice
    [Fact]
    public void Hire_tolerates_duplicate_entries_in_the_bid_list()
    {
        var job = NewJob();
        var jelena = NewBid(job); var nikola = NewBid(job);

        job.Hire(jelena, [jelena, nikola, nikola, jelena]);

        job.AcceptedBidId.ShouldBe(jelena.Id);
        jelena.Status.ShouldBe(BidStatus.Accepted);
        nikola.Status.ShouldBe(BidStatus.Rejected);
    }

    // AC2 edge: chosen bid not part of the list (caller passes only the others)
    [Fact]
    public void Hire_works_when_the_chosen_bid_is_missing_from_the_list()
    {
        var job = NewJob();
        var jelena = NewBid(job); var nikola = NewBid(job);

        job.Hire(jelena, [nikola]);

        job.Status.ShouldBe(JobStatus.Assigned);
        jelena.Status.ShouldBe(BidStatus.Accepted);
        nikola.Status.ShouldBe(BidStatus.Rejected);
    }

    // AC2 edge: empty list
    [Fact]
    public void Hire_works_with_an_empty_list()
    {
        var job = NewJob(); var jelena = NewBid(job);
        job.Hire(jelena, []);
        job.Status.ShouldBe(JobStatus.Assigned);
        jelena.Status.ShouldBe(BidStatus.Accepted);
    }

    // AC2 edge: rejected and previously accepted-looking bids in the list are left alone
    [Fact]
    public void Hire_leaves_already_rejected_and_withdrawn_bids_as_they_were()
    {
        var job = NewJob();
        var jelena = NewBid(job); var rejected = NewBid(job, BidStatus.Rejected); var withdrawn = NewBid(job, BidStatus.Withdrawn);

        job.Hire(jelena, [jelena, rejected, withdrawn]);

        rejected.Status.ShouldBe(BidStatus.Rejected);
        withdrawn.Status.ShouldBe(BidStatus.Withdrawn);
    }

    // AC3: null args fail before any mutation
    [Fact]
    public void Hire_with_null_arguments_throws_and_changes_nothing()
    {
        var job = NewJob(); var jelena = NewBid(job);

        Should.Throw<Exception>(() => job.Hire(null!, [jelena]));
        Should.Throw<Exception>(() => job.Hire(jelena, null!));

        job.Status.ShouldBe(JobStatus.Open);
        job.AcceptedBidId.ShouldBeNull();
        jelena.Status.ShouldBe(BidStatus.Active);
    }

    // AC3: hire the same bid twice and hire another bid after a hire
    [Fact]
    public void Second_hire_on_the_same_or_another_bid_is_refused_and_first_hire_stands()
    {
        var job = NewJob();
        var jelena = NewBid(job); var nikola = NewBid(job);
        job.Hire(jelena, [jelena, nikola]);

        Should.Throw<DomainException>(() => job.Hire(jelena, [jelena, nikola]));
        Should.Throw<DomainException>(() => job.Hire(nikola, [jelena, nikola]));

        job.AcceptedBidId.ShouldBe(jelena.Id);
        jelena.Status.ShouldBe(BidStatus.Accepted);
        nikola.Status.ShouldBe(BidStatus.Rejected);
    }

    // AC4 matrix
    [Theory]
    [InlineData(JobStatus.Open)]
    [InlineData(JobStatus.Done)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Cancelled)]
    public void Finish_is_refused_in_every_status_but_assigned_and_changes_nothing(JobStatus status)
    {
        var job = JobIn(status, out _);
        var before = Snap(job);
        Should.Throw<DomainException>(() => job.Finish(Now.AddDays(1)));
        Snap(job).ShouldBe(before);
    }

    // AC4: UTC conversion
    [Fact]
    public void Finish_stores_utc_for_non_utc_input_at_the_same_instant()
    {
        var job = JobIn(JobStatus.Assigned, out _);
        var local = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.FromHours(2));

        job.Finish(local);

        job.FinishedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
        job.FinishedAt.Value.ShouldBe(local);
        job.FinishedAt.Value.UtcDateTime.ShouldBe(new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc));
    }

    // AC5 matrix
    [Theory]
    [InlineData(JobStatus.Done)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Cancelled)]
    public void Cancel_is_refused_for_done_closed_and_cancelled_and_changes_nothing(JobStatus status)
    {
        var job = JobIn(status, out var bid);
        var before = Snap(job);
        var bidBefore = bid?.Status;
        Should.Throw<DomainException>(job.Cancel);
        Snap(job).ShouldBe(before);
        bid?.Status.ShouldBe(bidBefore!.Value);
    }

    // AC5: cancelled job cannot be hired or finished; cancel keeps all bid statuses
    [Fact]
    public void Cancelled_assigned_job_keeps_bid_statuses_and_cannot_be_finished_or_hired()
    {
        var job = NewJob();
        var jelena = NewBid(job); var nikola = NewBid(job); var withdrawn = NewBid(job, BidStatus.Withdrawn);
        job.Hire(jelena, [jelena, nikola, withdrawn]);

        job.Cancel();

        job.Status.ShouldBe(JobStatus.Cancelled);
        job.AcceptedBidId.ShouldBe(jelena.Id);
        job.FinishedAt.ShouldBeNull();
        job.ClosedAt.ShouldBeNull();
        jelena.Status.ShouldBe(BidStatus.Accepted);
        nikola.Status.ShouldBe(BidStatus.Rejected);
        withdrawn.Status.ShouldBe(BidStatus.Withdrawn);
        Should.Throw<DomainException>(() => job.Finish(Now));
        Should.Throw<DomainException>(() => job.Hire(nikola, [jelena, nikola]));
    }

    // AC5: open job with active bids is cancelled and bids stay active
    [Fact]
    public void Cancelled_open_job_leaves_active_bids_active()
    {
        var job = NewJob(); var a = NewBid(job);
        job.Cancel();
        job.Status.ShouldBe(JobStatus.Cancelled);
        a.Status.ShouldBe(BidStatus.Active);
    }

    // Encapsulation: no public setter on lifecycle state
    [Fact]
    public void Lifecycle_state_has_no_public_setters()
    {
        foreach (var name in new[] { nameof(Job.Status), nameof(Job.AcceptedBidId), nameof(Job.FinishedAt), nameof(Job.ClosedAt) })
            typeof(Job).GetProperty(name)!.SetMethod!.IsPublic.ShouldBeFalse(name);
        typeof(Bid).GetProperty(nameof(Bid.Status))!.SetMethod!.IsPublic.ShouldBeFalse();
    }

    // ---- Persistence with real Postgres ----

    private async Task<(Guid client, Guid worker, Guid job)> SeedParents()
    {
        var n = Interlocked.Increment(ref _seq);
        var client = new Guid($"00000000-0000-0000-0000-{n:D12}");
        var worker = new Guid($"00000000-0000-0000-0001-{n:D12}");
        var job = new Guid($"00000000-0000-0000-0002-{n:D12}");
        await fx.Execute($"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES
              ('{client}', '+3816{n:D8}', 'Client', 'Ana', 0, 0),
              ('{worker}', '+3817{n:D8}', 'Worker', 'Jelena', 0, 0);
            INSERT INTO app.trades (name) VALUES ('T13_{n}');
            INSERT INTO app.areas (name, kind) VALUES ('A13_{n}', 'Neighborhood');
            """);
        return (client, worker, job);
    }

    private async Task<(Guid job, Guid jelena, Guid nikola, Guid withdrawn)> SeedJobWithBidsViaEf()
    {
        var (client, w1, jobId) = await SeedParents();
        var n = _seq;
        var w2 = new Guid($"00000000-0000-0000-0003-{n:D12}");
        var w3 = new Guid($"00000000-0000-0000-0004-{n:D12}");
        await fx.Execute($"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES
              ('{w2}', '+3818{n:D8}', 'Worker', 'Nikola', 0, 0),
              ('{w3}', '+3819{n:D8}', 'Worker', 'Marko', 0, 0);
            """);
        var tradeId = int.Parse((await fx.QueryStrings("SELECT max(id)::text FROM app.trades")).Single());
        var areaId = int.Parse((await fx.QueryStrings("SELECT max(id)::text FROM app.areas")).Single());

        await using var db = fx.NewContext();
        db.Jobs.Add(new Job { Id = jobId, ClientId = client, TradeId = tradeId, AreaId = areaId, Title = "t", Description = "d", Street = "s", HouseNumber = "1" });
        Bid B(Guid w) => new() { Id = Guid.NewGuid(), JobId = jobId, WorkerId = w, ArrivalWindowStart = Now, ArrivalWindowEnd = Now.AddHours(1), CreatedAt = Now };
        var jelena = B(w1); var nikola = B(w2); var withdrawn = B(w3);
        SetBidStatus(withdrawn, BidStatus.Withdrawn);
        db.Bids.AddRange(jelena, nikola, withdrawn);
        await db.SaveChangesAsync();
        return (jobId, jelena.Id, nikola.Id, withdrawn.Id);
    }

    // AC1 via DB
    [Fact]
    public async Task Persisted_new_job_reloads_open_with_utc_creation_time_and_no_accepted_bid_finish_or_close()
    {
        var (job, _, _, _) = await SeedJobWithBidsViaEf();
        await using var db = fx.NewContext();
        var j = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == job);
        j.Status.ShouldBe(JobStatus.Open);
        j.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        j.AcceptedBidId.ShouldBeNull();
        j.FinishedAt.ShouldBeNull();
        j.ClosedAt.ShouldBeNull();
        j.Number.ShouldBeGreaterThan(0);
    }

    // AC2 + AC4 via DB: hire, save, reload; finish, save, reload
    [Fact]
    public async Task Hire_then_finish_survive_save_and_reload_on_postgres()
    {
        var (jobId, jelena, nikola, withdrawn) = await SeedJobWithBidsViaEf();

        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.SingleAsync(x => x.Id == jobId);
            var bids = await db.Bids.Where(b => b.JobId == jobId).ToListAsync();
            job.Hire(bids.Single(b => b.Id == jelena), bids);
            await db.SaveChangesAsync();
        }

        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
            job.Status.ShouldBe(JobStatus.Assigned);
            job.AcceptedBidId.ShouldBe(jelena);
            var bids = await db.Bids.AsNoTracking().Where(b => b.JobId == jobId).ToDictionaryAsync(b => b.Id, b => b.Status);
            bids[jelena].ShouldBe(BidStatus.Accepted);
            bids[nikola].ShouldBe(BidStatus.Rejected);
            bids[withdrawn].ShouldBe(BidStatus.Withdrawn);
        }
        (await fx.QueryStrings($"SELECT status || '|' || accepted_bid_id FROM app.jobs WHERE id = '{jobId}'"))
            .Single().ShouldBe($"Assigned|{jelena}");

        var local = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.FromHours(2));
        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.SingleAsync(x => x.Id == jobId);
            job.Finish(local);
            await db.SaveChangesAsync();
        }
        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
            job.Status.ShouldBe(JobStatus.Done);
            job.FinishedAt.ShouldBe(local);
            job.FinishedAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
            job.ClosedAt.ShouldBeNull();
        }
    }

    // AC5 via DB
    [Fact]
    public async Task Cancel_after_hire_persists_cancelled_and_leaves_bid_rows_unchanged()
    {
        var (jobId, jelena, nikola, withdrawn) = await SeedJobWithBidsViaEf();
        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.SingleAsync(x => x.Id == jobId);
            var bids = await db.Bids.Where(b => b.JobId == jobId).ToListAsync();
            job.Hire(bids.Single(b => b.Id == jelena), bids);
            await db.SaveChangesAsync();
        }
        await using (var db = fx.NewContext())
        {
            (await db.Jobs.SingleAsync(x => x.Id == jobId)).Cancel();
            await db.SaveChangesAsync();
        }
        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId);
            job.Status.ShouldBe(JobStatus.Cancelled);
            job.AcceptedBidId.ShouldBe(jelena);
            var bids = await db.Bids.AsNoTracking().Where(b => b.JobId == jobId).ToDictionaryAsync(b => b.Id, b => b.Status);
            bids[jelena].ShouldBe(BidStatus.Accepted);
            bids[nikola].ShouldBe(BidStatus.Rejected);
            bids[withdrawn].ShouldBe(BidStatus.Withdrawn);
        }
    }

    // AC3 via DB: refused hire leaves rows unchanged even if SaveChanges is called afterwards
    [Fact]
    public async Task Refused_hire_followed_by_save_changes_writes_nothing()
    {
        var (jobId, jelena, nikola, withdrawn) = await SeedJobWithBidsViaEf();
        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.SingleAsync(x => x.Id == jobId);
            var bids = await db.Bids.Where(b => b.JobId == jobId).ToListAsync();
            Should.Throw<DomainException>(() => job.Hire(bids.Single(b => b.Id == withdrawn), bids));
            (await db.SaveChangesAsync()).ShouldBe(0);
        }
        await using (var db = fx.NewContext())
        {
            (await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId)).Status.ShouldBe(JobStatus.Open);
            var bids = await db.Bids.AsNoTracking().Where(b => b.JobId == jobId).ToDictionaryAsync(b => b.Id, b => b.Status);
            bids[jelena].ShouldBe(BidStatus.Active);
            bids[nikola].ShouldBe(BidStatus.Active);
            bids[withdrawn].ShouldBe(BidStatus.Withdrawn);
        }
    }

    // No model change: EF model has no pending changes vs. the migrations snapshot
    [Fact]
    public void Ef_model_has_no_pending_changes_against_the_migration_snapshot()
    {
        using var db = fx.NewContext();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
