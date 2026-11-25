using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Handyman.Api.Tests.Infrastructure;

/// <summary>Signs JWTs that look like Supabase tokens, with a key only the tests know.</summary>
public static class TestAuth
{
    public const string Issuer = "http://test.local/auth/v1";
    public const string Audience = "authenticated";
    public const string Secret = "test-signing-key-test-signing-key-0123456789";
    public static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes(Secret));

    public static string TokenFor(Guid userId) => Build([new Claim("sub", userId.ToString()), new Claim("role", "authenticated")]);

    /// <summary>A token with full control over claims, lifetime, issuer, audience and key, for negative tests.</summary>
    public static string Build(
        IEnumerable<Claim> claims,
        DateTime? expires = null,
        string issuer = Issuer,
        string? audience = Audience,
        SecurityKey? key = null,
        string algorithm = SecurityAlgorithms.HmacSha256,
        DateTime? notBefore = null)
    {
        var token = new JwtSecurityToken(
            issuer, audience, claims,
            notBefore: notBefore,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key ?? Key, algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
