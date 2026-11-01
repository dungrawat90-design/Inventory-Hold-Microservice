using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

/// <summary>Creates indexes at startup and, when enabled, seeds demo stock without overwriting existing documents.</summary>
internal sealed class MongoInitializer(
    MongoCollections collections,
    IOptions<SeedOptions> seedOptions,
    ILogger<MongoInitializer> logger) : IHostedService
{
    private static readonly (string Sku, int Available)[] DemoStock =
    [
        ("DEMO-1001", 25),
        ("DEMO-1002", 10),
        ("DEMO-1003", 5),
        ("DEMO-1004", 100),
        ("DEMO-1005", 0),
    ];

    private const int MaxAttempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // MongoDB can still be starting when the API is. Retry for about a minute instead of crashing the container.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await InitializeAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is MongoException or TimeoutException)
            {
                logger.LogWarning(ex, "MongoDB is not ready (attempt {Attempt} of {MaxAttempts}); retrying", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var expiryIndex = new CreateIndexModel<HoldDocument>(
            Builders<HoldDocument>.IndexKeys.Ascending(d => d.Status).Ascending(d => d.ExpiresAt),
            new CreateIndexOptions { Name = "ix_holds_status_expiresAt" });
        await collections.Holds.Indexes.CreateOneAsync(expiryIndex, cancellationToken: cancellationToken);

        if (!seedOptions.Value.Enabled)
        {
            return;
        }

        foreach (var (sku, available) in DemoStock)
        {
            await collections.Inventory.UpdateOneAsync(
                Builders<InventoryDocument>.Filter.Eq(d => d.Sku, sku),
                Builders<InventoryDocument>.Update
                    .SetOnInsert(d => d.Available, available)
                    .SetOnInsert(d => d.Held, 0),
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }

        logger.LogInformation("Seeded demo inventory with {SkuCount} SKUs", DemoStock.Length);
    }
}
