namespace InventoryHold.Domain;

public enum HoldStatus
{
    Active,
    Released,
    Expired,
}

public sealed record HoldItem(string Sku, int Quantity);

public sealed record Hold(
    string Id,
    IReadOnlyList<HoldItem> Items,
    HoldStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ReleasedAt)
{
    public bool IsDue(DateTimeOffset now) => Status == HoldStatus.Active && ExpiresAt <= now;
}
