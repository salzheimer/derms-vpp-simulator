namespace Rva.SharedKernel.IntegrationEvents;

/// <summary>
/// Marker for an event one module publishes for others. Events are immutable records declared in the
/// publishing module's .Contracts project.
/// </summary>
public interface IIntegrationEvent;
