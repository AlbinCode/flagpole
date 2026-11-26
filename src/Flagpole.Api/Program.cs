using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Flagpole.Api.Auth;
using Flagpole.Api.Data;
using Flagpole.Api.Flags;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FlagpoleDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Flagpole")));

builder.Services.AddOptions<ApiKeyOptions>()
    .BindConfiguration(ApiKeyOptions.Section)
    .ValidateDataAnnotations()
    .Validate(o => o.Keys.All(k => k.Key.Length >= 32 && !string.IsNullOrWhiteSpace(k.Name)),
        "Every API key needs a name and a key of at least 32 characters.")
    .ValidateOnStart();

builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiKeyDefaults.AdminPolicy, policy => policy.RequireRole(ApiKeyDefaults.AdminRole))
    .AddPolicy(ApiKeyDefaults.ReaderPolicy, policy => policy.RequireRole(ApiKeyDefaults.AdminRole, ApiKeyDefaults.ReaderRole));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // One budget per API key, so a single noisy client cannot starve the others.
    options.AddPolicy(EvaluationEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:EvaluationsPerMinute", 600),
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FlagCache>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<FlagpoleDbContext>(tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness: the process is up. Readiness: it can also reach the database.
// Container Apps uses them to restart broken replicas and to hold traffic
// until a new revision is ready.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapFlagEndpoints();
app.MapEvaluationEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FlagpoleDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
