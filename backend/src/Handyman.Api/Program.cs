using System.Reflection;
using Handyman.Api.Auth;
using Handyman.Api.Data;
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

builder.Host.UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.Section));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));
builder.Services.AddHttpClient<IBlobStorage, SupabaseStorage>();
builder.Services.AddSupabaseJwt();
builder.Services.AddOpenApi();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
{
    if (corsOrigins.Length > 0)
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

if (!isDocGeneration && builder.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", ct); // also keeps the free Supabase project awake
    return Results.Ok(new { status = "ok" });
}).WithName("Health");

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/me", (System.Security.Claims.ClaimsPrincipal user) => Results.Ok(new { id = user.Id() })).WithName("GetMe");

app.Run();

public partial class Program;
