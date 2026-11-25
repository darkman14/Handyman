using System.Text.Json;
using Handyman.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Handyman.Api.Infrastructure;

/// <summary>A query/route parameter that cannot be read; answered as 400 validation_failed with <c>errors</c> naming it.</summary>
public sealed class InvalidParameterException(string parameter, string message)
    : BadHttpRequestException(message, StatusCodes.Status400BadRequest)
{
    public string Parameter { get; } = parameter;
}

/// <summary>
/// The one place where exceptions become HTTP responses (RFC 7807, content type application/problem+json).
/// Every body has a stable <c>code</c>. Unknown failures give a fixed 500 text: no message, stack, SQL or connection details,
/// in every environment; the exception is logged here instead.
///
/// Codes (DomainException.Code is set where the rule is thrown, see Domain/Entities/Jobs.cs):
///   job_not_open          Hire on a non-Open job, and the loser of a concurrent save (DbUpdateConcurrencyException)
///   bid_job_mismatch      A bid belongs to another job
///   bid_not_active        The bid to hire is not Active
///   job_not_assigned      Finish on a job that is not Assigned
///   job_not_done          Rate on a job that is not Done
///   bid_not_accepted      The bid is not the accepted bid of the job
///   not_allowed           The user may not do this on the job
///   invalid_stars         Stars outside 1..5
///   rating_job_mismatch   A rating belongs to another job
///   already_rated         The user already rated the job
///   job_not_cancellable   Cancel on a job that is not Open or Assigned
///   business_rule_violated  DomainException created without an explicit code (fallback)
///   &lt;thing&gt;_not_found    NotFoundException(thing) -> 404
///   validation_failed     Bad or malformed request -> 400
///   server_error          Anything else -> 500
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions) : IExceptionHandler
{
    public const string ValidationFailed = "validation_failed";
    public const string ServerError = "server_error";
    public const string JobNotOpen = "job_not_open";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, detail) = Map(exception);

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogInformation("Request refused with {Status} {Code}: {Message}", status, code, exception.Message);

        var problem = ProblemFor(status, code, title, detail);
        if (exception is InvalidParameterException invalid)
            problem.Extensions["errors"] = new Dictionary<string, string[]> { [invalid.Parameter] = [detail] };
        await WriteAsync(httpContext, problem, jsonOptions.Value.SerializerOptions, cancellationToken);
        return true;
    }

    private static (int Status, string Code, string Title, string Detail) Map(Exception exception) => exception switch
    {
        DomainException e => (StatusCodes.Status409Conflict, e.Code, "Conflict", e.Message),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, JobNotOpen, "Conflict",
            "The job changed while you were working on it. It is no longer open."),
        NotFoundException e => (StatusCodes.Status404NotFound, e.Code, "Not found", e.Message),
        // Malformed JSON, wrong content type, unparsable route/query values: the framework's own 4xx.
        InvalidParameterException e => (StatusCodes.Status400BadRequest, ValidationFailed, "Validation failed", e.Message),
        BadHttpRequestException { StatusCode: not StatusCodes.Status400BadRequest } e => (e.StatusCode, "request_rejected",
            "Request rejected", "The request was rejected."),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, ValidationFailed, "Validation failed",
            "The request could not be read. Check the body and parameters."),
        _ => (StatusCodes.Status500InternalServerError, ServerError, "Server error",
            "Something went wrong. Try again."),
    };

    public static ProblemDetails ProblemFor(int status, string code, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{status}",
            Title = title,
            Status = status,
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        return problem;
    }

    public static Task WriteAsync(HttpContext httpContext, ProblemDetails problem, JsonSerializerOptions options, CancellationToken ct)
    {
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return httpContext.Response.WriteAsJsonAsync(problem, options, "application/problem+json", ct);
    }
}
