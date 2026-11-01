using InventoryHold.Application.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace InventoryHold.Infrastructure.Messaging;

/// <summary>
/// Reference consumer. Other services (orders, notifications) would add their own consumers for these events.
/// Messages that still fail after the retries configured on the bus end up in the endpoint's _error queue.
/// </summary>
internal sealed class HoldEventLogConsumer(ILogger<HoldEventLogConsumer> logger) :
    IConsumer<HoldCreated>,
    IConsumer<HoldReleased>,
    IConsumer<HoldExpired>
{
    public Task Consume(ConsumeContext<HoldCreated> context)
    {
        logger.LogInformation(
            "Hold created: {HoldId} with {ItemCount} items, expires {ExpiresAt}",
            context.Message.HoldId,
            context.Message.Items.Count,
            context.Message.ExpiresAt);
        return Task.CompletedTask;
    }

    public Task Consume(ConsumeContext<HoldReleased> context)
    {
        logger.LogInformation("Hold released: {HoldId}", context.Message.HoldId);
        return Task.CompletedTask;
    }

    public Task Consume(ConsumeContext<HoldExpired> context)
    {
        logger.LogInformation("Hold expired: {HoldId}", context.Message.HoldId);
        return Task.CompletedTask;
    }
}
