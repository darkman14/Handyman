using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Handyman.Api.Infrastructure;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

public class US_0017_FailuresAsProblemDetailsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("type").GetString().ShouldNotBeNullOrWhiteSpace();
        return body;
    }

    [Fact]
    public async Task Domain_error_returns_409_with_its_stable_code()
    {
        var body = await ProblemOf(await factory.CreateClient().GetAsync("/test-errors/domain"), HttpStatusCode.Conflict, "job_not_open");
        body.GetProperty("detail").GetString()!.ShouldContain("only an Open job can be hired");
    }

    [Fact]
    public async Task Domain_error_without_explicit_code_still_has_a_code()
    {
        await ProblemOf(await factory.CreateClient().GetAsync("/test-errors/domain-default"), HttpStatusCode.Conflict, "business_rule_violated");
    }

    [Fact]
    public async Task Concurrency_conflict_returns_409_job_not_open_without_internals()
    {
        var response = await factory.CreateClient().GetAsync("/test-errors/concurrency");
        await ProblemOf(response, HttpStatusCode.Conflict, "job_not_open");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("secret sql");
    }

    [Fact]
    public async Task Not_found_returns_404_with_thing_not_found_code()
    {
        await ProblemOf(await factory.CreateClient().GetAsync("/test-errors/not-found"), HttpStatusCode.NotFound, "job_not_found");
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Development")]
    public async Task Unexpected_failure_returns_500_without_internals_in_any_environment(string environment)
    {
        using var custom = factory.WithWebHostBuilder(b => b.UseEnvironment(environment));
        var response = await custom.CreateClient().GetAsync("/test-errors/boom");
        await ProblemOf(response, HttpStatusCode.InternalServerError, "server_error");
        var text = await response.Content.ReadAsStringAsync();
        foreach (var leak in new[] { "hunter2", "db.internal", "Npgsql", "InvalidOperationException", "StackTrace", " at " })
            text.ShouldNotContain(leak);
    }

    [Fact]
    public async Task Unexpected_failure_is_logged_on_the_server_with_the_exception()
    {
        var logger = new CapturingLogger<ApiExceptionHandler>();
        var handler = new ApiExceptionHandler(logger, Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var boom = new InvalidOperationException("secret");

        await handler.TryHandleAsync(context, boom, CancellationToken.None);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Exception == boom);
    }

    [Fact]
    public async Task Missing_and_invalid_fields_return_400_validation_failed_naming_each_field_and_skip_the_handler()
    {
        var calls = factory.Services.GetRequiredService<TestErrorCalls>();
        var before = calls.ValidatedHandlerCalls;

        var response = await factory.CreateClient().PostAsJsonAsync("/test-errors/validated", new { stars = 9 });

        var body = await ProblemOf(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = body.GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ShouldBe(["title", "stars"], ignoreOrder: true);
        errors.EnumerateObject().ShouldAllBe(p => p.Value.GetArrayLength() > 0);
        calls.ValidatedHandlerCalls.ShouldBe(before);
    }

    [Fact]
    public async Task Invalid_query_parameter_returns_400_validation_failed()
    {
        var calls = factory.Services.GetRequiredService<TestErrorCalls>();
        var before = calls.ValidatedHandlerCalls;

        var body = await ProblemOf(await factory.CreateClient().GetAsync("/test-errors/query?page=99"), HttpStatusCode.BadRequest, "validation_failed");

        body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["page"]);
        calls.ValidatedHandlerCalls.ShouldBe(before);
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/test-errors/validated", new { title = "Leaking tap", stars = 3 });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("{ \"title\": ")]
    [InlineData("not json at all")]
    [InlineData("{ \"title\": \"x\", \"stars\": \"many\" }")]
    public async Task Malformed_json_returns_400_validation_failed_not_500(string json)
    {
        var calls = factory.Services.GetRequiredService<TestErrorCalls>();
        var before = calls.ValidatedHandlerCalls;
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await factory.CreateClient().PostAsync("/test-errors/validated", content);

        var body = await ProblemOf(response, HttpStatusCode.BadRequest, "validation_failed");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("System.Text.Json");
        calls.ValidatedHandlerCalls.ShouldBe(before);
    }

    [Fact]
    public async Task Missing_token_is_still_401_and_wrong_permission_still_403_with_no_problem_body()
    {
        var unauthorized = await factory.CreateClient().GetAsync("/api/me");
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var forbidden = await factory.ClientFor(Guid.NewGuid()).GetAsync("/test-errors/forbidden");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
