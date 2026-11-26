using Flagpole.Api.Auth;
using Flagpole.Core;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Flagpole.Api.Flags;

public static class EvaluationEndpoints
{
    public const string RateLimitPolicy = "evaluate";
    public const int MaxFlagsPerBatch = 50;

    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var evaluate = app.MapGroup("/api/evaluate")
            .WithTags("Evaluation")
            .RequireAuthorization(ApiKeyDefaults.ReaderPolicy)
            .RequireRateLimiting(RateLimitPolicy);

        evaluate.MapGet("/{key}", EvaluateOne);
        evaluate.MapPost("/", EvaluateMany);

        return app;
    }

    /// <summary>
    /// Unknown flags evaluate to off instead of 404, so a client that asks for
    /// a removed flag falls back to the old behaviour instead of failing.
    /// </summary>
    private static async Task<Results<Ok<EvaluationResult>, ValidationProblem>> EvaluateOne(
        string key,
        string environment,
        string? userId,
        FlagCache cache,
        CancellationToken ct)
    {
        if (Validate(environment, userId) is { } problem)
        {
            return problem;
        }

        var flag = await cache.GetAsync(key, ct);
        return TypedResults.Ok(FlagEvaluator.Evaluate(flag, key, FlagRules.NormalizeEnvironment(environment), userId));
    }

    private static async Task<Results<Ok<List<EvaluationResult>>, ValidationProblem>> EvaluateMany(
        BatchEvaluationRequest request,
        FlagCache cache,
        CancellationToken ct)
    {
        if (Validate(request.Environment, request.UserId) is { } problem)
        {
            return problem;
        }

        if (request.Flags is not { Count: > 0 and <= MaxFlagsPerBatch })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["flags"] = [$"Send between 1 and {MaxFlagsPerBatch} flag keys."]
            });
        }

        var environment = FlagRules.NormalizeEnvironment(request.Environment);
        var results = new List<EvaluationResult>(request.Flags.Count);

        foreach (var key in request.Flags.Distinct(StringComparer.Ordinal))
        {
            var flag = await cache.GetAsync(key, ct);
            results.Add(FlagEvaluator.Evaluate(flag, key, environment, request.UserId));
        }

        return TypedResults.Ok(results);
    }

    private static ValidationProblem? Validate(string? environment, string? userId)
    {
        if (!FlagRules.IsKnownEnvironment(environment))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["environment"] = [$"Use one of: {string.Join(", ", FlagRules.Environments)}."]
            });
        }

        if (userId is { Length: > FlagRules.MaxUserIdLength })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = [$"At most {FlagRules.MaxUserIdLength} characters."]
            });
        }

        return null;
    }
}
