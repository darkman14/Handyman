using System.Text;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Handyman.Api.Auth;

public static class SupabaseJwt
{
    /// <summary>Tolerated difference between our clock and Supabase's when checking <c>exp</c> / <c>nbf</c>.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A validly signed token is still refused (401) unless it belongs to a signed-in user: <c>sub</c> must be a GUID
    /// and <c>role</c> must be "authenticated" (the Supabase anon key has role "anon" and no sub).
    /// </summary>
    private static Task RejectUnusableIdentity(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var sub = principal?.FindFirst("sub")?.Value;
        var role = principal?.FindFirst("role")?.Value;
        if (!Guid.TryParse(sub, out _) || role != "authenticated")
            context.Fail("Token is not a signed-in user token.");
        return Task.CompletedTask;
    }

    public static IServiceCollection AddSupabaseJwt(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<JwksCache>();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SupabaseOptions>, JwksCache>((options, supabase, jwks) =>
            {
                var url = supabase.Value.Url.TrimEnd('/');
                options.MapInboundClaims = false; // keep "sub" as "sub"
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = $"{url}/auth/v1",
                    ValidateAudience = true,
                    ValidAudience = "authenticated",
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = ClockSkew,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                };
                if (!string.IsNullOrEmpty(supabase.Value.JwtSecret))
                {
                    // Legacy shared-secret projects sign with HS256 only.
                    options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabase.Value.JwtSecret));
                }
                else
                {
                    // Asymmetric signing keys from the JWKS; never accept HS* (key confusion) or "none".
                    options.TokenValidationParameters.ValidAlgorithms =
                        [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256];
                    options.TokenValidationParameters.IssuerSigningKeyResolver =
                        (_, _, kid, _) => jwks.GetKeys($"{url}/auth/v1/.well-known/jwks.json", kid);
                }
                options.Events = new JwtBearerEvents { OnTokenValidated = RejectUnusableIdentity };
            });
        services.AddAuthorization();
        return services;
    }
}

/// <summary>Downloads and caches the Supabase JWKS; refreshes when an unknown key id shows up.</summary>
public class JwksCache
{
    private readonly object _lock = new();
    private JsonWebKeySet? _set;
    private DateTime _fetched = DateTime.MinValue;

    public IEnumerable<SecurityKey> GetKeys(string jwksUrl, string? kid)
    {
        lock (_lock)
        {
            var stale = DateTime.UtcNow - _fetched > TimeSpan.FromHours(1);
            var unknown = kid != null && _set != null && !_set.Keys.Any(k => k.Kid == kid)
                          && DateTime.UtcNow - _fetched > TimeSpan.FromMinutes(1);
            if (_set == null || stale || unknown)
            {
                using var http = new HttpClient();
                _set = new JsonWebKeySet(http.GetStringAsync(jwksUrl).GetAwaiter().GetResult());
                _fetched = DateTime.UtcNow;
            }
            return _set.GetSigningKeys();
        }
    }
}
