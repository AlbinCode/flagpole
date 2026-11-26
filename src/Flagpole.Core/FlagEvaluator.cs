namespace Flagpole.Core;

public enum EvaluationReason
{
    FlagNotFound,
    EnvironmentNotConfigured,
    Disabled,
    TargetedUser,
    FullRollout,
    RolloutIncluded,
    RolloutExcluded,
    MissingUserForRollout
}

public sealed record EvaluationResult(string FlagKey, string Environment, bool Enabled, EvaluationReason Reason);

/// <summary>
/// Decides whether a flag is on for a given user. Pure logic with no I/O,
/// so every rule can be unit tested.
/// </summary>
public static class FlagEvaluator
{
    public static EvaluationResult Evaluate(FeatureFlag? flag, string flagKey, string environment, string? userId)
    {
        if (flag is null)
        {
            return new(flagKey, environment, false, EvaluationReason.FlagNotFound);
        }

        var settings = flag.GetEnvironment(environment);
        if (settings is null)
        {
            return Result(false, EvaluationReason.EnvironmentNotConfigured);
        }

        if (!settings.Enabled)
        {
            return Result(false, EvaluationReason.Disabled);
        }

        if (userId is not null && settings.TargetUsers.Contains(userId, StringComparer.Ordinal))
        {
            return Result(true, EvaluationReason.TargetedUser);
        }

        if (settings.RolloutPercentage >= 100)
        {
            return Result(true, EvaluationReason.FullRollout);
        }

        if (settings.RolloutPercentage <= 0)
        {
            return Result(false, EvaluationReason.RolloutExcluded);
        }

        if (string.IsNullOrEmpty(userId))
        {
            // A partial rollout needs a stable user id, otherwise the same
            // person could flip between on and off on every request.
            return Result(false, EvaluationReason.MissingUserForRollout);
        }

        var included = RolloutBucket.For(flag.Key, userId) < settings.RolloutPercentage;
        return Result(included, included ? EvaluationReason.RolloutIncluded : EvaluationReason.RolloutExcluded);

        EvaluationResult Result(bool enabled, EvaluationReason reason) =>
            new(flag.Key, environment, enabled, reason);
    }
}
