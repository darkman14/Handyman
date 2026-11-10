using Handyman.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string Schema = "app";

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
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        SnakeCase.Apply(modelBuilder);
    }
}
