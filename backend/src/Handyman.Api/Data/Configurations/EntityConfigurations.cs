using Handyman.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Handyman.Api.Data.Configurations;

// Names are converted to snake_case by SnakeCase. Foreign keys stay inside schema app,
// never into the Supabase auth or storage schemas. Enums are stored as text.

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever(); // = Supabase auth user id
        b.Property(x => x.Phone).HasMaxLength(20);
        b.HasIndex(x => x.Phone).IsUnique(); // stored normalized to +381...
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.HomeAreaId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_users_role", "role IN ('Client', 'Worker', 'Admin')"));
    }
}

public class WorkerProfileConfiguration : IEntityTypeConfiguration<WorkerProfile>
{
    public void Configure(EntityTypeBuilder<WorkerProfile> b)
    {
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.HasOne<User>().WithOne().HasForeignKey<WorkerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> b) => b.Property(x => x.Name).HasMaxLength(100);
}

public class WorkerTradeConfiguration : IEntityTypeConfiguration<WorkerTrade>
{
    public void Configure(EntityTypeBuilder<WorkerTrade> b)
    {
        b.HasKey(x => new { x.WorkerId, x.TradeId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Trade>().WithMany().HasForeignKey(x => x.TradeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_areas_kind", "kind IN ('City', 'Municipality', 'Neighborhood')"));
    }
}

public class WorkerAreaConfiguration : IEntityTypeConfiguration<WorkerArea>
{
    public void Configure(EntityTypeBuilder<WorkerArea> b)
    {
        b.HasKey(x => new { x.WorkerId, x.AreaId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.ReferralCode).HasMaxLength(50);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Number).UseIdentityAlwaysColumn(); // database-generated, readable job number
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Street).HasMaxLength(200);
        b.Property(x => x.HouseNumber).HasMaxLength(20);
        b.Property(x => x.Entrance).HasMaxLength(20);
        b.Property(x => x.Floor).HasMaxLength(20);
        b.Property(x => x.Apartment).HasMaxLength(20);
        b.Property(x => x.AddressNote).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Trade>().WithMany().HasForeignKey(x => x.TradeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Area>().WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ClientId);
        b.HasIndex(x => new { x.Status, x.AreaId });
        b.ToTable(t => t.HasCheckConstraint("ck_jobs_status", "status IN ('Open', 'Assigned', 'Done', 'Closed', 'Cancelled')"));
    }
}

public class JobPhotoConfiguration : IEntityTypeConfiguration<JobPhoto>
{
    public void Configure(EntityTypeBuilder<JobPhoto> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StoragePath).HasMaxLength(500);
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BidConfiguration : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.JobId, x.WorkerId }).IsUnique(); // one bid per worker per job, withdrawn included
        b.HasIndex(x => x.WorkerId);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_bids_status", "status IN ('Active', 'Accepted', 'Rejected', 'Withdrawn')");
            t.HasCheckConstraint("ck_bids_arrival_window", "arrival_window_end > arrival_window_start");
            t.HasCheckConstraint("ck_bids_money", "diagnostic_fee >= 0 AND hourly_rate >= 0");
        });
    }
}

public class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RaterId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.JobId, x.RaterId }).IsUnique(); // one rating per rater per job
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RateeId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_ratings_stars", "stars BETWEEN 1 AND 5"));
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasMaxLength(50);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.UserId);
    }
}

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Token).HasMaxLength(500);
        b.Property(x => x.Platform).HasMaxLength(20);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
