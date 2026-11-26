using System.Net.Http.Headers;
using Flagpole.Api.Auth;
using Flagpole.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flagpole.Api.Tests;

/// <summary>
/// Runs the real API in memory, with SQLite instead of PostgreSQL so the
/// tests need no database server. Each factory gets its own database.
/// </summary>
public sealed class FlagpoleApiFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "test-admin-key-0000000000000000000000";
    public const string ReaderKey = "test-reader-key-000000000000000000000";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FlagpoleApiFactory() => _connection.Open();

    /// <summary>Read when the first client is created, so set it before that.</summary>
    public int EvaluationsPerMinute { get; init; } = 10_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Flagpole", "Host=unused");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("RateLimiting:EvaluationsPerMinute", EvaluationsPerMinute.ToString());
        builder.UseSetting("ApiKeys:Keys:0:Name", "test-admin");
        builder.UseSetting("ApiKeys:Keys:0:Key", AdminKey);
        builder.UseSetting("ApiKeys:Keys:0:Role", ApiKeyDefaults.AdminRole);
        builder.UseSetting("ApiKeys:Keys:1:Name", "test-reader");
        builder.UseSetting("ApiKeys:Keys:1:Key", ReaderKey);
        builder.UseSetting("ApiKeys:Keys:1:Role", ApiKeyDefaults.ReaderRole);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FlagpoleDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FlagpoleDbContext>>();
            services.AddDbContext<FlagpoleDbContext>(options => options.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<FlagpoleDbContext>().Database.EnsureCreated();
        });
    }

    public HttpClient CreateClient(string? apiKey)
    {
        var client = CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, apiKey);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
