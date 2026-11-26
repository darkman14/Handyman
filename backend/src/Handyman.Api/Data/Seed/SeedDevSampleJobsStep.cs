using Handyman.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Data.Seed;

/// <summary>
/// Adds the three sample jobs from workflow.md section 7 (DevSampleJobs), but only when no job exists at all.
/// Depends on <see cref="SeedDevUsersStep"/> and <see cref="SeedDevWorkersStep"/>: register it after both; it throws if users, trades or areas are missing.
/// Jobs and bids are created through the domain methods (Hire, Finish), and everything is saved in one SaveChanges.
/// </summary>
public class SeedDevSampleJobsStep(AppDbContext db, ILogger<SeedDevSampleJobsStep> log) : IDevSeedStep
{
    private const string Ana = "+381600000201", Milan = "+381600000202", Ivana = "+381600000203";
    private const string Jelena = "+381600000102", Nikola = "+381600000103";

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Jobs.AnyAsync(ct))
        {
            log.LogDebug("Jobs already exist; sample jobs skipped.");
            return;
        }

        var users = await db.Users.ToDictionaryAsync(u => u.Phone, u => u.Id, ct);
        var trades = await db.Trades.ToDictionaryAsync(t => t.Name, t => t.Id, ct);
        var areas = (await db.Areas.Where(a => a.Kind == AreaKind.Neighborhood).ToListAsync(ct)).ToDictionary(a => a.Name, a => a.Id);

        Guid User(string phone) => users.TryGetValue(phone, out var id)
            ? id
            : throw new InvalidOperationException($"Sample jobs need seeded user {phone}; the users and workers seed steps must run first.");
        int Trade(string name) => trades.TryGetValue(name, out var id)
            ? id
            : throw new InvalidOperationException($"Sample jobs need trade '{name}' in the reference data.");
        int Area(string name) => areas.TryGetValue(name, out var id)
            ? id
            : throw new InvalidOperationException($"Sample jobs need neighborhood '{name}' in the reference data.");

        var now = DateTimeOffset.UtcNow;
        var tomorrow = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

        // 1. Ana: open dishwasher job with two active bids.
        var dishwasher = NewJob(User(Ana), Trade("Appliance repair"), Area("Čubura"), now.AddHours(-6),
            "Dishwasher does not drain", "The dishwasher stops mid-cycle and leaves water at the bottom. About six years old.",
            "Njegoševa", "24", "2", "3", "12", "Ring the bell marked Marković; the gate code is 1234.");
        var anaBids = new List<Bid>
        {
            NewBid(dishwasher, User(Jelena), tomorrow.AddHours(9), tomorrow.AddHours(12), 1500, 2500, now.AddHours(-4)),
            NewBid(dishwasher, User(Nikola), tomorrow.AddHours(14), tomorrow.AddHours(18), 2000, 2200, now.AddHours(-3)),
        };

        // 2. Milan: open electrical job, no bids.
        var electrical = NewJob(User(Milan), Trade("Electrical"), Area("Bežanijska kosa"), now.AddHours(-2),
            "Socket in the kitchen is dead", "The socket next to the stove stopped working; the fuse is fine.",
            "Bežanijska kosa 15", "15", "1", "5", "21", "Third door on the left, buzzer is broken, call on arrival.");

        // 3. Ivana: done appliance job, hired to Jelena.
        var created = now.AddDays(-5);
        var done = NewJob(User(Ivana), Trade("Appliance repair"), Area("Đeram"), created,
            "Washing machine will not spin", "The drum does not spin during the program, water drains fine.",
            "Cara Dušana", "8", "1", "1", "3", "Courtyard entrance, flat on the ground floor.");
        var doneBid = NewBid(done, User(Jelena), created.AddDays(1).AddHours(9), created.AddDays(1).AddHours(12), 1500, 2500, created.AddHours(2));
        done.Hire(doneBid, [doneBid]);
        done.Finish(created.AddDays(2));

        // Job ids are time-ordered, so the identity job numbers increase in this order.
        db.Jobs.AddRange(dishwasher, electrical, done);
        db.Bids.AddRange(anaBids);
        db.Bids.Add(doneBid);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded 3 sample jobs.");
    }

    private static int _sequence;

    /// <summary>Time-ordered ids, one millisecond apart: EF may insert same-type rows in key order, and job numbers must follow the intended order.</summary>
    private static Guid NextId() => Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMilliseconds(Interlocked.Increment(ref _sequence)));

    private static Job NewJob(Guid clientId, int tradeId, int areaId, DateTimeOffset createdAt, string title, string description,
        string street, string house, string entrance, string floor, string apartment, string note) => new()
    {
        Id = NextId(),
        ClientId = clientId,
        TradeId = tradeId,
        AreaId = areaId,
        Title = title,
        Description = description,
        Street = street,
        HouseNumber = house,
        Entrance = entrance,
        Floor = floor,
        Apartment = apartment,
        AddressNote = note,
        CreatedAt = createdAt,
    };

    private static Bid NewBid(Job job, Guid workerId, DateTimeOffset start, DateTimeOffset end, int fee, int rate, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        JobId = job.Id,
        WorkerId = workerId,
        ArrivalWindowStart = start,
        ArrivalWindowEnd = end,
        DiagnosticFee = fee,
        HourlyRate = rate,
        CreatedAt = createdAt,
    };
}
