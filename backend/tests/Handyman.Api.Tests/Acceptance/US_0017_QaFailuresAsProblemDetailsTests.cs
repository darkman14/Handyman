using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Handyman.Api.Domain;
using Handyman.Api.Domain.Entities;
using Handyman.Api.Infrastructure;
using Handyman.Api.Tests.Cqrs;
using Handyman.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Handyman.Api.Tests.Acceptance;

// QA-owned tests for US-0017 (beyond the dev's US_0017_FailuresAsProblemDetailsTests):
// the real command decorator chain, validation edge cases, malformed bodies, cancellation, auth unchanged.

public record QaFlat(
    [property: Required, StringLength(10)] string? Title,
    [property: Range(1, 5)] int Stars,
    [property: RegularExpression("^[a-z]+$")] string? Slug);

public record QaNested([property: Required] string? Name, QaFlat? Inner);

public sealed record QaFailCommand(string Mode, string Prefix) : ICommand<string>;

public sealed class QaFailHandler(AppDbContext db, PendingPushes pushes) : ICommandHandler<QaFailCommand, string>
{
    public async Task<string> Handle(QaFailCommand c, CancellationToken ct)
    {
        db.Trades.Add(new Trade { Name = c.Prefix });
        await db.SaveChangesAsync(ct);
        pushes.Add(Guid.NewGuid(), "Hired");
        switch (c.Mode)
        {
            case "domain": throw new DomainException("job_not_open", "Job is Assigned; only an Open job can be hired.");
            case "concurrency": throw new DbUpdateConcurrencyException("UPDATE app.jobs SET status = 'x' WHERE xmin = 1 Host=db.internal");
            case "notfound": throw new NotFoundException("bid");
            case "boom": throw new InvalidOperationException("password=hunter2 Host=db.internal", new Exception("inner secret conn=Server=x;Password=y"));
            case "wrapped-domain": throw new InvalidOperationException("wrapper", new DomainException("job_not_open", "inner"));
            default: return "ok";
        }
    }
}

public sealed class QaCounters
{
    public int Handler;
    public TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class QaCapturingLogger<T> : ILogger<T>
{
    public static readonly List<(string Category, LogLevel Level, string Message)> Entries = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries) Entries.Add((typeof(T).Name, logLevel, formatter(state, exception)));
    }
}

public sealed class QaEndpointsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.UseEndpoints(endpoints =>
        {
            var g = endpoints.MapGroup("/api/qa").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
            g.MapGet("/cmd/{mode}", async (string mode, string prefix, ICommandHandler<QaFailCommand, string> h, CancellationToken ct) =>
                Results.Ok(await h.Handle(new QaFailCommand(mode, prefix), ct)));
            g.MapPost("/flat", (QaFlat body, QaCounters c) => { Interlocked.Increment(ref c.Handler); return Results.Ok(body); });
            g.MapPost("/nested", (QaNested body, QaCounters c) => { Interlocked.Increment(ref c.Handler); return Results.Ok(body); });
            g.MapGet("/guid/{id}", (Guid id, QaCounters c) => { Interlocked.Increment(ref c.Handler); return Results.Ok(id); });
            g.MapGet("/need", (int page, QaCounters c) => { Interlocked.Increment(ref c.Handler); return Results.Ok(page); });
            g.MapGet("/wait", async (QaCounters c, CancellationToken ct) =>
            {
                c.Started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { c.Cancelled.TrySetResult(); throw; }
                return Results.Ok();
            });
        });
    };
}

