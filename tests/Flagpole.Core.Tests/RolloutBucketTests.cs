using Flagpole.Core;

namespace Flagpole.Core.Tests;

public class RolloutBucketTests
{
    [Fact]
    public void Same_input_always_gives_the_same_bucket()
    {
        Assert.Equal(RolloutBucket.For("new-checkout", "user-42"), RolloutBucket.For("new-checkout", "user-42"));
    }

    [Fact]
    public void Bucket_is_between_0_and_100()
    {
        for (var i = 0; i < 1_000; i++)
        {
            var bucket = RolloutBucket.For("flag", $"user-{i}");
            Assert.InRange(bucket, 0, 99.99);
        }
    }

    [Fact]
    public void Different_flags_place_the_same_user_differently()
    {
        var differences = Enumerable.Range(0, 100)
            .Count(i => RolloutBucket.For("flag-a", $"user-{i}") != RolloutBucket.For("flag-b", $"user-{i}"));

        Assert.True(differences > 90);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(90)]
    public void Rollout_share_is_close_to_the_percentage(int percentage)
    {
        const int users = 20_000;

        var included = Enumerable.Range(0, users)
            .Count(i => RolloutBucket.For("new-checkout", $"user-{i}") < percentage);

        var share = included * 100.0 / users;
        Assert.InRange(share, percentage - 1.5, percentage + 1.5);
    }
}
