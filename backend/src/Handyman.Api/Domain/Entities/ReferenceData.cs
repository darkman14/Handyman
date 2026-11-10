namespace Handyman.Api.Domain.Entities;

public enum AreaKind { City, Municipality, Neighborhood }

public class Trade
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A tree: city, municipality, neighborhood. Jobs and worker coverage point at neighborhoods.</summary>
public class Area
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AreaKind Kind { get; set; }
    public int? ParentId { get; set; }
}

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int AreaId { get; set; }
    public string ReferralCode { get; set; } = "";
}