public class US_0017_QaFailuresAsProblemDetailsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly RecordingQueue _queue = new();
    private readonly QaCounters _counters = new();
    private readonly WebApplicationFactoryHolder _holder;

    // A derived factory so the QA endpoints, handler and recording queue are added on top of ApiFactory.
    private sealed class WebApplicationFactoryHolder(ApiFactory f, RecordingQueue queue, QaCounters counters)
    {
        public HttpClient Client(string environment = "Test")
        {
            var custom = f.WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                b.ConfigureServices(s =>
                {
                    s.AddCqrs([typeof(QaFailHandler)]);
                    s.AddSingleton<IPushQueue>(queue);
                    s.AddSingleton(counters);
                    s.AddSingleton(typeof(ILogger<>), typeof(QaCapturingLogger<>));
                    s.AddTransient<IStartupFilter, QaEndpointsStartupFilter>();
                });
            });
            var client = custom.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestAuth.TokenFor(Guid.NewGuid()));
            return client;
        }
    }

    public US_0017_QaFailuresAsProblemDetailsTests(ApiFactory factory)
    {
        _factory = factory;
        _holder = new WebApplicationFactoryHolder(factory, _queue, _counters);
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        r.StatusCode.ShouldBe(status);
        r.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        return body;
    }

    private async Task<bool> TradeExists(string name)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Trades.AnyAsync(t => t.Name == name);
    }

    private static string NewPrefix() => "qa17-" + Guid.NewGuid().ToString("N")[..10];

    // AC1 + AC2 + AC3 through the real decorator chain: mapped response is right, transaction rolled back, pushes not sent.
    [Theory]
    [InlineData("domain", HttpStatusCode.Conflict, "job_not_open")]
    [InlineData("concurrency", HttpStatusCode.Conflict, "job_not_open")]
    [InlineData("notfound", HttpStatusCode.NotFound, "bid_not_found")]
    [InlineData("boom", HttpStatusCode.InternalServerError, "server_error")]
    public async Task Failure_inside_a_command_rolls_back_sends_no_push_and_maps_correctly(string mode, HttpStatusCode status, string code)
    {
        var prefix = NewPrefix();
        var client = _holder.Client();

        var response = await client.GetAsync($"/api/qa/cmd/{mode}?prefix={prefix}");

        var body = await Problem(response, status, code);
        (await TradeExists(prefix)).ShouldBeFalse("the transaction must roll back");
        _queue.Received.ShouldBeEmpty("pushes must not be sent after a failure");
        var text = body.GetRawText();
        foreach (var leak in new[] { "hunter2", "db.internal", "xmin", "UPDATE", "inner secret", "Password", "InvalidOperationException", "Npgsql" })
            text.ShouldNotContain(leak);
    }

    [Fact]
    public async Task Successful_command_commits_and_sends_push_once()
    {
        var prefix = NewPrefix();
        var response = await _holder.Client().GetAsync($"/api/qa/cmd/ok?prefix={prefix}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TradeExists(prefix)).ShouldBeTrue();
        _queue.Received.Count.ShouldBe(1);
    }

    // The mapping is by exact type: a DomainException hidden as inner exception of another type is an unexpected failure.
    [Fact]
    public async Task Domain_exception_wrapped_in_another_exception_is_a_500_without_internals()
    {
        var response = await _holder.Client().GetAsync($"/api/qa/cmd/wrapped-domain?prefix={NewPrefix()}");
        var body = await Problem(response, HttpStatusCode.InternalServerError, "server_error");
        body.GetRawText().ShouldNotContain("wrapper");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Server_error_leaks_nothing_in_development_and_production_like_environments(string environment)
    {
        var response = await _holder.Client(environment).GetAsync($"/api/qa/cmd/boom?prefix={NewPrefix()}");
        var body = await Problem(response, HttpStatusCode.InternalServerError, "server_error");
        body.EnumerateObject().Select(p => p.Name).Order().ShouldBe(["code", "detail", "status", "title", "type"]);
        body.GetProperty("detail").GetString().ShouldBe("Something went wrong. Try again.");
        response.Headers.Contains("Server-Timing").ShouldBeFalse();
    }

    [Fact]
    public async Task Unexpected_failure_is_logged_at_error_level_by_the_handler()
    {
        lock (QaCapturingLogger<ApiExceptionHandler>.Entries) QaCapturingLogger<ApiExceptionHandler>.Entries.Clear();
        await _holder.Client().GetAsync($"/api/qa/cmd/boom?prefix={NewPrefix()}");
        lock (QaCapturingLogger<ApiExceptionHandler>.Entries)
            QaCapturingLogger<ApiExceptionHandler>.Entries.Count(e => e.Level == LogLevel.Error).ShouldBe(1);
    }

    // AC4 edge cases
    [Fact]
    public async Task Validation_errors_are_camel_cased_name_every_field_and_do_not_call_the_handler()
    {
        var client = _holder.Client();
        var response = await client.PostAsJsonAsync("/api/qa/flat", new { stars = 0, slug = "BAD SLUG", title = "this title is much too long" });
        var body = await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        body.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order().ShouldBe(["slug", "stars", "title"]);
        _counters.Handler.ShouldBe(0);
    }

    [Fact]
    public async Task Validation_messages_do_not_leak_internals()
    {
        var response = await _holder.Client().PostAsJsonAsync("/api/qa/flat", new { stars = 0, slug = "BAD", title = "" });
        var text = await response.Content.ReadAsStringAsync();
        foreach (var leak in new[] { "System.", "Handyman.Api", "QaFlat", "Exception", "at " })
            text.ShouldNotContain(leak);
    }

    // KNOWN_GAP BUG-0006 (low): the RegularExpression default message echoes the regex pattern to the client.
    [Fact]
    public async Task KNOWN_GAP_BUG_0006_regex_validation_message_echoes_the_pattern()
    {
        var response = await _holder.Client().PostAsJsonAsync("/api/qa/flat", new { stars = 3, slug = "BAD", title = "ok" });
        var text = await response.Content.ReadAsStringAsync();
        text.ShouldContain("^[a-z]+$"); // flip to ShouldNotContain when BUG-0006 is fixed
    }

    // KNOWN_GAP BUG-0007 (low): nested objects are not validated, an invalid inner object reaches the handler.
    [Fact]
    public async Task KNOWN_GAP_BUG_0007_nested_objects_are_not_validated()
    {
        var response = await _holder.Client().PostAsJsonAsync("/api/qa/nested", new { name = "ok", inner = new { stars = 99 } });
        response.StatusCode.ShouldBe(HttpStatusCode.OK); // should be 400 once nested validation exists
        _counters.Handler.ShouldBe(1);
        (await _holder.Client().PostAsJsonAsync("/api/qa/nested", new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // top level still works
    }

    [Theory]
    [InlineData("{\"title\":\"a\",\"stars\":3,\"extra\":{\"x\":1},\"admin\":true}", HttpStatusCode.OK)]
    [InlineData("{\"title\":\"a\",\"title\":\"b\",\"stars\":3}", HttpStatusCode.OK)]
    [InlineData("{\"title\":\"a\",\"stars\":\"many\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"title\":[1,2],\"stars\":3}", HttpStatusCode.BadRequest)]
    [InlineData("{\"title\":\"a\",\"stars\":3.5}", HttpStatusCode.BadRequest)]
    [InlineData("{\"title\":\"a\",\"stars\":99999999999999999999}", HttpStatusCode.BadRequest)]
    [InlineData("null", HttpStatusCode.BadRequest)]
    [InlineData("", HttpStatusCode.BadRequest)]
    [InlineData("   ", HttpStatusCode.BadRequest)]
    [InlineData("[]", HttpStatusCode.BadRequest)]
    [InlineData("{\"title\":\"a\",\"stars\":3", HttpStatusCode.BadRequest)]
    [InlineData("{\"title\":\"a\",\"stars\":3}}", HttpStatusCode.BadRequest)]
    [InlineData("{'title':'a'}", HttpStatusCode.BadRequest)]
    public async Task Body_variants_give_the_expected_status_and_never_500(string json, HttpStatusCode expected)
    {
        var response = await _holder.Client().PostAsync("/api/qa/flat", new StringContent(json, Encoding.UTF8, "application/json"));
        response.StatusCode.ShouldBe(expected);
        if (expected == HttpStatusCode.BadRequest)
        {
            await Problem(response, expected, "validation_failed");
            (await response.Content.ReadAsStringAsync()).ShouldNotContain("System.Text.Json");
            _counters.Handler.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Invalid_utf8_body_is_400_not_500()
    {
        var content = new ByteArrayContent([0x7B, 0x22, 0x74, 0x22, 0x3A, 0x22, 0xFF, 0xFE, 0x22, 0x7D]);
        content.Headers.ContentType = new("application/json");
        var response = await _holder.Client().PostAsync("/api/qa/flat", content);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    // KNOWN_GAP BUG-0005 (low): the framework answers 415 itself with an empty body, so there is no problem+json and no code
    // (the dev notes promise request_rejected). Flip to the problem assertions when fixed.
    [Fact]
    public async Task KNOWN_GAP_BUG_0005_wrong_content_type_is_415_with_an_empty_body()
    {
        var response = await _holder.Client().PostAsync("/api/qa/flat", new StringContent("{\"title\":\"a\",\"stars\":3}", Encoding.UTF8, "text/plain"));
        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        response.Content.Headers.ContentType.ShouldBeNull();
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        _counters.Handler.ShouldBe(0);
    }

    [Fact]
    public async Task Huge_body_is_rejected_by_validation_not_500()
    {
        var json = JsonSerializer.Serialize(new { title = new string('x', 5_000_000), stars = 3 });
        var response = await _holder.Client().PostAsync("/api/qa/flat", new StringContent(json, Encoding.UTF8, "application/json"));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        (await response.Content.ReadAsStringAsync()).Length.ShouldBeLessThan(2000);
    }

    [Fact]
    public async Task Bad_route_and_query_values_are_400_validation_failed()
    {
        var client = _holder.Client();
        await Problem(await client.GetAsync("/api/qa/guid/not-a-guid"), HttpStatusCode.BadRequest, "validation_failed");
        await Problem(await client.GetAsync("/api/qa/need"), HttpStatusCode.BadRequest, "validation_failed");
        await Problem(await client.GetAsync("/api/qa/need?page=abc"), HttpStatusCode.BadRequest, "validation_failed");
        _counters.Handler.ShouldBe(0);
    }

    [Fact]
    public async Task Client_abort_is_not_logged_as_an_error_or_500()
    {
        lock (QaCapturingLogger<ApiExceptionHandler>.Entries) QaCapturingLogger<ApiExceptionHandler>.Entries.Clear();
        var client = _holder.Client();
        using var cts = new CancellationTokenSource();
        var call = client.GetAsync("/api/qa/wait", cts.Token);
        await _counters.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(call);
        await _counters.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        lock (QaCapturingLogger<ApiExceptionHandler>.Entries)
            QaCapturingLogger<ApiExceptionHandler>.Entries.Where(e => e.Level >= LogLevel.Error).ShouldBeEmpty();
    }

    // Auth unchanged
    [Fact]
    public async Task Missing_bad_and_expired_tokens_are_401_with_no_internals()
    {
        var plain = _factory.CreateClient();
        (await plain.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var bad = _factory.CreateClient();
        bad.DefaultRequestHeaders.Authorization = new("Bearer", "not.a.token");
        var response = await bad.GetAsync("/api/me");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();

        var valid = _factory.ClientFor(await _factory.SeedUserAsync());
        (await valid.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unauthenticated_invalid_body_is_401_not_400()
    {
        // Authorization runs before the validation filter: no information about the schema for anonymous callers.
        var response = await _holder.Client().WithoutToken().PostAsync("/api/qa/flat", new StringContent("{}", Encoding.UTF8, "application/json"));
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cors_preflight_is_unaffected_and_unknown_routes_stay_plain_404_405()
    {
        var client = _factory.CreateClient();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/me");
        preflight.Headers.Add("Origin", "http://example.test");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        ((int)(await client.SendAsync(preflight)).StatusCode).ShouldBeLessThan(500);

        (await client.GetAsync("/nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync("/health", null)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    // BUG-0008 fixed: Job has a concurrency token (xmin); the real two-context race is in US_0017_ConcurrencyTokenTests.
    [Fact]
    public void Job_has_a_concurrency_token_so_real_concurrent_saves_collide()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = db.Model.FindEntityType(typeof(Job))!;
        job.GetProperties().Any(p => p.IsConcurrencyToken).ShouldBeTrue();
    }
}

internal static class QaHttpExtensions
{
    public static HttpClient WithoutToken(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
