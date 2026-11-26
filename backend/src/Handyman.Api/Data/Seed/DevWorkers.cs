namespace Handyman.Api.Data.Seed;

/// <summary>One seeded worker's trades, covered municipalities and approval state (workflow.md section 7).</summary>
public record DevWorker(string Phone, string[] Trades, string[] Municipalities, bool Approved);

public static class DevWorkers
{
    public static readonly IReadOnlyList<DevWorker> All =
    [
        new("+381600000101", ["Electrical", "Heating & AC"], ["Novi Beograd", "Zvezdara"], true),
        new("+381600000102", ["Appliance repair"], ["Vračar", "Zvezdara"], true),
        new("+381600000103", ["Appliance repair", "Plumbing"], ["Vračar", "Novi Beograd"], true),
        new("+381600000104", ["Painting", "Carpentry"], ["Vračar"], false),
    ];
}
