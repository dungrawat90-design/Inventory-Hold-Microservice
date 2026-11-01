using InventoryHold.Infrastructure.Mongo;
using MongoDB.Driver;

namespace InventoryHold.IntegrationTests;

/// <summary>
/// Talks to the MongoDB started by <c>docker compose up</c> (published on localhost:27017), in a separate
/// database that is dropped at the start of every run so it never touches the API's demo data.
/// Override the address with the MONGO_TEST_CONNECTION environment variable.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    internal const string TestDatabase = "inventory_hold_tests";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable("MONGO_TEST_CONNECTION") ?? "mongodb://localhost:27017";

    internal MongoCollections Collections { get; private set; } = null!;

    internal MongoInventoryRepository Inventory { get; private set; } = null!;

    internal MongoHoldRepository Holds { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var settings = MongoClientSettings.FromConnectionString(ConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);

        try
        {
            await client.DropDatabaseAsync(TestDatabase);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach MongoDB at {ConnectionString}. Start the stack first with 'docker compose up --build'.",
                ex);
        }

        Collections = new MongoCollections(client.GetDatabase(TestDatabase));
        Inventory = new MongoInventoryRepository(Collections);
        Holds = new MongoHoldRepository(Collections);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    internal async Task SeedAsync(string sku, int available) =>
        await Collections.Inventory.InsertOneAsync(new InventoryDocument { Sku = sku, Available = available, Held = 0 });
}
