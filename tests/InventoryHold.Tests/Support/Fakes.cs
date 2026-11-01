using InventoryHold.Application.Abstractions;
using InventoryHold.Application.Contracts;
using InventoryHold.Domain;

namespace InventoryHold.Tests.Support;

public sealed class InMemoryInventoryRepository : IInventoryRepository
{
    private readonly object _gate = new();
    private readonly SortedDictionary<string, (int Available, int Held)> _stock = new(StringComparer.Ordinal);
    private int _listCalls;

    public int ListCalls => _listCalls;

    public HashSet<string> FailRestoreFor { get; } = new(StringComparer.Ordinal);

    public void Seed(string sku, int available)
    {
        lock (_gate)
        {
            _stock[sku] = (available, 0);
        }
    }

    public InventoryItem Get(string sku)
    {
        lock (_gate)
        {
            var (available, held) = _stock[sku];
            return new InventoryItem(sku, available, held);
        }
    }

    public async Task<ReserveResult> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (_gate)
        {
            if (!_stock.TryGetValue(sku, out var current))
            {
                return ReserveResult.UnknownSku;
            }

            if (current.Available < quantity)
            {
                return ReserveResult.Insufficient;
            }

            _stock[sku] = (current.Available - quantity, current.Held + quantity);
            return ReserveResult.Reserved;
        }
    }

    public async Task RestoreAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (_gate)
        {
            if (FailRestoreFor.Contains(sku))
            {
                throw new InvalidOperationException("Simulated restore failure.");
            }

            if (!_stock.TryGetValue(sku, out var current) || current.Held < quantity)
            {
                throw new InventoryInconsistencyException(sku);
            }

            _stock[sku] = (current.Available + quantity, current.Held - quantity);
        }
    }

    public Task<(IReadOnlyList<InventoryItem> Items, long TotalCount)> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _listCalls);
        lock (_gate)
        {
            var all = _stock.Select(kv => new InventoryItem(kv.Key, kv.Value.Available, kv.Value.Held)).ToList();
            IReadOnlyList<InventoryItem> page = all.Skip(skip).Take(take).ToList();
            return Task.FromResult((page, (long)all.Count));
        }
    }
}

public sealed class InMemoryHoldRepository : IHoldRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Hold> _holds = new(StringComparer.Ordinal);

    public bool FailInsert { get; set; }

    public Task InsertAsync(Hold hold, CancellationToken cancellationToken)
    {
        if (FailInsert)
        {
            throw new InvalidOperationException("Simulated insert failure.");
        }

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

    public async Task<Hold?> TryTransitionAsync(string holdId, HoldStatus target, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await Task.Yield();
        lock (_gate)
        {
            if (!_holds.TryGetValue(holdId, out var current) || current.Status != HoldStatus.Active)
            {
                return null;
            }

            var updated = current with { Status = target, ReleasedAt = at };
            _holds[holdId] = updated;
            return updated;
        }
    }

    public Task<IReadOnlyList<Hold>> FindDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Hold> due = _holds.Values
                .Where(h => h.IsDue(now))
                .OrderBy(h => h.ExpiresAt)
                .Take(limit)
                .ToList();
            return Task.FromResult(due);
        }
    }
}

public sealed class FakeInventoryCache : IInventoryCache
{
    private readonly object _gate = new();
    private readonly Dictionary<(int Skip, int Take), InventoryPage> _pages = new();
    private int _invalidations;
    private int _generation;

    public int Invalidations => _invalidations;

    public Task<InventoryCacheLookup> GetAsync(int skip, int take, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(new InventoryCacheLookup(_pages.GetValueOrDefault((skip, take)), _generation.ToString()));
        }
    }

    public Task SetAsync(int skip, int take, InventoryPage page, string token, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (token == _generation.ToString())
            {
                _pages[(skip, take)] = page;
            }
        }

        return Task.CompletedTask;
    }

    public Task InvalidateAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _pages.Clear();
            _generation++;
        }

        Interlocked.Increment(ref _invalidations);
        return Task.CompletedTask;
    }
}

public sealed class RecordingEventPublisher : IHoldEventPublisher
{
    private readonly object _gate = new();
    private readonly List<object> _published = new();

    public bool Fail { get; set; }

    public IReadOnlyList<object> Published
    {
        get
        {
            lock (_gate)
            {
                return _published.ToList();
            }
        }
    }

    public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken)
        where TEvent : class
    {
        if (Fail)
        {
            throw new InvalidOperationException("Simulated broker outage.");
        }

        lock (_gate)
        {
            _published.Add(message);
        }

        return Task.CompletedTask;
    }
}
