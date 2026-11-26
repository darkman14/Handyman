namespace Handyman.Api.Infrastructure;

public class SupabaseOptions
{
    public const string Section = "Supabase";

    public string Url { get; set; } = "";
    /// <summary>
    /// Expected token issuer. Optional: empty means <c>{Url}/auth/v1</c>. Set it when the issuer Supabase puts in tokens
    /// differs from the URL the API uses to reach Supabase (local dev: the stack signs with http://127.0.0.1:54321/auth/v1).
    /// </summary>
    public string Issuer { get; set; } = "";

    public string ServiceRoleKey { get; set; } = "";

    /// <summary>Only for projects on the legacy shared JWT secret. Empty means validate with JWKS.</summary>
    public string JwtSecret { get; set; } = "";
}
