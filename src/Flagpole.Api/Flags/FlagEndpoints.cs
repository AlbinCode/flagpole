using System.Security.Claims;
using System.Text.Json;
using Flagpole.Api.Auth;
using Flagpole.Api.Data;
using Flagpole.Core;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Flagpole.Api.Flags;

public static class FlagEndpoints
{
    public static IEndpointRouteBuilder MapFlagEndpoints(this IEndpointRouteBuilder app)
    {
        var flags = app.MapGroup("/api/flags")
            .WithTags("Flags")
            .RequireAuthorization(ApiKeyDefaults.AdminPolicy);

        flags.MapGet("/", ListFlags);
        flags.MapGet("/{key}", GetFlag);
        flags.MapPost("/", CreateFlag);
        flags.MapPut("/{key}/environments/{environment}", UpdateEnvironment);
        flags.MapDelete("/{key}", DeleteFlag);
        flags.MapGet("/{key}/audit", GetAuditLog);

        return app;
    }

    private static async Task<Ok<List<FlagResponse>>> ListFlags(FlagpoleDbContext db, CancellationToken ct)
    {
        var flags = await db.Flags.AsNoTracking().Include(f => f.Environments).OrderBy(f => f.Key).ToListAsync(ct);
        return TypedResults.Ok(flags.Select(FlagResponse.From).ToList());
    }

    private static async Task<Results<Ok<FlagResponse>, NotFound>> GetFlag(string key, FlagpoleDbContext db, CancellationToken ct)
    {
        var flag = await db.Flags.AsNoTracking().Include(f => f.Environments).SingleOrDefaultAsync(f => f.Key == key, ct);
        return flag is null ? TypedResults.NotFound() : TypedResults.Ok(FlagResponse.From(flag));
    }

    private static async Task<Results<Created<FlagResponse>, ValidationProblem, ProblemHttpResult>> CreateFlag(
        CreateFlagRequest request,
        FlagpoleDbContext db,
        FlagCache cache,
        TimeProvider time,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!FlagRules.IsValidKey(request.Key))
        {
            errors["key"] = ["Use 2-63 lowercase letters, digits and dashes, starting with a letter or digit."];
        }

        if (request.Description is { Length: > FlagRules.MaxDescriptionLength })
        {
            errors["description"] = [$"At most {FlagRules.MaxDescriptionLength} characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await db.Flags.AnyAsync(f => f.Key == request.Key, ct))
        {
            return DuplicateKey(request.Key);
        }

        var now = time.GetUtcNow();
        var flag = new FeatureFlag
        {
            Key = request.Key,
            Description = request.Description ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Flags.Add(flag);
        db.AuditEntries.Add(Audit(flag.Key, "flag.created", null, user, now, new { flag.Description }));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request created the same key between the check and the insert.
            return DuplicateKey(request.Key);
        }

        cache.Invalidate(flag.Key);
        return TypedResults.Created($"/api/flags/{flag.Key}", FlagResponse.From(flag));
    }

    private static async Task<Results<Ok<FlagResponse>, NotFound, ValidationProblem>> UpdateEnvironment(
        string key,
        string environment,
        UpdateEnvironmentRequest request,
        FlagpoleDbContext db,
        FlagCache cache,
        TimeProvider time,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!FlagRules.IsKnownEnvironment(environment))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["environment"] = [$"Use one of: {string.Join(", ", FlagRules.Environments)}."]
            });
        }

        var errors = FlagRules.ValidateEnvironmentUpdate(request.RolloutPercentage, request.TargetUsers);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var flag = await db.Flags.Include(f => f.Environments).SingleOrDefaultAsync(f => f.Key == key, ct);
        if (flag is null)
        {
            return TypedResults.NotFound();
        }

        var name = FlagRules.NormalizeEnvironment(environment);
        var settings = flag.GetEnvironment(name);
        if (settings is null)
        {
            settings = new FlagEnvironment { Name = name };
            flag.Environments.Add(settings);
        }

        settings.Enabled = request.Enabled;
        settings.RolloutPercentage = request.RolloutPercentage;
        settings.TargetUsers = request.TargetUsers?.Distinct(StringComparer.Ordinal).ToList() ?? [];

        var now = time.GetUtcNow();
        flag.UpdatedAt = now;
        db.AuditEntries.Add(Audit(flag.Key, "environment.updated", name, user, now, new
        {
            settings.Enabled,
            settings.RolloutPercentage,
            TargetUserCount = settings.TargetUsers.Count
        }));

        await db.SaveChangesAsync(ct);
        cache.Invalidate(flag.Key);

        return TypedResults.Ok(FlagResponse.From(flag));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteFlag(
        string key,
        FlagpoleDbContext db,
        FlagCache cache,
        TimeProvider time,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var flag = await db.Flags.SingleOrDefaultAsync(f => f.Key == key, ct);
        if (flag is null)
        {
            return TypedResults.NotFound();
        }

        db.Flags.Remove(flag);
        db.AuditEntries.Add(Audit(flag.Key, "flag.deleted", null, user, time.GetUtcNow(), details: null));
        await db.SaveChangesAsync(ct);
        cache.Invalidate(flag.Key);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<AuditEntryResponse>>> GetAuditLog(string key, FlagpoleDbContext db, CancellationToken ct)
    {
        // The audit log is kept after a flag is deleted, so this does not 404.
        var entries = await db.AuditEntries
            .AsNoTracking()
            .Where(a => a.FlagKey == key)
            .OrderByDescending(a => a.Id)
            .Take(100)
            .ToListAsync(ct);

        return TypedResults.Ok(entries.Select(AuditEntryResponse.From).ToList());
    }

    private static ProblemHttpResult DuplicateKey(string key) =>
        TypedResults.Problem($"A flag with key '{key}' already exists.", statusCode: StatusCodes.Status409Conflict);

    private static AuditEntry Audit(string flagKey, string action, string? environment, ClaimsPrincipal user, DateTimeOffset now, object? details) => new()
    {
        FlagKey = flagKey,
        Action = action,
        Environment = environment,
        Actor = user.Identity?.Name ?? "unknown",
        Timestamp = now,
        Details = details is null ? null : JsonSerializer.Serialize(details, JsonSerializerOptions.Web)
    };
}
