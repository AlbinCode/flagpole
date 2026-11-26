using System.Text.RegularExpressions;

namespace Flagpole.Core;

/// <summary>
/// Input rules shared by the API and the tests.
/// </summary>
public static partial class FlagRules
{
    public const int MaxTargetUsers = 100;
    public const int MaxUserIdLength = 128;
    public const int MaxDescriptionLength = 500;

    public static readonly IReadOnlyList<string> Environments = ["development", "staging", "production"];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$")]
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    public static bool IsKnownEnvironment(string? environment) =>
        environment is not null && Environments.Contains(environment, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeEnvironment(string environment) => environment.ToLowerInvariant();

    public static Dictionary<string, string[]> ValidateEnvironmentUpdate(int rolloutPercentage, IReadOnlyCollection<string>? targetUsers)
    {
        var errors = new Dictionary<string, string[]>();

        if (rolloutPercentage is < 0 or > 100)
        {
            errors["rolloutPercentage"] = ["Must be between 0 and 100."];
        }

        if (targetUsers is { Count: > MaxTargetUsers })
        {
            errors["targetUsers"] = [$"At most {MaxTargetUsers} target users are allowed."];
        }
        else if (targetUsers is not null && targetUsers.Any(u => string.IsNullOrWhiteSpace(u) || u.Length > MaxUserIdLength))
        {
            errors["targetUsers"] = [$"User ids must be non-empty and at most {MaxUserIdLength} characters."];
        }

        return errors;
    }
}
