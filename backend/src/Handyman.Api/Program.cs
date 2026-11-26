using System.Reflection;
using Handyman.Api.Auth;
using Handyman.Api.Cqrs;
using Handyman.Api.Data;
using Handyman.Api.Data.Seed;
using Handyman.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// The build-time OpenAPI generator starts the app to read endpoints: no database work then.
var isDocGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
var isLocal = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");

if (isLocal)
{
    // Written by scripts/dev-env. Real environments use environment variables.
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
}
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://0.0.0.0:5080"); // reachable from phones on the LAN
}

builder.Host.UseSerilog((context, services, logger) =>
    logger.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services).WriteTo.Console(),
    preserveStaticLogger: true); // one logger per host, so parallel test hosts do not share log output

builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.Section));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));
builder.Services.AddDbContext<ReadDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .AddInterceptors(new ReadOnlySessionInterceptor())
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
builder.Services.AddCqrs(typeof(Program).Assembly);
builder.Services.AddHttpClient<IBlobStorage, SupabaseStorage>();
builder.Services.AddSupabaseJwt();
builder.Services.AddHttpClient<ISupabaseAdminClient, SupabaseAdminClient>();
builder.Services.AddScoped<DevSeeder>();
builder.Services.AddScoped<IDevSeedStep, SeedDevUsersStep>();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
// Unreadable bodies/parameters must throw (in every environment) so the exception handler answers 400 validation_failed.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
{
    if (corsOrigins.Length > 0)
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

// The shared test server sets Database__MigrateOnStartup=true (see deploy/docker-compose.test.yml).
var migrateOnStartup = builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Database:MigrateOnStartup");
if (!isDocGeneration && migrateOnStartup)
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}
// Seeding needs Seed:Enabled and a Development/Test environment; otherwise it is refused with a logged error.
// Never during the build-time OpenAPI generation (no database, no Supabase).
if (!isDocGeneration)
    await DevSeedStartup.RunAsync(app.Services, app.Environment, app.Configuration);

app.UseExceptionHandler(); // first, so it catches everything below; 401/403 are not exceptions and stay as they are
app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CallerStatusMiddleware>(); // 403 user_blocked for blocked callers; shares the users lookup with GET /me
app.UseAuthorization();

app.MapOpenApi();

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", ct); // also keeps the free Supabase project awake
    return Results.Ok(new { status = "ok" });
}).WithName("Health");

var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
api.MapGet("/me", (System.Security.Claims.ClaimsPrincipal user, HttpContext http) =>
    {
        var status = http.Items[CallerStatus.ItemKey] as CallerStatus;
        if (status is not { ProfileExists: true })
            return Results.Problem(ApiExceptionHandler.ProblemFor(StatusCodes.Status404NotFound, "profile_missing", "Not found",
                "Your profile has not been created yet."));
        return Results.Ok(new { id = user.Id() });
    }).WithName("GetMe")
    .ProducesProblem(StatusCodes.Status403Forbidden)  // user_blocked
    .ProducesProblem(StatusCodes.Status404NotFound);  // profile_missing
Handyman.Api.Features.Trades.ListTrades.Map(api);
Handyman.Api.Features.Areas.ListAreas.Map(api);

app.Run();

public partial class Program;
