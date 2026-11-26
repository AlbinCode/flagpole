using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flagpole.Api.Flags;
using Flagpole.Core;

namespace Flagpole.Api.Tests;

public class FlagApiTests : IClassFixture<FlagpoleApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _admin;
    private readonly HttpClient _reader;
    private readonly HttpClient _anonymous;

    public FlagApiTests(FlagpoleApiFactory factory)
    {
        _admin = factory.CreateClient(FlagpoleApiFactory.AdminKey);
        _reader = factory.CreateClient(FlagpoleApiFactory.ReaderKey);
        _anonymous = factory.CreateClient(apiKey: null);
    }

    private static string NewKey() => $"flag-{Guid.NewGuid():N}"[..20];

    private async Task<string> CreateFlag(string? key = null)
    {
        key ??= NewKey();
        var response = await _admin.PostAsJsonAsync("/api/flags", new CreateFlagRequest(key, "Test flag"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return key;
    }

    private Task<HttpResponseMessage> Configure(string key, string environment, bool enabled, int rollout, params string[] targetUsers) =>
        _admin.PutAsJsonAsync($"/api/flags/{key}/environments/{environment}",
            new UpdateEnvironmentRequest(enabled, rollout, [.. targetUsers]));

    private async Task<EvaluationResult> Evaluate(string key, string environment = "production", string? userId = "user-1")
    {
        var url = $"/api/evaluate/{key}?environment={environment}" + (userId is null ? "" : $"&userId={userId}");
        var response = await _reader.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EvaluationResult>(Json))!;
    }

    [Fact]
    public async Task Liveness_check_needs_no_key()
    {
        var response = await _anonymous.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Requests_without_a_key_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/flags")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/evaluate/x?environment=production")).StatusCode);
    }

    [Fact]
    public async Task Requests_with_a_wrong_key_are_rejected()
    {
        using var factory = new FlagpoleApiFactory();
        var client = factory.CreateClient("not-a-real-key-000000000000000000000000");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/flags")).StatusCode);
    }

    [Fact]
    public async Task Reader_keys_cannot_manage_flags()
    {
        var response = await _reader.PostAsJsonAsync("/api/flags", new CreateFlagRequest(NewKey(), null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Created_flag_is_off_until_configured()
    {
        var key = await CreateFlag();

        var result = await Evaluate(key);

        Assert.False(result.Enabled);
        Assert.Equal(EvaluationReason.EnvironmentNotConfigured, result.Reason);
    }

    [Fact]
    public async Task Enabled_flag_is_on_for_everyone_at_full_rollout()
    {
        var key = await CreateFlag();
        Assert.Equal(HttpStatusCode.OK, (await Configure(key, "production", enabled: true, rollout: 100)).StatusCode);

        var result = await Evaluate(key, userId: null);

        Assert.True(result.Enabled);
        Assert.Equal(EvaluationReason.FullRollout, result.Reason);
    }

    [Fact]
    public async Task Target_users_get_the_flag_before_the_rollout()
    {
        var key = await CreateFlag();
        await Configure(key, "production", enabled: true, rollout: 0, "tester");

        Assert.True((await Evaluate(key, userId: "tester")).Enabled);
        Assert.False((await Evaluate(key, userId: "someone-else")).Enabled);
    }

    [Fact]
    public async Task Environments_are_independent()
    {
        var key = await CreateFlag();
        await Configure(key, "staging", enabled: true, rollout: 100);

        Assert.True((await Evaluate(key, "staging")).Enabled);
        Assert.False((await Evaluate(key, "production")).Enabled);
    }

    [Fact]
    public async Task Changes_are_visible_immediately_despite_the_cache()
    {
        var key = await CreateFlag();
        await Configure(key, "production", enabled: false, rollout: 100);
        Assert.False((await Evaluate(key)).Enabled);

        await Configure(key, "production", enabled: true, rollout: 100);

        Assert.True((await Evaluate(key)).Enabled);
    }

    [Theory]
    [InlineData("Not Valid")]
    [InlineData("a")]
    [InlineData("UPPER")]
    public async Task Invalid_keys_are_rejected(string key)
    {
        var response = await _admin.PostAsJsonAsync("/api/flags", new CreateFlagRequest(key, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"key\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Duplicate_keys_return_conflict()
    {
        var key = await CreateFlag();

        var response = await _admin.PostAsJsonAsync("/api/flags", new CreateFlagRequest(key, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("qa", 50)]
    [InlineData("production", 150)]
    [InlineData("production", -1)]
    public async Task Invalid_environment_updates_are_rejected(string environment, int rollout)
    {
        var key = await CreateFlag();

        var response = await Configure(key, environment, enabled: true, rollout);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_missing_flag_returns_not_found()
    {
        var response = await Configure(NewKey(), "production", enabled: true, rollout: 100);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_flags_evaluate_to_off()
    {
        var result = await Evaluate("does-not-exist");

        Assert.False(result.Enabled);
        Assert.Equal(EvaluationReason.FlagNotFound, result.Reason);
    }

    [Fact]
    public async Task Deleted_flag_evaluates_to_off_and_keeps_its_audit_log()
    {
        var key = await CreateFlag();
        await Configure(key, "production", enabled: true, rollout: 100);
        Assert.True((await Evaluate(key)).Enabled);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/flags/{key}")).StatusCode);

        Assert.Equal(EvaluationReason.FlagNotFound, (await Evaluate(key)).Reason);
        var audit = await _admin.GetFromJsonAsync<List<AuditEntryResponse>>($"/api/flags/{key}/audit", Json);
        Assert.Equal(["flag.deleted", "environment.updated", "flag.created"], audit!.Select(a => a.Action));
    }

    [Fact]
    public async Task Audit_log_records_who_made_each_change()
    {
        var key = await CreateFlag();
        await Configure(key, "production", enabled: true, rollout: 25, "tester");

        var audit = await _admin.GetFromJsonAsync<List<AuditEntryResponse>>($"/api/flags/{key}/audit", Json);

        Assert.All(audit!, entry => Assert.Equal("test-admin", entry.Actor));
        var update = audit!.First();
        Assert.Equal("environment.updated", update.Action);
        Assert.Equal("production", update.Environment);
        Assert.Contains("\"rolloutPercentage\":25", update.Details);
    }

    [Fact]
    public async Task Batch_evaluation_returns_one_result_per_flag()
    {
        var on = await CreateFlag();
        var off = await CreateFlag();
        await Configure(on, "production", enabled: true, rollout: 100);
        await Configure(off, "production", enabled: false, rollout: 100);

        var response = await _reader.PostAsJsonAsync("/api/evaluate",
            new BatchEvaluationRequest("production", "user-1", [on, off, "missing-flag"]));
        var results = await response.Content.ReadFromJsonAsync<List<EvaluationResult>>(Json);

        Assert.Equal(
            [(on, true, EvaluationReason.FullRollout), (off, false, EvaluationReason.Disabled), ("missing-flag", false, EvaluationReason.FlagNotFound)],
            results!.Select(r => (r.FlagKey, r.Enabled, r.Reason)));
    }

    [Fact]
    public async Task Batch_evaluation_limits_the_number_of_flags()
    {
        var keys = Enumerable.Range(0, EvaluationEndpoints.MaxFlagsPerBatch + 1).Select(i => $"flag-{i}").ToList();

        var response = await _reader.PostAsJsonAsync("/api/evaluate", new BatchEvaluationRequest("production", null, keys));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Evaluation_rejects_unknown_environments()
    {
        var response = await _reader.GetAsync("/api/evaluate/some-flag?environment=qa");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public class RateLimitTests
{
    [Fact]
    public async Task Evaluations_are_rate_limited_per_key()
    {
        using var factory = new FlagpoleApiFactory { EvaluationsPerMinute = 3 };
        var client = factory.CreateClient(FlagpoleApiFactory.ReaderKey);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.GetAsync("/api/evaluate/some-flag?environment=production")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}
