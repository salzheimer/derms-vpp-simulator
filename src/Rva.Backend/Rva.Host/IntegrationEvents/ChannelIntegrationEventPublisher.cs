using System.Threading.Channels;
using Rva.SharedKernel.IntegrationEvents;

namespace Rva.Host.IntegrationEvents;

/// <summary>
/// Queues published events on an in-memory channel; <see cref="IntegrationEventDispatcher"/> delivers them.
/// Delivery is best-effort: events still queued when the process stops are lost. Reliable delivery comes from
/// each module's outbox (relay added with the first publishing module, Phase 1 Step 1).
/// </summary>
internal sealed class ChannelIntegrationEventPublisher(Channel<IIntegrationEvent> channel) : IIntegrationEventPublisher
{
    public ValueTask PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return channel.Writer.WriteAsync(integrationEvent, cancellationToken);
    }
}
