using Flagpole.Core;

namespace Flagpole.Core.Tests;

public class FlagEvaluatorTests
{
    private static FeatureFlag Flag(bool enabled = true, int rollout = 100, params string[] targetUsers) => new()
    {
        Key = "new-checkout",
        Environments =
        [
            new FlagEnvironment
            {
                Name = "production",
                Enabled = enabled,
                RolloutPercentage = rollout,
                TargetUsers = [.. targetUsers]
            }
        ]
    };

    [Fact]
    public void Missing_flag_is_off()
    {
        var result = FlagEvaluator.Evaluate(null, "unknown", "production", "user-1");

        Assert.False(result.Enabled);
        Assert.Equal(EvaluationReason.FlagNotFound, result.Reason);
    }

    [Fact]
    public void Environment_without_configuration_is_off()
    {
        var result = FlagEvaluator.Evaluate(Flag(), "new-checkout", "staging", "user-1");

        Assert.False(result.Enabled);
        Assert.Equal(EvaluationReason.EnvironmentNotConfigured, result.Reason);
    }

    [Fact]
    public void Environment_name_is_case_insensitive()
    {
        var result = FlagEvaluator.Evaluate(Flag(), "new-checkout", "PRODUCTION", "user-1");

        Assert.True(result.Enabled);
    }

    [Fact]
    public void Disabled_flag_is_off_even_for_target_users()
    {
        var result = FlagEvaluator.Evaluate(Flag(enabled: false, rollout: 100, "tester"), "new-checkout", "production", "tester");

        Assert.False(result.Enabled);
        Assert.Equal(EvaluationReason.Disabled, result.Reason);
    }

    [Fact]
    public void Target_user_is_on_even_at_zero_percent()
    {
        var result = FlagEvaluator.Evaluate(Flag(rollout: 0, targetUsers: "tester"), "new-checkout", "production", "tester");

        Assert.True(result.Enabled);
        Assert.Equal(EvaluationReason.TargetedUser, result.Reason);
    }

    [Fact]
    public void Full_rollout_is_on_without_a_user()
    {
        var result = FlagEvaluator.Evaluate(Flag(rollout: 100), "new-checkout", "production", userId: null);

        Assert.True(result.Enabled);
        Assert.Equal(EvaluationReason.FullRollout, result.Reason);
    }

    [Fact]
    public void Zero_percent_rollout_is_off()
    {
        var result = FlagEvaluator.Evaluate(Flag(rollout: 0), "new-checkout", "production", "user-1");

        Assert.False(result.Enabled);
        Assert.Equal(EvaluationReason.RolloutExcluded, result.Reason);
    }

    [Fact]
    public void Partial_rollout_without_a_user_is_off()
    {
        var result = FlagEvaluator.Evaluate(Flag(rollout: 50), "new-checkout", "production", userId: null);

        Assert.False(result.Enabled);
        Assert.Equal(EvaluationReason.MissingUserForRollout, result.Reason);
    }

    [Fact]
    public void Partial_rollout_matches_the_users_bucket()
    {
        var flag = Flag(rollout: 30);

        for (var i = 0; i < 200; i++)
        {
            var userId = $"user-{i}";
            var expected = RolloutBucket.For(flag.Key, userId) < 30;

            var result = FlagEvaluator.Evaluate(flag, flag.Key, "production", userId);

            Assert.Equal(expected, result.Enabled);
            Assert.Equal(expected ? EvaluationReason.RolloutIncluded : EvaluationReason.RolloutExcluded, result.Reason);
        }
    }

    [Fact]
    public void Raising_the_rollout_never_turns_a_user_off()
    {
        // Users who already have a feature must keep it when the rollout grows.
        for (var i = 0; i < 500; i++)
        {
            var userId = $"user-{i}";
            var onAt20 = FlagEvaluator.Evaluate(Flag(rollout: 20), "new-checkout", "production", userId).Enabled;
            var onAt60 = FlagEvaluator.Evaluate(Flag(rollout: 60), "new-checkout", "production", userId).Enabled;

            Assert.True(!onAt20 || onAt60, $"{userId} was on at 20% but off at 60%.");
        }
    }
}
