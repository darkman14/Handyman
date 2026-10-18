using System.Security.Claims;

namespace Handyman.Api.Auth;

public static class CurrentUser
{
    /// <summary>The Supabase user id (the token's <c>sub</c> claim).</summary>
    public static Guid Id(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id) ? id : throw new UnauthorizedAccessException("Token has no valid sub claim.");
    }
}
