namespace Flagpole.Core;

/// <summary>
/// A feature flag with one configuration per environment.
/// </summary>
public sealed class FeatureFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable identifier used by clients, for example "new-checkout".</summary>
    public required string Key { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<FlagEnvironment> Environments { get; set; } = [];

    public FlagEnvironment? GetEnvironment(string environment) =>
        Environments.FirstOrDefault(e => string.Equals(e.Name, environment, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// How a flag behaves in one environment, such as staging or production.
/// </summary>
public sealed class FlagEnvironment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    /// <summary>Kill switch. When false the flag is off for everyone.</summary>
    public bool Enabled { get; set; }

    /// <summary>Share of users, 0 to 100, that get the flag when it is enabled.</summary>
    public int RolloutPercentage { get; set; }

    /// <summary>Users that always get the flag when it is enabled, for example testers.</summary>
    public List<string> TargetUsers { get; set; } = [];
}
