using FluentAssertions;
using InventoryHold.Application.Contracts;
using InventoryHold.Domain;
using InventoryHold.Tests.Support;

namespace InventoryHold.Tests;

public class ReleaseAndExpiryTests
{
    private static async Task<(Harness Harness, HoldResponse Hold)> HarnessWithActiveHoldAsync(int quantity = 3)
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 10);
        var hold = await h.Service.CreateAsync(Harness.Request(("SKU-1", quantity)), CancellationToken.None);
        return (h, hold);
    }

    [Fact]
    public async Task ReleaseAsync_ActiveHold_RestoresStockAndMarksReleased()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();

        var released = await h.Service.ReleaseAsync(hold.Id, CancellationToken.None);

        released.Status.Should().Be("Released");
        released.ReleasedAt.Should().Be(h.Time.GetUtcNow());
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 10, 0));
    }

    [Fact]
    public async Task ReleaseAsync_ActiveHold_PublishesHoldReleased()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();

        await h.Service.ReleaseAsync(hold.Id, CancellationToken.None);

        h.Events.Published.OfType<HoldReleased>().Should().ContainSingle().Which.HoldId.Should().Be(hold.Id);
    }

    [Fact]
    public async Task ReleaseAsync_CalledTwice_RestoresStockOnlyOnce()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();

        await h.Service.ReleaseAsync(hold.Id, CancellationToken.None);
        var second = await h.Service.ReleaseAsync(hold.Id, CancellationToken.None);

        second.Status.Should().Be("Released");
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 10, 0));
        h.Events.Published.OfType<HoldReleased>().Should().HaveCount(1);
    }

    [Fact]
    public async Task ReleaseAsync_ManyConcurrentCalls_RestoreStockExactlyOnce()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync(4);

        await Task.WhenAll(Enumerable.Range(0, 25)
            .Select(_ => Task.Run(() => h.Service.ReleaseAsync(hold.Id, CancellationToken.None))));

        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 10, 0));
    }

    [Fact]
    public async Task ReleaseAsync_UnknownHold_ThrowsHoldNotFound()
    {
        var h = new Harness();

        var act = () => h.Service.ReleaseAsync("missing", CancellationToken.None);

        await act.Should().ThrowAsync<HoldNotFoundException>();
    }

    [Fact]
    public async Task ReleaseAsync_ExpiredHold_ReturnsExpiredAndDoesNotRestoreAgain()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();
        h.Time.Advance(TimeSpan.FromMinutes(16));
        await h.Service.ExpireDueAsync(CancellationToken.None);

        var result = await h.Service.ReleaseAsync(hold.Id, CancellationToken.None);

        result.Status.Should().Be("Expired");
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 10, 0));
    }

    [Fact]
    public async Task GetAsync_ExistingHold_ReturnsIt()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();

        var fetched = await h.Service.GetAsync(hold.Id, CancellationToken.None);

        fetched.Should().BeEquivalentTo(hold);
    }

    [Fact]
    public async Task GetAsync_UnknownHold_ThrowsHoldNotFound()
    {
        var h = new Harness();

        var act = () => h.Service.GetAsync("missing", CancellationToken.None);

        await act.Should().ThrowAsync<HoldNotFoundException>();
    }

    [Fact]
    public async Task ExpireDueAsync_AfterExpiryTime_ExpiresHoldAndRestoresStock()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();
        h.Time.Advance(TimeSpan.FromMinutes(15));

        var expired = await h.Service.ExpireDueAsync(CancellationToken.None);

        expired.Should().Be(1);
        (await h.Service.GetAsync(hold.Id, CancellationToken.None)).Status.Should().Be("Expired");
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 10, 0));
        h.Events.Published.OfType<HoldExpired>().Should().ContainSingle().Which.HoldId.Should().Be(hold.Id);
    }

    [Fact]
    public async Task ExpireDueAsync_BeforeExpiryTime_ChangesNothing()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();
        h.Time.Advance(TimeSpan.FromMinutes(14));

        var expired = await h.Service.ExpireDueAsync(CancellationToken.None);

        expired.Should().Be(0);
        (await h.Service.GetAsync(hold.Id, CancellationToken.None)).Status.Should().Be("Active");
        h.Inventory.Get("SKU-1").Held.Should().Be(3);
    }

    [Fact]
    public async Task ExpireDueAsync_HoldAlreadyReleased_DoesNotRestoreAgain()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();
        await h.Service.ReleaseAsync(hold.Id, CancellationToken.None);
        h.Time.Advance(TimeSpan.FromMinutes(30));

        var expired = await h.Service.ExpireDueAsync(CancellationToken.None);

        expired.Should().Be(0);
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 10, 0));
    }

    [Fact]
    public async Task ExpireDueAsync_RunTwice_ExpiresOnlyOnce()
    {
        var (h, _) = await HarnessWithActiveHoldAsync();
        h.Time.Advance(TimeSpan.FromMinutes(20));

        var first = await h.Service.ExpireDueAsync(CancellationToken.None);
        var second = await h.Service.ExpireDueAsync(CancellationToken.None);

        (first, second).Should().Be((1, 0));
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 10, 0));
    }

    [Fact]
    public async Task ExpireDueAsync_OneHoldFailsToRestore_StillExpiresTheOthers()
    {
        var h = new Harness();
        h.Inventory.Seed("BAD-1", 5);
        h.Inventory.Seed("OK-1", 5);
        var bad = await h.Service.CreateAsync(Harness.Request(("BAD-1", 1)), CancellationToken.None);
        var good = await h.Service.CreateAsync(Harness.Request(("OK-1", 1)), CancellationToken.None);
        h.Inventory.FailRestoreFor.Add("BAD-1");
        h.Time.Advance(TimeSpan.FromMinutes(16));

        var expired = await h.Service.ExpireDueAsync(CancellationToken.None);

        expired.Should().Be(1);
        (await h.Service.GetAsync(good.Id, CancellationToken.None)).Status.Should().Be("Expired");
        h.Inventory.Get("OK-1").Should().Be(new InventoryItem("OK-1", 5, 0));
        bad.Id.Should().NotBe(good.Id);
    }

    [Fact]
    public async Task ReleaseAsync_RestoreFails_ThrowsSoTheCallerSeesTheFailure()
    {
        var (h, hold) = await HarnessWithActiveHoldAsync();
        h.Inventory.FailRestoreFor.Add("SKU-1");

        var act = () => h.Service.ReleaseAsync(hold.Id, CancellationToken.None);

        await act.Should().ThrowAsync<AggregateException>();
    }
}
