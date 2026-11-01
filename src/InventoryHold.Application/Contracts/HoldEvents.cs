namespace InventoryHold.Application.Contracts;

public sealed record HoldCreated(string HoldId, IReadOnlyList<HoldItemResponse> Items, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record HoldReleased(string HoldId, DateTimeOffset ReleasedAt);

public sealed record HoldExpired(string HoldId, DateTimeOffset ExpiredAt);
