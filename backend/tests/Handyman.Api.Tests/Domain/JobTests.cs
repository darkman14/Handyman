using System.Reflection;
using Handyman.Api.Domain;
using Handyman.Api.Domain.Entities;

namespace Handyman.Api.Tests.Domain;

public class JobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static Job NewJob() => new() { Id = Guid.NewGuid(), Title = "Leaking tap (Ana)" };

    private static Bid NewBid(Job job, BidStatus status = BidStatus.Active)
    {
        var bid = new Bid { Id = Guid.NewGuid(), JobId = job.Id, WorkerId = Guid.NewGuid() };
        Set(bid, nameof(Bid.Status), status);
        return bid;
    }

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static Job JobIn(JobStatus status)
    {
        var job = NewJob();
        switch (status)
        {
            case JobStatus.Assigned or JobStatus.Done:
                var bid = NewBid(job);
                job.Hire(bid, [bid]);
                if (status == JobStatus.Done) job.Finish(Now);
                break;
            case JobStatus.Cancelled:
                job.Cancel();
                break;
            case JobStatus.Closed:
                Set(job, nameof(Job.Status), JobStatus.Closed); // closing comes with US-0014
                break;
        }
        return job;
    }

    [Fact]
    public void New_job_is_open_with_utc_creation_time_and_nothing_else_set()
    {
        var job = NewJob();

        Assert.Equal(JobStatus.Open, job.Status);
        Assert.Equal(TimeSpan.Zero, job.CreatedAt.Offset);
        Assert.NotEqual(default, job.CreatedAt);
        Assert.Null(job.AcceptedBidId);
        Assert.Null(job.FinishedAt);
        Assert.Null(job.ClosedAt);
    }

    [Fact]
    public void Hiring_a_bid_assigns_the_job_accepts_it_and_rejects_other_active_bids()
    {
        var job = NewJob();
        var jelena = NewBid(job);
        var nikola = NewBid(job);
        var withdrawn = NewBid(job, BidStatus.Withdrawn);

        job.Hire(jelena, [jelena, nikola, withdrawn]);

        Assert.Equal(JobStatus.Assigned, job.Status);
        Assert.Equal(jelena.Id, job.AcceptedBidId);
        Assert.Equal(BidStatus.Accepted, jelena.Status);
        Assert.Equal(BidStatus.Rejected, nikola.Status);
        Assert.Equal(BidStatus.Withdrawn, withdrawn.Status);
    }

    [Fact]
    public void Hiring_the_only_bid_works_when_there_are_no_other_bids()
    {
        var job = NewJob();
        var only = NewBid(job);

        job.Hire(only, [only]);

        Assert.Equal(JobStatus.Assigned, job.Status);
        Assert.Equal(BidStatus.Accepted, only.Status);
    }

    [Theory]
    [InlineData(JobStatus.Assigned)]
    [InlineData(JobStatus.Done)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Cancelled)]
    public void Hiring_on_a_job_that_is_not_open_is_refused_and_changes_nothing(JobStatus status)
    {
        var job = JobIn(status);
        var acceptedBefore = job.AcceptedBidId;
        var bid = NewBid(job);
        var other = NewBid(job);

        Assert.Throws<DomainException>(() => job.Hire(bid, [bid, other]));

        Assert.Equal(status, job.Status);
        Assert.Equal(acceptedBefore, job.AcceptedBidId);
        Assert.Equal(BidStatus.Active, bid.Status);
        Assert.Equal(BidStatus.Active, other.Status);
    }

    [Fact]
    public void Hiring_a_bid_of_another_job_is_refused_and_changes_nothing()
    {
        var job = NewJob();
        var mine = NewBid(job);
        var foreign = NewBid(NewJob());

        Assert.Throws<DomainException>(() => job.Hire(foreign, [mine, foreign]));

        Assert.Equal(JobStatus.Open, job.Status);
        Assert.Null(job.AcceptedBidId);
        Assert.Equal(BidStatus.Active, mine.Status);
        Assert.Equal(BidStatus.Active, foreign.Status);
    }

    [Theory]
    [InlineData(BidStatus.Withdrawn)]
    [InlineData(BidStatus.Rejected)]
    [InlineData(BidStatus.Accepted)]
    public void Hiring_a_bid_that_is_not_active_is_refused_and_changes_nothing(BidStatus status)
    {
        var job = NewJob();
        var bid = NewBid(job, status);
        var other = NewBid(job);

        Assert.Throws<DomainException>(() => job.Hire(bid, [bid, other]));

        Assert.Equal(JobStatus.Open, job.Status);
        Assert.Null(job.AcceptedBidId);
        Assert.Equal(status, bid.Status);
        Assert.Equal(BidStatus.Active, other.Status);
    }

    [Fact]
    public void Hiring_the_same_bid_twice_is_refused()
    {
        var job = NewJob();
        var bid = NewBid(job);
        job.Hire(bid, [bid]);

        Assert.Throws<DomainException>(() => job.Hire(bid, [bid]));

        Assert.Equal(BidStatus.Accepted, bid.Status);
        Assert.Equal(bid.Id, job.AcceptedBidId);
    }

    [Fact]
    public void Hiring_when_every_bid_is_withdrawn_is_refused()
    {
        var job = NewJob();
        var withdrawn = NewBid(job, BidStatus.Withdrawn);

        Assert.Throws<DomainException>(() => job.Hire(withdrawn, [withdrawn]));

        Assert.Equal(JobStatus.Open, job.Status);
    }

    [Fact]
    public void Finishing_an_assigned_job_marks_it_done_with_utc_finish_time()
    {
        var job = JobIn(JobStatus.Assigned);

        job.Finish(new DateTimeOffset(2026, 11, 12, 11, 0, 0, TimeSpan.FromHours(1)));

        Assert.Equal(JobStatus.Done, job.Status);
        Assert.Equal(Now, job.FinishedAt);
        Assert.Equal(TimeSpan.Zero, job.FinishedAt!.Value.Offset);
    }

    [Theory]
    [InlineData(JobStatus.Open)]
    [InlineData(JobStatus.Done)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Cancelled)]
    public void Finishing_a_job_that_is_not_assigned_is_refused_and_changes_nothing(JobStatus status)
    {
        var job = JobIn(status);
        var finishedBefore = job.FinishedAt;

        Assert.Throws<DomainException>(() => job.Finish(Now.AddDays(1)));

        Assert.Equal(status, job.Status);
        Assert.Equal(finishedBefore, job.FinishedAt);
    }

    [Fact]
    public void Finishing_twice_keeps_the_first_finish_time()
    {
        var job = JobIn(JobStatus.Assigned);
        job.Finish(Now);

        Assert.Throws<DomainException>(() => job.Finish(Now.AddHours(1)));

        Assert.Equal(Now, job.FinishedAt);
    }

    [Fact]
    public void Cancelling_an_open_job_cancels_it_and_leaves_bids_unchanged()
    {
        var job = NewJob();
        var active = NewBid(job);
        var withdrawn = NewBid(job, BidStatus.Withdrawn);

        job.Cancel();

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(BidStatus.Active, active.Status);
        Assert.Equal(BidStatus.Withdrawn, withdrawn.Status);
    }

    [Fact]
    public void Cancelling_an_assigned_job_leaves_bids_unchanged_and_finishing_it_afterwards_is_refused()
    {
        var job = NewJob();
        var jelena = NewBid(job);
        var nikola = NewBid(job);
        job.Hire(jelena, [jelena, nikola]);

        job.Cancel();

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(jelena.Id, job.AcceptedBidId);
        Assert.Equal(BidStatus.Accepted, jelena.Status);
        Assert.Equal(BidStatus.Rejected, nikola.Status);
        Assert.Throws<DomainException>(() => job.Finish(Now));
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Null(job.FinishedAt);
    }

    [Theory]
    [InlineData(JobStatus.Done)]
    [InlineData(JobStatus.Closed)]
    [InlineData(JobStatus.Cancelled)]
    public void Cancelling_a_done_closed_or_cancelled_job_is_refused_and_changes_nothing(JobStatus status)
    {
        var job = JobIn(status);

        Assert.Throws<DomainException>(job.Cancel);

        Assert.Equal(status, job.Status);
    }
}
