using FluentAssertions;
using FluentValidation;
using InventoryHold.Application.Contracts;
using InventoryHold.Domain;
using InventoryHold.Tests.Support;

namespace InventoryHold.Tests;

public class CreateHoldTests
{
    [Fact]
    public async Task CreateAsync_EnoughStock_MovesQuantityFromAvailableToHeld()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 10);

        var hold = await h.Service.CreateAsync(Harness.Request(("SKU-1", 3)), CancellationToken.None);

        hold.Status.Should().Be("Active");
        hold.Items.Should().ContainSingle().Which.Should().Be(new HoldItemResponse("SKU-1", 3));
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 7, 3));
    }

    [Fact]
    public async Task CreateAsync_NoTtl_UsesDefaultOfFifteenMinutes()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 1);

        var hold = await h.Service.CreateAsync(Harness.Request(("SKU-1", 1)), CancellationToken.None);

        hold.ExpiresAt.Should().Be(h.Time.GetUtcNow().AddMinutes(15));
    }

    [Fact]
    public async Task CreateAsync_CustomTtl_UsesRequestedLifetime()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 1);
        var request = Harness.Request(("SKU-1", 1)) with { TtlMinutes = 40 };

        var hold = await h.Service.CreateAsync(request, CancellationToken.None);

        hold.ExpiresAt.Should().Be(h.Time.GetUtcNow().AddMinutes(40));
    }

    [Fact]
    public async Task CreateAsync_NotEnoughStock_ThrowsAndLeavesStockUnchanged()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 2);

        var act = () => h.Service.CreateAsync(Harness.Request(("SKU-1", 3)), CancellationToken.None);

        (await act.Should().ThrowAsync<InsufficientStockException>()).Which.Sku.Should().Be("SKU-1");
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 2, 0));
        h.Events.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ExactlyAllStock_LeavesAvailableAtZero()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 4);

        await h.Service.CreateAsync(Harness.Request(("SKU-1", 4)), CancellationToken.None);

        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 0, 4));
    }

    [Fact]
    public async Task CreateAsync_UnknownSku_ThrowsSkuNotFound()
    {
        var h = new Harness();

        var act = () => h.Service.CreateAsync(Harness.Request(("NOPE", 1)), CancellationToken.None);

        (await act.Should().ThrowAsync<SkuNotFoundException>()).Which.Sku.Should().Be("NOPE");
    }

    [Fact]
    public async Task CreateAsync_SecondSkuFails_RestoresTheFirstSku()
    {
        var h = new Harness();
        h.Inventory.Seed("A-1", 5);
        h.Inventory.Seed("B-1", 1);

        var act = () => h.Service.CreateAsync(Harness.Request(("A-1", 2), ("B-1", 2)), CancellationToken.None);

        await act.Should().ThrowAsync<InsufficientStockException>();
        h.Inventory.Get("A-1").Should().Be(new InventoryItem("A-1", 5, 0));
        h.Inventory.Get("B-1").Should().Be(new InventoryItem("B-1", 1, 0));
    }

    [Fact]
    public async Task CreateAsync_HoldInsertFails_RestoresReservedStock()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 5);
        h.Holds.FailInsert = true;

        var act = () => h.Service.CreateAsync(Harness.Request(("SKU-1", 2)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 5, 0));
    }

    [Fact]
    public async Task CreateAsync_DuplicateSkus_AreMergedIntoOneLine()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 10);

        var hold = await h.Service.CreateAsync(Harness.Request(("SKU-1", 2), ("SKU-1", 3)), CancellationToken.None);

        hold.Items.Should().ContainSingle().Which.Quantity.Should().Be(5);
        h.Inventory.Get("SKU-1").Held.Should().Be(5);
    }

    [Fact]
    public async Task CreateAsync_Success_InvalidatesCacheAndPublishesHoldCreated()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 5);

        var hold = await h.Service.CreateAsync(Harness.Request(("SKU-1", 1)), CancellationToken.None);

        h.Cache.Invalidations.Should().Be(1);
        h.Events.Published.Should().ContainSingle().Which.Should().BeOfType<HoldCreated>()
            .Which.HoldId.Should().Be(hold.Id);
    }

    [Fact]
    public async Task CreateAsync_BrokerDown_StillCreatesTheHold()
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 5);
        h.Events.Fail = true;

        var hold = await h.Service.CreateAsync(Harness.Request(("SKU-1", 1)), CancellationToken.None);

        (await h.Holds.GetAsync(hold.Id, CancellationToken.None)).Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task CreateAsync_InvalidRequest_ThrowsValidationAndTouchesNothing(CreateHoldRequest request)
    {
        var h = new Harness();
        h.Inventory.Seed("SKU-1", 5);

        var act = () => h.Service.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        h.Inventory.Get("SKU-1").Should().Be(new InventoryItem("SKU-1", 5, 0));
    }

    public static TheoryData<CreateHoldRequest> InvalidRequests => new()
    {
        new CreateHoldRequest(null, null),
        new CreateHoldRequest([], null),
        new CreateHoldRequest([new HoldItemRequest("SKU-1", 0)], null),
        new CreateHoldRequest([new HoldItemRequest("SKU-1", -2)], null),
        new CreateHoldRequest([new HoldItemRequest(null, 1)], null),
        new CreateHoldRequest([new HoldItemRequest("bad sku!", 1)], null),
        new CreateHoldRequest([new HoldItemRequest("SKU-1", 1)], 0),
        new CreateHoldRequest([new HoldItemRequest("SKU-1", 1)], 61),
    };

    [Fact]
    public async Task CreateAsync_ManyConcurrentRequestsForLastUnits_NeverOversells()
    {
        var h = new Harness();
        h.Inventory.Seed("HOT-1", 10);

        var attempts = Enumerable.Range(0, 60)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await h.Service.CreateAsync(Harness.Request(("HOT-1", 1)), CancellationToken.None);
                    return true;
                }
                catch (InsufficientStockException)
                {
                    return false;
                }
            }))
            .ToList();

        var results = await Task.WhenAll(attempts);

        results.Count(r => r).Should().Be(10);
        h.Inventory.Get("HOT-1").Should().Be(new InventoryItem("HOT-1", 0, 10));
    }
}
