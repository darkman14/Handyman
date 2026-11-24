using Handyman.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Data;

/// <summary>The database context for query handlers: same model and connection as <see cref="AppDbContext"/>,
/// no change tracking, and saving is refused. It has no migrations of its own (AppDbContext owns the schema).</summary>
public class ReadDbContext : DbContext
{
    public ReadDbContext(DbContextOptions<ReadDbContext> options) : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<WorkerProfile> WorkerProfiles => Set<WorkerProfile>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<WorkerTrade> WorkerTrades => Set<WorkerTrade>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<WorkerArea> WorkerAreas => Set<WorkerArea>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobPhoto> JobPhotos => Set<JobPhoto>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AppDbContext.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        SnakeCase.Apply(modelBuilder);
    }

    private const string Refused = "ReadDbContext is read-only: use AppDbContext in a command handler to save changes.";

    public override int SaveChanges() => throw new InvalidOperationException(Refused);

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new InvalidOperationException(Refused);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Refused);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Refused);
}
