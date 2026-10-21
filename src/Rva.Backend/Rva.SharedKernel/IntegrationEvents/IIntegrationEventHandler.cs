namespace Rva.SharedKernel.IntegrationEvents;

/// <summary>
/// Handles one integration event type. Lives in the consuming module (internal) and is registered in its
/// Add&lt;Area&gt;Module method; the host resolves every handler for an event's type in a fresh DI scope.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
