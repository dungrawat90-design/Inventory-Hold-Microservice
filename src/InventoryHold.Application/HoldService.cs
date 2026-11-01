using FluentValidation;
using InventoryHold.Application.Abstractions;
using InventoryHold.Application.Contracts;
using InventoryHold.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryHold.Application;

public sealed class HoldService(
    IInventoryRepository inventory,
    IHoldRepository holds,
    IInventoryCache cache,
    IHoldEventPublisher events,
    IValidator<CreateHoldRequest> validator,
    IOptions<HoldOptions> options,
    TimeProvider time,
    ILogger<HoldService> logger)
{
    private readonly HoldOptions _settings = options.Value;

    public async Task<HoldResponse> CreateAsync(CreateHoldRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var lines = request.Items!
            .GroupBy(i => i.Sku!.Trim(), StringComparer.Ordinal)
            .Select(g => new HoldItem(g.Key, g.Sum(i => i.Quantity)))
            .OrderBy(i => i.Sku, StringComparer.Ordinal)
            .ToList();

        var ttl = TimeSpan.FromMinutes(request.TtlMinutes ?? _settings.DefaultTtlMinutes);
        var reserved = new List<HoldItem>();
        Hold hold;

        try
        {
            foreach (var line in lines)
            {
                var result = await inventory.TryReserveAsync(line.Sku, line.Quantity, cancellationToken);
                if (result == ReserveResult.Reserved)
                {
                    reserved.Add(line);
                    continue;
                }

                throw result == ReserveResult.UnknownSku
                    ? new SkuNotFoundException(line.Sku)
                    : new InsufficientStockException(line.Sku);
            }

            var now = time.GetUtcNow();
            hold = new Hold(Guid.NewGuid().ToString("N"), lines, HoldStatus.Active, now, now.Add(ttl), null);
            await holds.InsertAsync(hold, cancellationToken);
        }
        catch
        {
            await RollbackAsync(reserved);
            throw;
        }

        await cache.InvalidateAsync(CancellationToken.None);
        await PublishSafelyAsync(
            hold.Id,
            new HoldCreated(hold.Id, HoldResponse.From(hold).Items, hold.CreatedAt, hold.ExpiresAt));

        return HoldResponse.From(hold);
    }

    public async Task<HoldResponse> GetAsync(string holdId, CancellationToken cancellationToken)
    {
        var hold = await holds.GetAsync(holdId, cancellationToken) ?? throw new HoldNotFoundException(holdId);
        return HoldResponse.From(hold);
    }

    /// <summary>Releases an active hold. Releasing a hold that is already released or expired is a no-op.</summary>
    public async Task<HoldResponse> ReleaseAsync(string holdId, CancellationToken cancellationToken)
    {
        _ = await holds.GetAsync(holdId, cancellationToken) ?? throw new HoldNotFoundException(holdId);

        var released = await holds.TryTransitionAsync(holdId, HoldStatus.Released, time.GetUtcNow(), cancellationToken);
        if (released is null)
        {
            var current = await holds.GetAsync(holdId, cancellationToken) ?? throw new HoldNotFoundException(holdId);
            return HoldResponse.From(current);
        }

        await FinishTransitionAsync(released, new HoldReleased(released.Id, released.ReleasedAt ?? time.GetUtcNow()));
        return HoldResponse.From(released);
    }

    /// <summary>Expires every active hold that is past its expiry time. Returns how many this call expired.</summary>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var due = await holds.FindDueAsync(time.GetUtcNow(), _settings.ExpiryBatchSize, cancellationToken);
        var expired = 0;
        var failed = 0;
        Exception? firstFailure = null;

        foreach (var candidate in due)
        {
            try
            {
                var won = await holds.TryTransitionAsync(candidate.Id, HoldStatus.Expired, time.GetUtcNow(), cancellationToken);
                if (won is null)
                {
                    continue;
                }

                await FinishTransitionAsync(won, new HoldExpired(won.Id, won.ReleasedAt ?? time.GetUtcNow()));
                expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                firstFailure ??= ex;
            }
        }

        if (failed > 0)
        {
            logger.LogError(firstFailure, "Expiring due holds failed for {FailedCount} of {DueCount} holds", failed, due.Count);
        }

        return expired;
    }

    public async Task<InventoryPage> GetInventoryAsync(int? skip, int? take, CancellationToken cancellationToken)
    {
        var effectiveSkip = Math.Max(0, skip ?? 0);
        var effectiveTake = take is null or <= 0
            ? _settings.DefaultInventoryPageSize
            : Math.Min(take.Value, _settings.MaxInventoryPageSize);

        var lookup = await cache.GetAsync(effectiveSkip, effectiveTake, cancellationToken);
        if (lookup.Page is not null)
        {
            return lookup.Page;
        }

        var (items, total) = await inventory.ListAsync(effectiveSkip, effectiveTake, cancellationToken);
        var page = new InventoryPage(
            items.Select(i => new InventoryItemResponse(i.Sku, i.Available, i.Held, i.Total)).ToList(),
            effectiveSkip,
            effectiveTake,
            total);

        await cache.SetAsync(effectiveSkip, effectiveTake, page, lookup.Token, cancellationToken);
        return page;
    }

    // After the hold moved out of Active this caller owns the restore, so it must not be cancelled midway.
    private async Task FinishTransitionAsync<TEvent>(Hold hold, TEvent message)
        where TEvent : class
    {
        var failures = new List<Exception>();
        var failedSkus = new List<string>();

        foreach (var item in hold.Items)
        {
            try
            {
                await inventory.RestoreAsync(item.Sku, item.Quantity, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                failedSkus.Add(item.Sku);
            }
        }

        await cache.InvalidateAsync(CancellationToken.None);

        if (failures.Count > 0)
        {
            logger.LogError(
                failures[0],
                "Restoring stock for hold {HoldId} failed for {FailedCount} of {ItemCount} items, first SKU {Sku}",
                hold.Id,
                failures.Count,
                hold.Items.Count,
                failedSkus[0]);
            throw new AggregateException("Stock restore failed.", failures);
        }

        await PublishSafelyAsync(hold.Id, message);
    }

    private async Task RollbackAsync(IReadOnlyList<HoldItem> reserved)
    {
        var failed = 0;
        Exception? first = null;

        foreach (var item in reserved)
        {
            try
            {
                await inventory.RestoreAsync(item.Sku, item.Quantity, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failed++;
                first ??= ex;
            }
        }

        if (failed > 0)
        {
            logger.LogError(first, "Rolling back a failed hold left {FailedCount} of {ItemCount} items reserved", failed, reserved.Count);
        }
    }

    private async Task PublishSafelyAsync<TEvent>(string holdId, TEvent message)
        where TEvent : class
    {
        try
        {
            await events.PublishAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Publishing {EventType} failed for hold {HoldId}", typeof(TEvent).Name, holdId);
        }
    }
}
