using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using Handyman.Api.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Handyman.Api.Tests.Infrastructure;

public record TestValidatedRequest(
    [property: Required, StringLength(20)] string? Title,
    [property: Range(1, 5)] int Stars);

/// <summary>Counts how often the validated test endpoint's handler ran (it must not run on invalid input).</summary>
public class TestErrorCalls
{
    public int ValidatedHandlerCalls;
}

/// <summary>Test-only endpoints that throw each kind of error. Added by ApiFactory; never part of the real API.</summary>
public class TestErrorEndpointsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.UseEndpoints(endpoints =>
        {
            var group = endpoints.MapGroup("/test-errors").AddEndpointFilter<Handyman.Api.Infrastructure.ValidationFilter>();
            group.MapGet("/domain", () => { throw new DomainException("job_not_open", "Job is Assigned; only an Open job can be hired."); });
            group.MapGet("/domain-default", () => { throw new DomainException("Some rule."); });
            group.MapGet("/concurrency", () => { throw new DbUpdateConcurrencyException("secret sql: UPDATE app.jobs SET ... xmin"); });
            group.MapGet("/not-found", () => { throw new NotFoundException("job"); });
            group.MapGet("/boom", () => { throw new InvalidOperationException("password=hunter2 Host=db.internal at Npgsql.Secret"); });
            group.MapPost("/validated", (TestValidatedRequest request, TestErrorCalls calls) =>
            {
                Interlocked.Increment(ref calls.ValidatedHandlerCalls);
                return Results.Ok(request);
            });
            group.MapGet("/query", ([Range(1, 10)] int page, TestErrorCalls calls) =>
            {
                Interlocked.Increment(ref calls.ValidatedHandlerCalls);
                return Results.Ok(page);
            });
            group.MapGet("/forbidden", () => Results.Forbid()).RequireAuthorization();
        });
    };
}
