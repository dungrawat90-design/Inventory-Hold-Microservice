using InventoryHold.Application;
using InventoryHold.Application.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace InventoryHold.Tests.Support;

public sealed class Harness
{
    public Harness(HoldOptions? options = null)
    {
        var settings = options ?? new HoldOptions();
        var optionsWrapper = Options.Create(settings);
        Service = new HoldService(
            Inventory,
            Holds,
            Cache,
            Events,
            new CreateHoldRequestValidator(optionsWrapper),
            optionsWrapper,
            Time,
            NullLogger<HoldService>.Instance);
    }

    public InMemoryInventoryRepository Inventory { get; } = new();

    public InMemoryHoldRepository Holds { get; } = new();

    public FakeInventoryCache Cache { get; } = new();

    public RecordingEventPublisher Events { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));

    public HoldService Service { get; }

    public static CreateHoldRequest Request(params (string Sku, int Quantity)[] items) =>
        new(items.Select(i => new HoldItemRequest(i.Sku, i.Quantity)).ToList(), null);
}
