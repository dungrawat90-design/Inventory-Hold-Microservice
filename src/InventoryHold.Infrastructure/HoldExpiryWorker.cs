using InventoryHold.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryHold.Infrastructure;

/// <summary>Periodically expires holds that are past their lifetime and returns their stock.</summary>
public sealed class HoldExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<HoldOptions> options,
    TimeProvider time,
    ILogger<HoldExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.ExpiryIntervalSeconds), time);

        do
        {
            await RunOnceAsync(settings.ExpiryBatchSize, stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(int batchSize, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var holds = scope.ServiceProvider.GetRequiredService<HoldService>();

            // A full batch means more may be waiting; keep draining before sleeping again.
            int expired;
            do
            {
                expired = await holds.ExpireDueAsync(stoppingToken);
            }
            while (expired >= batchSize && !stoppingToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Not fatal: the next tick retries, and every transition is idempotent.
            logger.LogError(ex, "Hold expiry run failed; it will be retried on the next tick");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
