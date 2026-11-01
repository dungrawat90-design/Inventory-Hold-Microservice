using FluentAssertions;
using InventoryHold.Application;
using InventoryHold.Application.Abstractions;
using InventoryHold.Infrastructure;
using InventoryHold.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace InventoryHold.Tests;

public class HoldExpiryWorkerTests
{
    [Fact]
    public async Task Worker_AfterTheHoldLifetimePasses_ExpiresItAndRestoresStock()
    {
        var harness = new Harness();
        harness.Inventory.Seed("SKU-1", 10);
        var hold = await harness.Service.CreateAsync(Harness.Request(("SKU-1", 4)), CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSingleton<IInventoryRepository>(harness.Inventory);
        services.AddSingleton<IHoldRepository>(harness.Holds);
        services.AddSingleton<IInventoryCache>(harness.Cache);
        services.AddSingleton<IHoldEventPublisher>(harness.Events);
        services.AddSingleton<TimeProvider>(harness.Time);
        services.AddSingleton(Options.Create(new HoldOptions { ExpiryIntervalSeconds = 15 }));
        services.AddSingleton<FluentValidation.IValidator<InventoryHold.Application.Contracts.CreateHoldRequest>>(
            sp => new CreateHoldRequestValidator(sp.GetRequiredService<IOptions<HoldOptions>>()));
        services.AddLogging();
        services.AddScoped<HoldService>();
        await using var provider = services.BuildServiceProvider();

        var worker = new HoldExpiryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<HoldOptions>>(),
            harness.Time,
            NullLogger<HoldExpiryWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            harness.Time.Advance(TimeSpan.FromMinutes(16));
            await WaitUntilAsync(async () => (await harness.Holds.GetAsync(hold.Id, CancellationToken.None))!.Status == InventoryHold.Domain.HoldStatus.Expired);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        harness.Inventory.Get("SKU-1").Should().Be(new InventoryHold.Domain.InventoryItem("SKU-1", 10, 0));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
