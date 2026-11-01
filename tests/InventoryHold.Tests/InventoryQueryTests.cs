using FluentAssertions;
using InventoryHold.Application;
using InventoryHold.Application.Contracts;
using InventoryHold.Tests.Support;

namespace InventoryHold.Tests;

public class InventoryQueryTests
{
    [Fact]
    public async Task GetInventoryAsync_ReturnsAvailableHeldAndTotal()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 10);
        await h.Service.CreateAsync(Harness.Request(("SKU-1", 4)), CancellationToken.None);

        var page = await h.Service.GetInventoryAsync(null, null, CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Should().Be(new InventoryItemResponse("SKU-1", 6, 4, 10));
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetInventoryAsync_SecondCall_IsServedFromCache()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 10);

        await h.Service.GetInventoryAsync(null, null, CancellationToken.None);
        await h.Service.GetInventoryAsync(null, null, CancellationToken.None);

        h.Inventory.ListCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetInventoryAsync_AfterHoldCreated_ReflectsNewStock()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 10);
        await h.Service.GetInventoryAsync(null, null, CancellationToken.None);

        await h.Service.CreateAsync(Harness.Request(("SKU-1", 2)), CancellationToken.None);
        var page = await h.Service.GetInventoryAsync(null, null, CancellationToken.None);

        page.Items.Single().Available.Should().Be(8);
    }

    [Fact]
    public async Task GetInventoryAsync_AfterRelease_ReflectsRestoredStock()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 10);
        var hold = await h.Service.CreateAsync(Harness.Request(("SKU-1", 2)), CancellationToken.None);
        await h.Service.GetInventoryAsync(null, null, CancellationToken.None);

        await h.Service.ReleaseAsync(hold.Id, CancellationToken.None);
        var page = await h.Service.GetInventoryAsync(null, null, CancellationToken.None);

        page.Items.Single().Should().Be(new InventoryItemResponse("SKU-1", 10, 0, 10));
    }

    [Fact]
    public async Task GetInventoryAsync_TakeAboveMaximum_IsClamped()
    {
        var h = new Harness(new HoldOptions { MaxInventoryPageSize = 5 });
        h.Inventory.Seed("SKU-1", 1);

        var page = await h.Service.GetInventoryAsync(0, 1000, CancellationToken.None);

        page.Take.Should().Be(5);
    }

    [Fact]
    public async Task GetInventoryAsync_NegativeSkipAndNoTake_UsesDefaults()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 1);

        var page = await h.Service.GetInventoryAsync(-3, null, CancellationToken.None);

        (page.Skip, page.Take).Should().Be((0, 50));
    }

    [Fact]
    public async Task GetInventoryAsync_Paging_ReturnsRequestedSlice()
    {
        var h = new Harness();
        foreach (var sku in new[] { "A", "B", "C" })
        {
            h.Inventory.Seed(sku, 1);
        }

        var page = await h.Service.GetInventoryAsync(1, 1, CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Sku.Should().Be("B");
        page.TotalCount.Should().Be(3);
    }
}
