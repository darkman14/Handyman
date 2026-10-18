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
    public static readonly SymmetricSecurityKey Key =
        new(Encoding.UTF8.GetBytes("test-signing-key-test-signing-key-0123456789"));

    public static string TokenFor(Guid userId)
    {
        var token = new JwtSecurityToken(
            Issuer, Audience,
            [new Claim("sub", userId.ToString()), new Claim("role", "authenticated")],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
