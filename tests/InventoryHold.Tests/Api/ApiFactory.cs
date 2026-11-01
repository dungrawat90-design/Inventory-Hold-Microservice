using InventoryHold.Application.Abstractions;
using InventoryHold.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace InventoryHold.Tests.Api;

/// <summary>Runs the real API pipeline (routing, auth, validation, error mapping) over in-memory stores.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-0123456789";

    public InMemoryInventoryRepository Inventory { get; } = new();

    public InMemoryHoldRepository Holds { get; } = new();

    public RecordingEventPublisher Events { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:ApiKey"] = ApiKey,
            ["Mongo:ConnectionString"] = "mongodb://unused:27017",
            ["Redis:ConnectionString"] = "unused:6379",
            ["RabbitMq:Host"] = "unused",
            ["RabbitMq:Username"] = "unused",
            ["RabbitMq:Password"] = "unused",
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
        }));

        builder.ConfigureTestServices(services =>
        {
            // No real broker, database or cache in these tests, and no background work.
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IInventoryRepository>();
            services.RemoveAll<IHoldRepository>();
            services.RemoveAll<IInventoryCache>();
            services.RemoveAll<IHoldEventPublisher>();
            services.AddSingleton<IInventoryRepository>(Inventory);
            services.AddSingleton<IHoldRepository>(Holds);
            services.AddSingleton<IInventoryCache, FakeInventoryCache>();
            services.AddSingleton<IHoldEventPublisher>(Events);
        });
    }
}
