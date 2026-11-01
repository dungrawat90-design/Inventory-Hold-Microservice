using FluentAssertions;
using InventoryHold.Application.Abstractions;
using InventoryHold.Domain;

namespace InventoryHold.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class MongoRepositoryTests : IClassFixture<MongoFixture>
{
    private readonly MongoFixture _mongo;

    public MongoRepositoryTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task TryReserveAsync_ManyConcurrentRequestsForLastUnits_NeverOversells()
    {
        await _mongo.SeedAsync("IT-HOT", 10);

        var results = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => _mongo.Inventory.TryReserveAsync("IT-HOT", 1, CancellationToken.None))));

        results.Count(r => r == ReserveResult.Reserved).Should().Be(10);
        results.Count(r => r == ReserveResult.Insufficient).Should().Be(90);
        var (items, _) = await _mongo.Inventory.ListAsync(0, 200, CancellationToken.None);
        items.Single(i => i.Sku == "IT-HOT").Should().Be(new InventoryItem("IT-HOT", 0, 10));
    }

    [Fact]
    public async Task TryReserveAsync_MoreThanAvailable_LeavesStockUntouched()
    {
        await _mongo.SeedAsync("IT-LOW", 2);

        var result = await _mongo.Inventory.TryReserveAsync("IT-LOW", 3, CancellationToken.None);

        result.Should().Be(ReserveResult.Insufficient);
        var (items, _) = await _mongo.Inventory.ListAsync(0, 200, CancellationToken.None);
        items.Single(i => i.Sku == "IT-LOW").Should().Be(new InventoryItem("IT-LOW", 2, 0));
    }

    [Fact]
    public async Task TryReserveAsync_UnknownSku_ReportsUnknown()
    {
        var result = await _mongo.Inventory.TryReserveAsync("IT-MISSING", 1, CancellationToken.None);

        result.Should().Be(ReserveResult.UnknownSku);
    }

    [Fact]
    public async Task RestoreAsync_MoreThanHeld_ThrowsAndLeavesStockUntouched()
    {
        await _mongo.SeedAsync("IT-RESTORE", 5);
        await _mongo.Inventory.TryReserveAsync("IT-RESTORE", 2, CancellationToken.None);

        var act = () => _mongo.Inventory.RestoreAsync("IT-RESTORE", 3, CancellationToken.None);

        await act.Should().ThrowAsync<InventoryInconsistencyException>();
        var (items, _) = await _mongo.Inventory.ListAsync(0, 200, CancellationToken.None);
        items.Single(i => i.Sku == "IT-RESTORE").Should().Be(new InventoryItem("IT-RESTORE", 3, 2));
    }

    [Fact]
    public async Task TryTransitionAsync_ManyConcurrentCallers_ExactlyOneWins()
    {
        var now = DateTimeOffset.UtcNow;
        var hold = new Hold("it-race", [new HoldItem("IT-RACE", 1)], HoldStatus.Active, now, now.AddMinutes(15), null);
        await _mongo.Holds.InsertAsync(hold, CancellationToken.None);

        var results = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(i => Task.Run(() => _mongo.Holds.TryTransitionAsync(
                "it-race",
                i % 2 == 0 ? HoldStatus.Released : HoldStatus.Expired,
                DateTimeOffset.UtcNow,
                CancellationToken.None))));

        results.Count(r => r is not null).Should().Be(1);
    }

    [Fact]
    public async Task FindDueAsync_ReturnsOnlyActiveHoldsPastExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        await _mongo.Holds.InsertAsync(new Hold("it-due", [new HoldItem("S", 1)], HoldStatus.Active, now.AddMinutes(-20), now.AddMinutes(-5), null), CancellationToken.None);
        await _mongo.Holds.InsertAsync(new Hold("it-later", [new HoldItem("S", 1)], HoldStatus.Active, now, now.AddMinutes(10), null), CancellationToken.None);
        await _mongo.Holds.InsertAsync(new Hold("it-done", [new HoldItem("S", 1)], HoldStatus.Released, now.AddMinutes(-20), now.AddMinutes(-5), now.AddMinutes(-10)), CancellationToken.None);

        var due = await _mongo.Holds.FindDueAsync(now, 100, CancellationToken.None);

        due.Select(h => h.Id).Should().Contain("it-due").And.NotContain(["it-later", "it-done"]);
    }

    [Fact]
    public async Task GetAsync_RoundTripsTheHold()
    {
        var created = DateTimeOffset.UtcNow;
        var hold = new Hold("it-roundtrip", [new HoldItem("A", 2), new HoldItem("B", 5)], HoldStatus.Active, created, created.AddMinutes(15), null);
        await _mongo.Holds.InsertAsync(hold, CancellationToken.None);

        var loaded = await _mongo.Holds.GetAsync("it-roundtrip", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Items.Should().Equal(hold.Items);
        loaded.Status.Should().Be(HoldStatus.Active);
        loaded.ExpiresAt.Should().BeCloseTo(hold.ExpiresAt, TimeSpan.FromMilliseconds(5));
    }
}
