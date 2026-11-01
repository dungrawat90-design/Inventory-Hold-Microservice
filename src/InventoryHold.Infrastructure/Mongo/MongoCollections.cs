using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

internal sealed class MongoCollections(IMongoDatabase database)
{
    public const string InventoryName = "inventory";
    public const string HoldsName = "holds";

    public IMongoCollection<InventoryDocument> Inventory { get; } = database.GetCollection<InventoryDocument>(InventoryName);

    public IMongoCollection<HoldDocument> Holds { get; } = database.GetCollection<HoldDocument>(HoldsName);
}
