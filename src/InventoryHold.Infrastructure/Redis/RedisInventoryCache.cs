using System.Text.Json;
using InventoryHold.Application.Abstractions;
using InventoryHold.Application.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure.Redis;

/// <summary>
/// Caches inventory pages in Redis. Invalidation increments a generation counter that is part of every key, so one
/// INCR retires all cached pages. A reader passes back the generation it read in; a page loaded before an invalidation
/// is therefore written under a retired key and never served. Redis failures degrade to a cache miss.
/// </summary>
internal sealed class RedisInventoryCache(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> options,
    ILogger<RedisInventoryCache> logger) : IInventoryCache
{
    private const string GenerationKey = "inventory:generation";

    public async Task<InventoryCacheLookup> GetAsync(int skip, int take, CancellationToken cancellationToken)
    {
        try
        {
            var database = redis.GetDatabase();
            var generation = await GetGenerationAsync(database);
            var cached = await database.StringGetAsync(PageKey(generation, skip, take));
            if (!cached.HasValue)
            {
                return new InventoryCacheLookup(null, generation);
            }

            return new InventoryCacheLookup(JsonSerializer.Deserialize<InventoryPage>((string)cached!), generation);
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            logger.LogWarning(ex, "Inventory cache read failed; serving from the database");
            return new InventoryCacheLookup(null, string.Empty);
        }
    }

    public async Task SetAsync(int skip, int take, InventoryPage page, string token, CancellationToken cancellationToken)
    {
        if (token.Length == 0)
        {
            return;
        }

        try
        {
            var database = redis.GetDatabase();
            await database.StringSetAsync(
                PageKey(token, skip, take),
                JsonSerializer.Serialize(page),
                TimeSpan.FromSeconds(options.Value.InventoryTtlSeconds));
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Inventory cache write failed");
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().StringIncrementAsync(GenerationKey);
        }
        catch (RedisException ex)
        {
            // Entries are short-lived, so a failed invalidation is bounded by the TTL.
            logger.LogWarning(ex, "Inventory cache invalidation failed; entries expire after {TtlSeconds} seconds", options.Value.InventoryTtlSeconds);
        }
    }

    private static async Task<string> GetGenerationAsync(IDatabase database)
    {
        var value = await database.StringGetAsync(GenerationKey);
        return value.HasValue ? (string)value! : "0";
    }

    private static string PageKey(string generation, int skip, int take) => $"inventory:page:g{generation}:{skip}:{take}";
}
