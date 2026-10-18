namespace Handyman.Api.Infrastructure;

public class SupabaseOptions
{
    public const string Section = "Supabase";

    public string Url { get; set; } = "";
    public string ServiceRoleKey { get; set; } = "";

    /// <summary>Only for projects on the legacy shared JWT secret. Empty means validate with JWKS.</summary>
    public string JwtSecret { get; set; } = "";
}
