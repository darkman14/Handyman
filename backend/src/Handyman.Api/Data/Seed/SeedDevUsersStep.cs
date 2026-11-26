using Handyman.Api.Domain.Entities;
using Handyman.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Data.Seed;

/// <summary>
/// Creates every <see cref="DevUsers"/> entry in Supabase Auth (reusing an auth user with the same phone) and in app.users with the same id.
/// Idempotent. Never re-keys an existing app.users row and never creates a second row for a phone: a mismatch throws, so startup fails loudly.
/// All auth users are resolved first and all app.users rows are saved in one SaveChanges, so a Supabase outage leaves no half-seeded app.users.
/// </summary>
public class SeedDevUsersStep(ISupabaseAdminClient admin, AppDbContext db, ILogger<SeedDevUsersStep> log) : IDevSeedStep
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var resolved = new List<(DevUser User, string Phone, Guid AuthId)>();
        try
        {
            foreach (var dev in DevUsers.All)
            {
                var phone = DevUsers.NormalizePhone(dev.Phone);
                var authId = await admin.FindUserIdByPhoneAsync(phone, ct)
                             ?? await admin.CreateConfirmedPhoneUserAsync(phone, ct);
                resolved.Add((dev, phone, authId));
            }
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Supabase Auth is not reachable or refused the request; no seeded users were written to app.users. Start local Supabase and restart the API.");
            throw new InvalidOperationException("Seeding users failed: Supabase Auth admin API is not available.", ex);
        }

        var neighborhoods = await db.Areas.Where(a => a.Kind == AreaKind.Neighborhood).ToDictionaryAsync(a => a.Name, a => a.Id, ct);
        var phones = resolved.Select(r => r.Phone).ToList();
        var ids = resolved.Select(r => r.AuthId).ToList();
        var existing = await db.Users.Where(u => phones.Contains(u.Phone) || ids.Contains(u.Id)).ToListAsync(ct);

        foreach (var (dev, phone, authId) in resolved)
        {
            var byPhone = existing.FirstOrDefault(u => u.Phone == phone);
            var byId = existing.FirstOrDefault(u => u.Id == authId);
            if (byPhone is not null && byPhone.Id == authId)
                continue; // already seeded

            if (byPhone is not null || byId is not null)
            {
                log.LogError("Seeded user {Name} does not match the existing data: the auth user id and the app.users row disagree. "
                             + "app.users and Supabase Auth must be reset together; nothing was changed.", dev.Name);
                throw new InvalidOperationException(
                    $"Seeded user '{dev.Name}': an app.users row exists with a different id or phone than the Supabase auth user. Reset app.users and Supabase Auth together.");
            }

            int? homeAreaId = null;
            if (dev.HomeNeighborhood is not null)
            {
                if (!neighborhoods.TryGetValue(dev.HomeNeighborhood, out var areaId))
                    throw new InvalidOperationException($"Seeded user '{dev.Name}': neighborhood '{dev.HomeNeighborhood}' is missing from the reference data.");
                homeAreaId = areaId;
            }

            db.Users.Add(new User { Id = authId, Phone = phone, Name = dev.Name, Role = dev.Role, HomeAreaId = homeAreaId });
        }

        await db.SaveChangesAsync(ct);
    }
}
