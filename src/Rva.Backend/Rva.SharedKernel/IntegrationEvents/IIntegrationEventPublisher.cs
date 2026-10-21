namespace Rva.SharedKernel.IntegrationEvents;

/// <summary>
/// Hands an integration event to the host for delivery to every handler of its type. Implemented by Rva.Host;
/// modules depend on this interface only. Delivery is asynchronous: returning does not mean any handler has run.
/// </summary>
public interface IIntegrationEventPublisher
{
    ValueTask PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
