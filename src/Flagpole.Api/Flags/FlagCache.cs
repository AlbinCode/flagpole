using Flagpole.Api.Data;
using Flagpole.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Flagpole.Api.Flags;

/// <summary>
/// Keeps flags in memory so evaluations, by far the most common request,
/// rarely touch the database.
/// </summary>
/// <remarks>
/// Writes on this instance invalidate the entry immediately. Other replicas
/// pick up the change when their entry expires, so the TTL is the upper
/// bound for how stale a flag can be. A shared cache or pub/sub could remove
/// that delay if it ever matters.
/// </remarks>
public sealed class FlagCache(IMemoryCache cache, IServiceScopeFactory scopeFactory)
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    private sealed record Entry(FeatureFlag? Flag);

    public async Task<FeatureFlag?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey(key), out Entry? cached) && cached is not null)
        {
            return cached.Flag;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlagpoleDbContext>();

        var flag = await db.Flags
            .AsNoTracking()
            .Include(f => f.Environments)
            .SingleOrDefaultAsync(f => f.Key == key, cancellationToken);

        // Unknown keys are cached too, so a client asking for a deleted flag
        // cannot cause a database query on every request.
        cache.Set(CacheKey(key), new Entry(flag), TimeToLive);
        return flag;
    }

    public void Invalidate(string key) => cache.Remove(CacheKey(key));

    private static string CacheKey(string key) => $"flag:{key}";
}
