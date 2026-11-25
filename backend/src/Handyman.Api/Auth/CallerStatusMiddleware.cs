using Handyman.Api.Data;
using Handyman.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Handyman.Api.Auth;

/// <summary>What the per-request lookup found about the signed-in caller.</summary>
public sealed record CallerStatus(bool ProfileExists, bool Blocked)
{
    public const string ItemKey = "Handyman.CallerStatus";
}

/// <summary>
/// Runs after authentication, for authenticated callers under /api only. One primary-key lookup on app.users per request
/// (no cache, so a block takes effect on the next request). A blocked user is answered 403 user_blocked before any handler runs.
/// Anonymous requests and invalid tokens pass through untouched. The result is shared with handlers via HttpContext.Items.
/// </summary>
public sealed class CallerStatusMiddleware(RequestDelegate next, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
{
    public const string BlockedCode = "user_blocked";

    public async Task InvokeAsync(HttpContext context, ReadDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.Request.Path.StartsWithSegments("/api")
            && TryGetId(context, out var id))
        {
            var row = await db.Users.Where(u => u.Id == id).Select(u => new { u.BlockedAt }).FirstOrDefaultAsync(context.RequestAborted);
            var status = new CallerStatus(row is not null, row?.BlockedAt is not null);
            context.Items[CallerStatus.ItemKey] = status;

            if (status.Blocked)
            {
                var problem = ApiExceptionHandler.ProblemFor(StatusCodes.Status403Forbidden, BlockedCode, "Forbidden",
                    "This account is blocked.");
                await ApiExceptionHandler.WriteAsync(context, problem, jsonOptions.Value.SerializerOptions, context.RequestAborted);
                return;
            }
        }

        await next(context);
    }

    private static bool TryGetId(HttpContext context, out Guid id)
    {
        try
        {
            id = context.User.Id();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            id = Guid.Empty;
            return false;
        }
    }
}
