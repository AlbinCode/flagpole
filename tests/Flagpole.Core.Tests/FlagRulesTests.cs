using Flagpole.Core;

namespace Flagpole.Core.Tests;

public class FlagRulesTests
{
    [Theory]
    [InlineData("new-checkout")]
    [InlineData("dark-mode-2")]
    [InlineData("ab")]
    public void Accepts_valid_keys(string key) => Assert.True(FlagRules.IsValidKey(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("New-Checkout")]
    [InlineData("-starts-with-dash")]
    [InlineData("has space")]
    [InlineData("has_underscore")]
    public void Rejects_invalid_keys(string? key) => Assert.False(FlagRules.IsValidKey(key));

    [Theory]
    [InlineData("production", true)]
    [InlineData("Staging", true)]
    [InlineData("qa", false)]
    [InlineData(null, false)]
    public void Knows_the_supported_environments(string? environment, bool expected) =>
        Assert.Equal(expected, FlagRules.IsKnownEnvironment(environment));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_rollout_outside_0_to_100(int percentage) =>
        Assert.Contains("rolloutPercentage", FlagRules.ValidateEnvironmentUpdate(percentage, []).Keys);

    [Fact]
    public void Rejects_too_many_target_users()
    {
        var users = Enumerable.Range(0, FlagRules.MaxTargetUsers + 1).Select(i => $"user-{i}").ToList();

        Assert.Contains("targetUsers", FlagRules.ValidateEnvironmentUpdate(50, users).Keys);
    }

    [Fact]
    public void Rejects_blank_target_users() =>
        Assert.Contains("targetUsers", FlagRules.ValidateEnvironmentUpdate(50, ["ok", " "]).Keys);

    [Fact]
    public void Accepts_a_valid_update() =>
        Assert.Empty(FlagRules.ValidateEnvironmentUpdate(25, ["tester"]));
}
