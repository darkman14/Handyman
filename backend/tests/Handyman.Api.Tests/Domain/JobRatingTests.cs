using System.Reflection;
using Handyman.Api.Domain;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Tests.Features;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Tests.Domain;

public class JobRatingTests(SchemaFixture fx) : IClassFixture<SchemaFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
    private static int _seq = 7000;

    private readonly Guid _ivana = Guid.NewGuid(); // client
    private readonly Guid _jelena = Guid.NewGuid(); // hired worker

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    /// <summary>A job in the given status with Ivana as client and Jelena as hired worker (when hired).</summary>
    private (Job job, Bid accepted) JobIn(JobStatus status)
    {
        var job = new Job { Id = Guid.NewGuid(), ClientId = _ivana, Title = "Leaking tap" };
        var bid = new Bid { Id = Guid.NewGuid(), JobId = job.Id, WorkerId = _jelena };
        switch (status)
        {
            case JobStatus.Assigned or JobStatus.Done:
                job.Hire(bid, [bid]);
                if (status == JobStatus.Done) job.Finish(Now);
                break;
            case JobStatus.Cancelled:
                job.Cancel();
                break;
            case JobStatus.Closed:
                job.Hire(bid, [bid]);
                job.Finish(Now);
                Set(job, nameof(Job.Status), JobStatus.Closed);
                break;
        }
        return (job, bid);
    }

    private static (JobStatus, DateTimeOffset?) Snap(Job j) => (j.Status, j.ClosedAt);

    [Fact]
    public void Client_rating_a_done_job_records_it_for_the_worker_and_keeps_the_job_done()
    {
        var (job, bid) = JobIn(JobStatus.Done);

        var rating = job.AddRating(_ivana, 5, "Great", Now, bid, []);

        Assert.Equal(job.Id, rating.JobId);
        Assert.Equal(_ivana, rating.RaterId);
        Assert.Equal(_jelena, rating.RateeId);
        Assert.Equal(5, rating.Stars);
        Assert.Equal("Great", rating.Comment);
        Assert.Equal(JobStatus.Done, job.Status);
        Assert.Null(job.ClosedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rating_closes_only_when_both_exist(bool clientFirst)
    {
        var (job, bid) = JobIn(JobStatus.Done);
        var (first, second) = clientFirst ? (_ivana, _jelena) : (_jelena, _ivana);

        var r1 = job.AddRating(first, 4, null, Now, bid, []);
        Assert.Equal(JobStatus.Done, job.Status);
        Assert.Null(job.ClosedAt);

        var closeTime = new DateTimeOffset(2026, 11, 13, 14, 0, 0, TimeSpan.FromHours(2));
        var r2 = job.AddRating(second, 3, "", closeTime, bid, [r1]);

        Assert.Equal(first, r2.RateeId);
        Assert.Equal(second, r2.RaterId);
        Assert.Equal(JobStatus.Closed, job.Status);
        Assert.Equal(closeTime, job.ClosedAt);
        Assert.Equal(TimeSpan.Zero, job.ClosedAt!.Value.Offset);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Boundary_stars_are_accepted(int stars)
    {
        var (job, bid) = JobIn(JobStatus.Done);
        Assert.Equal(stars, job.AddRating(_ivana, stars, null, Now, bid, []).Stars);
    }

    [Fact]
    public void Missing_and_empty_comments_are_both_allowed()
    {
        var (job, bid) = JobIn(JobStatus.Done);
        Assert.Null(job.AddRating(_ivana, 4, null, Now, bid, []).Comment);
        Assert.Equal("", job.AddRating(_jelena, 4, "", Now, bid, []).Comment);
    }

    [Fact]
    public void Non_participant_is_refused_and_nothing_changes()
    {
        var (job, bid) = JobIn(JobStatus.Done);
        var existing = job.AddRating(_ivana, 5, null, Now, bid, []);
        var before = Snap(job);

        var notHired = Guid.NewGuid(); // a bidder who was not hired, or a stranger
        Assert.Throws<DomainException>(() => job.AddRating(notHired, 5, null, Now, bid, [existing]));

        Assert.Equal(before, Snap(job));
    }

    [Fact]
    public void Second_rating_by_the_same_rater_is_refused_and_first_stays()
    {
        var (job, bid) = JobIn(JobStatus.Done);
        var first = job.AddRating(_ivana, 2, "first", Now, bid, []);

        Assert.Throws<DomainException>(() => job.AddRating(_ivana, 5, "again", Now, bid, [first]));

        Assert.Equal(2, first.Stars);
        Assert.Equal("first", first.Comment);
        Assert.Equal(JobStatus.Done, job.Status);
    }

    [Fact]
    public void Rating_again_after_the_job_closed_is_refused()
    {
        var (job, bid) = JobIn(JobStatus.Done);
        var r1 = job.AddRating(_ivana, 5, null, Now, bid, []);
        var r2 = job.AddRating(_jelena, 5, null, Now.AddHours(1), bid, [r1]);
        var closedAt = job.ClosedAt;

        Assert.Throws<DomainException>(() => job.AddRating(_ivana, 1, null, Now.AddDays(1), bid, [r1, r2]));

        Assert.Equal(JobStatus.Closed, job.Status);
        Assert.Equal(closedAt, job.ClosedAt);
    }

    [Theory]
    [InlineData(JobStatus.Open)]
    [InlineData(JobStatus.Assigned)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Cancelled)]
    public void Rating_a_job_that_is_not_done_is_refused_and_changes_nothing(JobStatus status)
    {
        var (job, bid) = JobIn(status);
        var before = Snap(job);

        Assert.Throws<DomainException>(() => job.AddRating(_ivana, 5, null, Now, bid, []));

        Assert.Equal(before, Snap(job));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Stars_outside_one_to_five_are_refused_and_changes_nothing(int stars)
    {
        var (job, bid) = JobIn(JobStatus.Done);
        var other = job.AddRating(_jelena, 4, null, Now, bid, []);
        var before = Snap(job);

        Assert.Throws<DomainException>(() => job.AddRating(_ivana, stars, null, Now, bid, [other]));

        Assert.Equal(before, Snap(job)); // would have closed the job if it had been accepted
    }

    [Fact]
    public void A_bid_that_is_not_the_accepted_one_is_refused()
    {
        var (job, _) = JobIn(JobStatus.Done);
        var loser = new Bid { Id = Guid.NewGuid(), JobId = job.Id, WorkerId = Guid.NewGuid() };

        Assert.Throws<DomainException>(() => job.AddRating(loser.WorkerId, 5, null, Now, loser, []));
        Assert.Throws<DomainException>(() => job.AddRating(_ivana, 5, null, Now, loser, []));
    }

    [Fact]
    public void A_rating_of_another_job_in_the_list_is_refused()
    {
        var (job, bid) = JobIn(JobStatus.Done);
        var foreign = new Rating { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), RaterId = _jelena, RateeId = _ivana, Stars = 5 };

        Assert.Throws<DomainException>(() => job.AddRating(_ivana, 5, null, Now, bid, [foreign]));

        Assert.Equal(JobStatus.Done, job.Status);
    }

    [Fact]
    public async Task Ratings_and_the_closed_job_survive_save_and_reload_on_postgres()
    {
        var n = Interlocked.Increment(ref _seq);
        var client = new Guid($"00000000-0000-0000-0000-{n:D12}");
        var worker = new Guid($"00000000-0000-0000-0001-{n:D12}");
        var jobId = new Guid($"00000000-0000-0000-0002-{n:D12}");
        await SeedAndRate(n, client, worker, jobId);
    }

    private async Task SeedAndRate(int n, Guid client, Guid worker, Guid jobId)
    {
        await fx.Execute($"""
            INSERT INTO app.users (id, phone, role, name, rating_sum, rating_count) VALUES
              ('{client}', '+3816{n:D8}', 'Client', 'Ivana', 0, 0),
              ('{worker}', '+3817{n:D8}', 'Worker', 'Jelena', 0, 0);
            INSERT INTO app.trades (name) VALUES ('T14_{n}');
            INSERT INTO app.areas (name, kind) VALUES ('A14_{n}', 'Neighborhood');
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

        // The handler's recipe: load job, accepted bid and the job's ratings, call AddRating, add the result, save.
        foreach (var rater in new[] { client, worker })
        {
            await using var db = fx.NewContext();
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId);
            var accepted = await db.Bids.SingleAsync(b => b.Id == job.AcceptedBidId);
            var ratings = await db.Ratings.Where(r => r.JobId == jobId).ToListAsync();
            db.Ratings.Add(job.AddRating(rater, rater == client ? 5 : 4, "ok", Now.AddHours(2), accepted, ratings));
            await db.SaveChangesAsync();
            if (rater == client)
                Assert.Equal(JobStatus.Done, (await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId)).Status);
        }

        await using (var db = fx.NewContext())
        {
            var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
            Assert.Equal(JobStatus.Closed, job.Status);
            Assert.Equal(Now.AddHours(2), job.ClosedAt);
            var ratings = await db.Ratings.AsNoTracking().Where(r => r.JobId == jobId).ToListAsync();
            Assert.Equal(2, ratings.Count);
            var byClient = ratings.Single(r => r.RaterId == client);
            Assert.Equal(worker, byClient.RateeId);
            Assert.Equal(5, byClient.Stars);
            var byWorker = ratings.Single(r => r.RaterId == worker);
            Assert.Equal(client, byWorker.RateeId);
            Assert.Equal(4, byWorker.Stars);
        }
    }
}
