using Handyman.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Data.Seed;

/// <summary>
/// Gives each <see cref="DevWorkers"/> entry a worker profile, trades and neighborhood coverage.
/// Depends on <see cref="SeedDevUsersStep"/>: it must be registered after it, and it throws if a seeded worker user is missing.
/// A worker who already has a profile is left completely untouched (no new coverage, no approval changes, no trades re-added).
/// </summary>
public class SeedDevWorkersStep(AppDbContext db, ILogger<SeedDevWorkersStep> log) : IDevSeedStep
{
    private const string PlaceholderBio = "Development placeholder bio.";

    public async Task SeedAsync(CancellationToken ct)
    {
        var phones = DevWorkers.All.Select(w => w.Phone).ToList();
        var users = await db.Users.Where(u => phones.Contains(u.Phone)).ToDictionaryAsync(u => u.Phone, u => u.Id, ct);
        var trades = await db.Trades.ToDictionaryAsync(t => t.Name, t => t.Id, ct);
        var municipalities = await db.Areas.Where(a => a.Kind == AreaKind.Municipality).ToDictionaryAsync(a => a.Name, a => a.Id, ct);
        var neighborhoods = (await db.Areas.Where(a => a.Kind == AreaKind.Neighborhood).ToListAsync(ct))
            .ToLookup(a => a.ParentId);
        var userIds = users.Values.ToList();
        var already = (await db.WorkerProfiles.Where(p => userIds.Contains(p.UserId)).Select(p => p.UserId).ToListAsync(ct)).ToHashSet();

        foreach (var worker in DevWorkers.All)
        {
            if (!users.TryGetValue(worker.Phone, out var userId))
                throw new InvalidOperationException($"Seeded worker {worker.Phone} has no app.users row; the users seed step must run first.");
            if (already.Contains(userId))
                continue;

            db.WorkerProfiles.Add(new WorkerProfile { UserId = userId, Bio = PlaceholderBio, ApprovedAt = worker.Approved ? DateTimeOffset.UtcNow : null });

            foreach (var name in worker.Trades)
            {
                if (!trades.TryGetValue(name, out var tradeId))
                    throw new InvalidOperationException($"Seeded worker {worker.Phone}: trade '{name}' is missing from the reference data.");
                db.WorkerTrades.Add(new WorkerTrade { WorkerId = userId, TradeId = tradeId });
            }

            var areaIds = new HashSet<int>();
            foreach (var name in worker.Municipalities)
            {
                if (!municipalities.TryGetValue(name, out var municipalityId))
                    throw new InvalidOperationException($"Seeded worker {worker.Phone}: municipality '{name}' is missing from the reference data.");
                foreach (var n in neighborhoods[municipalityId]) areaIds.Add(n.Id);
            }
            foreach (var areaId in areaIds)
                db.WorkerAreas.Add(new WorkerArea { WorkerId = userId, AreaId = areaId });
        }

        await db.SaveChangesAsync(ct);
        log.LogDebug("Seeded worker profiles checked.");
    }
}
