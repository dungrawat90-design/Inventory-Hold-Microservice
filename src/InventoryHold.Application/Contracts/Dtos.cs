using InventoryHold.Domain;

namespace InventoryHold.Application.Contracts;

public sealed record HoldItemRequest(string? Sku, int Quantity);

public sealed record CreateHoldRequest(IReadOnlyList<HoldItemRequest>? Items, int? TtlMinutes);

public sealed record HoldItemResponse(string Sku, int Quantity);

public sealed record HoldResponse(
    string Id,
    string Status,
    IReadOnlyList<HoldItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ReleasedAt)
{
    public static HoldResponse From(Hold hold) => new(
        hold.Id,
        hold.Status.ToString(),
        hold.Items.Select(i => new HoldItemResponse(i.Sku, i.Quantity)).ToList(),
        hold.CreatedAt,
        hold.ExpiresAt,
        hold.ReleasedAt);
}

public sealed record InventoryItemResponse(string Sku, int Available, int Held, int Total);

public sealed record InventoryPage(IReadOnlyList<InventoryItemResponse> Items, int Skip, int Take, long TotalCount);
