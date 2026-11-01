using InventoryHold.Application.Abstractions;
using MassTransit;

namespace InventoryHold.Infrastructure.Messaging;

internal sealed class MassTransitHoldEventPublisher(IPublishEndpoint publishEndpoint) : IHoldEventPublisher
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public async Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken)
        where TEvent : class
    {
        // A broker outage must not hold a request open: give up after a few seconds and let the caller log it.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishTimeout);
        await publishEndpoint.Publish(message, timeout.Token);
    }
}
