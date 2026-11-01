using System.Net.Http.Headers;
using System.Text;
using InventoryHold.Infrastructure.Mongo;
using MongoDB.Driver;

namespace InventoryHold.IntegrationTests;

/// <summary>
/// Connects to the running docker compose stack. Defaults match docker-compose.yml, so after
/// <c>docker compose up --build</c> no configuration is needed. Override with STACK_API_URL, STACK_API_KEY,
/// STACK_MONGO_CONNECTION, STACK_MONGO_DATABASE, RABBITMQ_UI_URL, RABBITMQ_USER and RABBITMQ_PASSWORD.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private static string Setting(string name, string fallback) => Environment.GetEnvironmentVariable(name) ?? fallback;

    public HttpClient Api { get; } = new()
    {
        BaseAddress = new Uri(Setting("STACK_API_URL", "http://localhost:5080")),
        Timeout = TimeSpan.FromSeconds(30),
    };

    public HttpClient RabbitManagement { get; } = new()
    {
        BaseAddress = new Uri(Setting("RABBITMQ_UI_URL", "http://localhost:15672")),
        Timeout = TimeSpan.FromSeconds(15),
    };

    internal MongoCollections Collections { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Api.DefaultRequestHeaders.Add("X-Api-Key", Setting("STACK_API_KEY", "dev-only-api-key-0123456789"));

        var credentials = $"{Setting("RABBITMQ_USER", "hold")}:{Setting("RABBITMQ_PASSWORD", "dev-only-rabbit-password")}";
        RabbitManagement.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));

        // The tests seed their own uniquely named SKUs straight into the API's database.
        var client = new MongoClient(Setting("STACK_MONGO_CONNECTION", "mongodb://localhost:27017"));
        Collections = new MongoCollections(client.GetDatabase(Setting("STACK_MONGO_DATABASE", "inventory_hold")));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Api.Dispose();
        RabbitManagement.Dispose();
        return Task.CompletedTask;
    }

    internal async Task<string> SeedSkuAsync(int available)
    {
        var sku = $"IT-{Guid.NewGuid():N}"[..20];
        await Collections.Inventory.InsertOneAsync(new InventoryDocument { Sku = sku, Available = available, Held = 0 });
        return sku;
    }
}
