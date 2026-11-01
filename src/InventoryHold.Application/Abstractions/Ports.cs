using InventoryHold.Application.Contracts;
using InventoryHold.Domain;

namespace InventoryHold.Application.Abstractions;

public enum ReserveResult
{
    Reserved,
    Insufficient,
    UnknownSku,
}

public interface IInventoryRepository
{
    /// <summary>Atomically moves <paramref name="quantity"/> from available to held when enough is available.</summary>
    Task<ReserveResult> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken);

    /// <summary>Atomically moves <paramref name="quantity"/> from held back to available.</summary>
    /// <exception cref="InventoryInconsistencyException">Held quantity is lower than <paramref name="quantity"/>.</exception>
    Task RestoreAsync(string sku, int quantity, CancellationToken cancellationToken);

    Task<(IReadOnlyList<InventoryItem> Items, long TotalCount)> ListAsync(int skip, int take, CancellationToken cancellationToken);
}

public interface IHoldRepository
{
    Task InsertAsync(Hold hold, CancellationToken cancellationToken);

    Task<Hold?> GetAsync(string holdId, CancellationToken cancellationToken);

    /// <summary>
    /// Moves an Active hold to <paramref name="target"/>. Returns the updated hold only for the caller that won
    /// the transition; returns null when the hold does not exist or is no longer Active.
    /// </summary>
    Task<Hold?> TryTransitionAsync(string holdId, HoldStatus target, DateTimeOffset at, CancellationToken cancellationToken);

    Task<IReadOnlyList<Hold>> FindDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Result of a cache read. <paramref name="Token"/> identifies the cache generation the read happened in and must be
/// passed back to <see cref="IInventoryCache.SetAsync"/>, so a page loaded before an invalidation is never stored
/// as if it were current.
/// </summary>
public sealed record InventoryCacheLookup(InventoryPage? Page, string Token);

public interface IInventoryCache
{
    Task<InventoryCacheLookup> GetAsync(int skip, int take, CancellationToken cancellationToken);

    Task SetAsync(int skip, int take, InventoryPage page, string token, CancellationToken cancellationToken);

    /// <summary>Called after every stock change. Implementations must not throw.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}

public interface IHoldEventPublisher
{
    Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken)
        where TEvent : class;
}
