using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Flagpole.Api.Auth;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string AdminRole = "admin";
    public const string ReaderRole = "reader";
    public const string AdminPolicy = "admin";
    public const string ReaderPolicy = "reader";
}

public sealed class ApiKeyOptions
{
    public const string Section = "ApiKeys";

    [MinLength(1, ErrorMessage = "Configure at least one API key.")]
    public List<ApiKeyEntry> Keys { get; set; } = [];
}

public sealed class ApiKeyEntry
{
    /// <summary>Shown in the audit log, for example "ops-team" or "web-frontend".</summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>The secret. Comes from Key Vault in Azure, never from source control.</summary>
    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Required, AllowedValues(ApiKeyDefaults.AdminRole, ApiKeyDefaults.ReaderRole)]
    public string Role { get; set; } = ApiKeyDefaults.ReaderRole;
}

/// <summary>
/// Authenticates requests by the X-Api-Key header. Admin keys manage flags,
/// reader keys can only evaluate them.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    IOptionsMonitor<ApiKeyOptions> apiKeys,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var provided) || string.IsNullOrEmpty(provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided.ToString()));

        foreach (var entry in apiKeys.CurrentValue.Keys)
        {
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(entry.Key));

            // Constant-time comparison so response timing does not reveal how
            // much of a guessed key was correct.
            if (CryptographicOperations.FixedTimeEquals(providedHash, expectedHash))
            {
                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, entry.Name), new Claim(ClaimTypes.Role, entry.Role)],
                    ApiKeyDefaults.Scheme);

                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.Scheme)));
            }
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }
}
