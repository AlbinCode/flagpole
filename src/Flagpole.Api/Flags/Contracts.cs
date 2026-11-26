using Flagpole.Api.Data;
using Flagpole.Core;

namespace Flagpole.Api.Flags;

public sealed record CreateFlagRequest(string Key, string? Description);

public sealed record UpdateEnvironmentRequest(bool Enabled, int RolloutPercentage, List<string>? TargetUsers);

public sealed record BatchEvaluationRequest(string Environment, string? UserId, List<string> Flags);

public sealed record EnvironmentResponse(string Name, bool Enabled, int RolloutPercentage, IReadOnlyList<string> TargetUsers);

public sealed record FlagResponse(
    string Key,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EnvironmentResponse> Environments)
{
    public static FlagResponse From(FeatureFlag flag) => new(
        flag.Key,
        flag.Description,
        flag.CreatedAt,
        flag.UpdatedAt,
        [.. flag.Environments
            .OrderBy(e => e.Name)
            .Select(e => new EnvironmentResponse(e.Name, e.Enabled, e.RolloutPercentage, e.TargetUsers))]);
}

public sealed record AuditEntryResponse(string Action, string? Environment, string Actor, DateTimeOffset Timestamp, string? Details)
{
    public static AuditEntryResponse From(AuditEntry entry) =>
        new(entry.Action, entry.Environment, entry.Actor, entry.Timestamp, entry.Details);
}
