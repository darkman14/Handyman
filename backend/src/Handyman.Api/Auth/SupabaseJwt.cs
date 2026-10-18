using System.Text;
using Handyman.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Handyman.Api.Auth;

public static class SupabaseJwt
{
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
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                };
                if (!string.IsNullOrEmpty(supabase.Value.JwtSecret))
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabase.Value.JwtSecret));
                else
                    options.TokenValidationParameters.IssuerSigningKeyResolver =
                        (_, _, kid, _) => jwks.GetKeys($"{url}/auth/v1/.well-known/jwks.json", kid);
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
