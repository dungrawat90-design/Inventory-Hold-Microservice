using InventoryHold.Application.Abstractions;
using InventoryHold.Domain;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal sealed class MongoInventoryRepository(MongoCollections collections) : IInventoryRepository
{
    public async Task<ReserveResult> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        // One conditional update: the filter and the decrement are applied atomically by MongoDB, so two
        // requests can never both take the last units and stock can never go negative.
        var filter = Builders<InventoryDocument>.Filter.Eq(d => d.Sku, sku)
            & Builders<InventoryDocument>.Filter.Gte(d => d.Available, quantity);
        var update = Builders<InventoryDocument>.Update
            .Inc(d => d.Available, -quantity)
            .Inc(d => d.Held, quantity);

        var updated = await collections.Inventory.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<InventoryDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        if (updated is not null)
        {
            return ReserveResult.Reserved;
        }

        var exists = await collections.Inventory
            .Find(Builders<InventoryDocument>.Filter.Eq(d => d.Sku, sku))
            .Limit(1)
            .AnyAsync(cancellationToken);

        return exists ? ReserveResult.Insufficient : ReserveResult.UnknownSku;
    }

    public async Task RestoreAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        var filter = Builders<InventoryDocument>.Filter.Eq(d => d.Sku, sku)
            & Builders<InventoryDocument>.Filter.Gte(d => d.Held, quantity);
        var update = Builders<InventoryDocument>.Update
            .Inc(d => d.Available, quantity)
            .Inc(d => d.Held, -quantity);

        var updated = await collections.Inventory.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<InventoryDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        if (updated is null)
        {
            throw new InventoryInconsistencyException(sku);
        }
    }

    public async Task<(IReadOnlyList<InventoryItem> Items, long TotalCount)> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var documents = await collections.Inventory
            .Find(FilterDefinition<InventoryDocument>.Empty)
            .SortBy(d => d.Sku)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(cancellationToken);

        var total = await collections.Inventory.CountDocumentsAsync(FilterDefinition<InventoryDocument>.Empty, cancellationToken: cancellationToken);

        IReadOnlyList<InventoryItem> items = documents.Select(d => new InventoryItem(d.Sku, d.Available, d.Held)).ToList();
        return (items, total);
    }
}
