using MongoDB.Bson.Serialization.Attributes;

namespace InventoryHold.Infrastructure.Mongo;

internal sealed class InventoryDocument
{
    [BsonId]
    public string Sku { get; set; } = string.Empty;

    public int Available { get; set; }

    public int Held { get; set; }
}

internal sealed class HoldItemDocument
{
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

internal sealed class HoldDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public List<HoldItemDocument> Items { get; set; } = [];

    public string Status { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ExpiresAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ReleasedAt { get; set; }
}
