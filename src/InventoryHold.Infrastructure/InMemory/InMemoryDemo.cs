using InventoryHold.Application.Abstractions;
using InventoryHold.Application.Contracts;
using InventoryHold.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InventoryHold.Infrastructure.InMemory;

/// <summary>
/// Demo-only stand-ins that let the API run on a machine with no MongoDB, Redis or RabbitMQ.
/// State lives in process memory and is lost on restart. Enabled with <c>Demo__InMemory=true</c>.
/// </summary>
public static class InMemoryDemo
{
    public static IServiceCollection AddInventoryHoldInMemoryDemo(this IServiceCollection services)
    {
        services.AddSingleton<IInventoryRepository>(new InMemoryInventoryRepository(
            ("DEMO-1001", 25), ("DEMO-1002", 10), ("DEMO-1003", 5), ("DEMO-1004", 100), ("DEMO-1005", 0)));
        services.AddSingleton<IHoldRepository, InMemoryHoldRepository>();
        services.AddSingleton<IInventoryCache, NoInventoryCache>();
        services.AddSingleton<IHoldEventPublisher, LoggingHoldEventPublisher>();
        services.AddHostedService<HoldExpiryWorker>();
        return services;
    }
}

internal sealed class InMemoryInventoryRepository : IInventoryRepository
{
    private readonly object _gate = new();
    private readonly SortedDictionary<string, (int Available, int Held)> _stock = new(StringComparer.Ordinal);

    public InMemoryInventoryRepository(params (string Sku, int Available)[] seed)
    {
        foreach (var (sku, available) in seed)
        {
            _stock[sku] = (available, 0);
        }
    }

    public Task<ReserveResult> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_stock.TryGetValue(sku, out var current))
            {
                return Task.FromResult(ReserveResult.UnknownSku);
            }

            if (current.Available < quantity)
            {
                return Task.FromResult(ReserveResult.Insufficient);
            }

            _stock[sku] = (current.Available - quantity, current.Held + quantity);
            return Task.FromResult(ReserveResult.Reserved);
        }
    }

    public Task RestoreAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_stock.TryGetValue(sku, out var current) || current.Held < quantity)
            {
                throw new InventoryInconsistencyException(sku);
            }

            _stock[sku] = (current.Available + quantity, current.Held - quantity);
            return Task.CompletedTask;
        }
    }

    public Task<(IReadOnlyList<InventoryItem> Items, long TotalCount)> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var all = _stock.Select(kv => new InventoryItem(kv.Key, kv.Value.Available, kv.Value.Held)).ToList();
            IReadOnlyList<InventoryItem> page = all.Skip(skip).Take(take).ToList();
            return Task.FromResult((page, (long)all.Count));
        }
    }
}

internal sealed class InMemoryHoldRepository : IHoldRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Hold> _holds = new(StringComparer.Ordinal);

    public Task InsertAsync(Hold hold, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _holds[hold.Id] = hold;
        }

        return Task.CompletedTask;
    }

    public Task<Hold?> GetAsync(string holdId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_holds.GetValueOrDefault(holdId));
        }
    }

    public Task<Hold?> TryTransitionAsync(string holdId, HoldStatus target, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_holds.TryGetValue(holdId, out var current) || current.Status != HoldStatus.Active)
            {
                return Task.FromResult<Hold?>(null);
            }

            var updated = current with { Status = target, ReleasedAt = at };
            _holds[holdId] = updated;
            return Task.FromResult<Hold?>(updated);
        }
    }

    public Task<IReadOnlyList<Hold>> FindDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Hold> due = _holds.Values.Where(h => h.IsDue(now)).OrderBy(h => h.ExpiresAt).Take(limit).ToList();
            return Task.FromResult(due);
        }
    }
}

internal sealed class NoInventoryCache : IInventoryCache
{
    public Task<InventoryCacheLookup> GetAsync(int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult(new InventoryCacheLookup(null, string.Empty));

    public Task SetAsync(int skip, int take, InventoryPage page, string token, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InvalidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class LoggingHoldEventPublisher(ILogger<LoggingHoldEventPublisher> logger) : IHoldEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken)
        where TEvent : class
    {
        logger.LogInformation("Event published (demo, not sent to a broker): {EventType} {@Event}", typeof(TEvent).Name, message);
        return Task.CompletedTask;
    }
}
