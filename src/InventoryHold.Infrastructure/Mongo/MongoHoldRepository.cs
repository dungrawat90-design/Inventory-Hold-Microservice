using InventoryHold.Application.Abstractions;
using InventoryHold.Domain;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal sealed class MongoHoldRepository(MongoCollections collections) : IHoldRepository
{
    private static readonly string Active = HoldStatus.Active.ToString();

    public Task InsertAsync(Hold hold, CancellationToken cancellationToken) =>
        collections.Holds.InsertOneAsync(ToDocument(hold), cancellationToken: cancellationToken);

    public async Task<Hold?> GetAsync(string holdId, CancellationToken cancellationToken)
    {
        var document = await collections.Holds
            .Find(Builders<HoldDocument>.Filter.Eq(d => d.Id, holdId))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : ToDomain(document);
    }

    public async Task<Hold?> TryTransitionAsync(string holdId, HoldStatus target, DateTimeOffset at, CancellationToken cancellationToken)
    {
        // The Active filter makes the transition a compare-and-set: of several callers racing for the same hold
        // (release, release, expiry worker) exactly one gets the document back and goes on to restore stock.
        var filter = Builders<HoldDocument>.Filter.Eq(d => d.Id, holdId)
            & Builders<HoldDocument>.Filter.Eq(d => d.Status, Active);
        var update = Builders<HoldDocument>.Update
            .Set(d => d.Status, target.ToString())
            .Set(d => d.ReleasedAt, at.UtcDateTime);

        var updated = await collections.Holds.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<HoldDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        return updated is null ? null : ToDomain(updated);
    }

    public async Task<IReadOnlyList<Hold>> FindDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        var filter = Builders<HoldDocument>.Filter.Eq(d => d.Status, Active)
            & Builders<HoldDocument>.Filter.Lte(d => d.ExpiresAt, now.UtcDateTime);

        var documents = await collections.Holds
            .Find(filter)
            .SortBy(d => d.ExpiresAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return documents.Select(ToDomain).ToList();
    }

    private static HoldDocument ToDocument(Hold hold) => new()
    {
        Id = hold.Id,
        Items = hold.Items.Select(i => new HoldItemDocument { Sku = i.Sku, Quantity = i.Quantity }).ToList(),
        Status = hold.Status.ToString(),
        CreatedAt = hold.CreatedAt.UtcDateTime,
        ExpiresAt = hold.ExpiresAt.UtcDateTime,
        ReleasedAt = hold.ReleasedAt?.UtcDateTime,
    };

    private static Hold ToDomain(HoldDocument document) => new(
        document.Id,
        document.Items.Select(i => new HoldItem(i.Sku, i.Quantity)).ToList(),
        Enum.Parse<HoldStatus>(document.Status),
        new DateTimeOffset(DateTime.SpecifyKind(document.CreatedAt, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(document.ExpiresAt, DateTimeKind.Utc)),
        document.ReleasedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(document.ReleasedAt.Value, DateTimeKind.Utc)));
}
