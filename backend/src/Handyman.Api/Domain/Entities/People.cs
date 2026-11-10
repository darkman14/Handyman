namespace Handyman.Api.Domain.Entities;

public enum UserRole { Client, Worker, Admin }

/// <summary>Id equals the Supabase auth user id. One role per user.</summary>
public class User
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = "";
    public UserRole Role { get; set; }
    public string Name { get; set; } = "";
    public int? HomeAreaId { get; set; }
    public int RatingSum { get; set; }
    public int RatingCount { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
}

public class WorkerProfile
{
    public Guid UserId { get; set; }
    public string Bio { get; set; } = "";
    public DateTimeOffset? ApprovedAt { get; set; }
}

public class WorkerTrade
{
    public Guid WorkerId { get; set; }
    public int TradeId { get; set; }
}

public class WorkerArea
{
    public Guid WorkerId { get; set; }
    public int AreaId { get; set; }
}
