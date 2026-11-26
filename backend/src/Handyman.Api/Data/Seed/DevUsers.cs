using Handyman.Api.Domain.Entities;

namespace Handyman.Api.Data.Seed;

/// <summary>One seeded user. <see cref="HomeNeighborhood"/> is set for clients only.</summary>
public record DevUser(string Name, string Phone, UserRole Role, string? HomeNeighborhood = null);

/// <summary>The development cast from workflow.md section 7. Phones must match [auth.sms.test_otp] in supabase/config.toml.</summary>
public static class DevUsers
{
    public static readonly IReadOnlyList<DevUser> All =
    [
        new("Marko Jovanović", "+381600000101", UserRole.Worker),
        new("Jelena Petrović", "+381600000102", UserRole.Worker),
        new("Nikola Đorđević", "+381600000103", UserRole.Worker),
        new("Stefan Ilić", "+381600000104", UserRole.Worker),
        new("Ana Marković", "+381600000201", UserRole.Client, "Čubura"),
        new("Milan Kostić", "+381600000202", UserRole.Client, "Bežanijska kosa"),
        new("Ivana Nikolić", "+381600000203", UserRole.Client, "Đeram"),
        new("Petar Stojanović", "+381600000204", UserRole.Client, "Neimar"),
        new("Mila Pavlović", "+381600000205", UserRole.Client, "Krunski venac"),
        new("Admin", "+381600000999", UserRole.Admin),
    ];

    /// <summary>Digits only, with a leading '+': "+381 60 000 0101", "381600000101" and "+381600000101" all become "+381600000101".</summary>
    public static string NormalizePhone(string phone) => "+" + new string(phone.Where(char.IsAsciiDigit).ToArray());
}
